using System.Data;
using System.Text.Json;
using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteMeetingCompletionRepository(
    SqliteConnectionFactory connections,
    IMeetingReportGenerator reportGenerator) : IMeetingCompletionRepository
{
    public async Task<MeetingCommandResult> CompleteAsync(
        Guid meetingId,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var commandState = await connection.QuerySingleOrDefaultAsync<CompletionCommandRow>(
            """
            SELECT m.phase AS Phase, m.version AS Version, m.facilitator_id AS FacilitatorId,
                   member.status AS MemberStatus
            FROM meetings m
            LEFT JOIN members member ON member.meeting_id = m.id AND member.user_id = @UserId
            WHERE m.id = @MeetingId;
            """,
            new { MeetingId = meetingId.ToString(), UserId = facilitatorId.ToString() },
            transaction);
        if (commandState is null || commandState.MemberStatus is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (commandState.MemberStatus == MemberStatus.Excluded.ToString()
            || commandState.FacilitatorId != facilitatorId.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.Forbidden);
        }

        var replay = await FindReplayAsync(connection, transaction, meetingId, facilitatorId, operationId, fingerprint);
        if (replay is not null)
        {
            return replay;
        }

        if (commandState.Phase != MeetingPhase.Discuss.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidPhase);
        }

        if (commandState.Version != expectedVersion)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        var current = await SqliteMeetingRepository.ReadStateAsync(connection, meetingId)
            ?? throw new InvalidOperationException("Meeting state is missing during completion.");
        var completed = MeetingCompletionFactory.Create(current, nowUtc);
        var pdf = reportGenerator.Generate(completed);
        if (pdf.Length == 0)
        {
            throw new InvalidOperationException("Generated meeting report is empty.");
        }

        var totals = completed.Completion!;
        await connection.ExecuteAsync(
            """
            INSERT INTO meeting_totals (
                meeting_id, closed_at_utc, participant_count, card_count,
                topic_count, vote_count, payload_json)
            VALUES (
                @MeetingId, @ClosedAtUtc, @ParticipantCount, @CardCount,
                @TopicCount, @VoteCount, @PayloadJson);
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                ClosedAtUtc = totals.ClosedAtUtc.ToString("O"),
                totals.ParticipantCount,
                totals.CardCount,
                totals.TopicCount,
                totals.VoteCount,
                PayloadJson = JsonSerializer.Serialize(completed),
            },
            transaction);
        foreach (var topicTotal in completed.TopicTotals!)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO topic_totals (meeting_id, topic_id, total_votes)
                VALUES (@MeetingId, @TopicId, @TotalVotes);
                """,
                new
                {
                    MeetingId = meetingId.ToString(),
                    TopicId = topicTotal.TopicId.ToString(),
                    topicTotal.TotalVotes,
                },
                transaction);
        }

        await connection.ExecuteAsync(
            "INSERT INTO reports (meeting_id, pdf, created_at_utc) VALUES (@MeetingId, @Pdf, @CreatedAtUtc);",
            new
            {
                MeetingId = meetingId.ToString(),
                Pdf = pdf,
                CreatedAtUtc = nowUtc.ToString("O"),
            },
            transaction);
        var cardPrivacy = new { MeetingId = meetingId.ToString(), AllAnonymous = current.Meeting.AnonymousCards };
        await connection.ExecuteAsync(
            """
            DELETE FROM card_authors WHERE meeting_id = @MeetingId
              AND (@AllAnonymous OR card_id IN (SELECT id FROM cards WHERE meeting_id = @MeetingId AND anonymous = 1));
            """,
            cardPrivacy,
            transaction);
        await connection.ExecuteAsync(
            """
            DELETE FROM command_deduplication WHERE meeting_id = @MeetingId AND operation_type = 'CreateCard'
              AND (@AllAnonymous OR anonymous_card = 1);
            """,
            cardPrivacy,
            transaction);

        if (current.Meeting.AnonymousVotes)
        {
            await connection.ExecuteAsync(
                "DELETE FROM votes WHERE meeting_id = @MeetingId;",
                new { MeetingId = meetingId.ToString() },
                transaction);
            await connection.ExecuteAsync(
                "DELETE FROM personal_ballots WHERE meeting_id = @MeetingId;",
                new { MeetingId = meetingId.ToString() },
                transaction);
        }

        await connection.ExecuteAsync(
            "DELETE FROM readiness WHERE meeting_id = @MeetingId;",
            new { MeetingId = meetingId.ToString() },
            transaction);
        var newVersion = expectedVersion + 1;
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET phase = 'Closed', version = @NewVersion,
                invitation_version = invitation_version + 1,
                facilitator_election_deadline_utc = NULL,
                facilitator_generation = facilitator_generation + 1,
                updated_at_utc = @Now
            WHERE id = @MeetingId AND facilitator_id = @FacilitatorId
              AND phase = 'Discuss' AND version = @ExpectedVersion;
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                FacilitatorId = facilitatorId.ToString(),
                ExpectedVersion = expectedVersion,
                NewVersion = newVersion,
                Now = nowUtc.ToString("O"),
            },
            transaction);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO command_deduplication (
                meeting_id, user_id, operation_id, operation_type, fingerprint,
                result_id, result_version, created_at_utc)
            VALUES (
                @MeetingId, @UserId, @OperationId, 'Complete', @Fingerprint,
                NULL, @ResultVersion, @CreatedAtUtc);
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                UserId = facilitatorId.ToString(),
                OperationId = operationId.ToString(),
                Fingerprint = fingerprint,
                ResultVersion = newVersion,
                CreatedAtUtc = nowUtc.ToString("O"),
            },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(MeetingCommandStatus.Success, newVersion);
    }

    public async Task<byte[]?> GetReportAsync(Guid meetingId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<byte[]?>(
            "SELECT pdf FROM reports WHERE meeting_id = @MeetingId;",
            new { MeetingId = meetingId.ToString() });
    }

    private static async Task<MeetingCommandResult?> FindReplayAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid facilitatorId,
        Guid operationId,
        string fingerprint)
    {
        var row = await connection.QuerySingleOrDefaultAsync<CompletionReplayRow>(
            """
            SELECT operation_type AS OperationType, fingerprint AS Fingerprint,
                   result_version AS ResultVersion
            FROM command_deduplication
            WHERE meeting_id = @MeetingId AND user_id = @UserId
              AND operation_id = @OperationId;
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                UserId = facilitatorId.ToString(),
                OperationId = operationId.ToString(),
            },
            transaction);
        if (row is null)
        {
            return null;
        }

        return row.OperationType != "Complete" || row.Fingerprint != fingerprint
            ? new MeetingCommandResult(MeetingCommandStatus.OperationConflict)
            : new MeetingCommandResult(MeetingCommandStatus.Success, row.ResultVersion, IsReplay: true);
    }

    private sealed class CompletionCommandRow
    {
        public required string Phase { get; init; }
        public int Version { get; init; }
        public string? FacilitatorId { get; init; }
        public string? MemberStatus { get; init; }
    }

    private sealed class CompletionReplayRow
    {
        public required string OperationType { get; init; }
        public required string Fingerprint { get; init; }
        public int ResultVersion { get; init; }
    }
}
