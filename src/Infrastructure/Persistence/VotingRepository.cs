using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence;

public sealed class VotingRepository(RelationalConnectionFactory connections) : IVotingRepository
{
    public async Task<VotingCommandResult> SetMyVotesAsync(
        Guid meetingId,
        Guid userId,
        Guid topicId,
        int count,
        int expectedBallotVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connections.BeginMeetingTransactionAsync(connection, meetingId, cancellationToken);
        var state = await connection.QuerySingleOrDefaultAsync<VotingStateRow>(
            """
            SELECT m.phase AS Phase, m.version AS Version, m.facilitator_id AS FacilitatorId,
                   m.vote_mode AS VoteMode, m.vote_percent AS VotePercent,
                   m.vote_minimum AS VoteMinimum, m.vote_fixed AS VoteFixed,
                   m.initial_card_count AS InitialCardCount,
                   member.status AS MemberStatus, ballot.version AS BallotVersion
            FROM meetings m
            LEFT JOIN members member ON member.meeting_id = m.id AND member.user_id = @UserId
            LEFT JOIN personal_ballots ballot ON ballot.meeting_id = m.id AND ballot.user_id = @UserId
            WHERE m.id = @MeetingId;
            """,
            new { MeetingId = meetingId, UserId = userId },
            transaction);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        if (state!.Phase != MeetingPhase.Vote.ToString())
        {
            return new VotingCommandResult(VotingCommandStatus.InvalidPhase);
        }

        if (state.BallotVersion != expectedBallotVersion)
        {
            return new VotingCommandResult(VotingCommandStatus.VersionConflict);
        }

        var topic = await connection.QuerySingleOrDefaultAsync<TopicVoteRow>(
            """
            SELECT t.id AS TopicId, vote.count AS Count
            FROM topics t
            LEFT JOIN votes vote ON vote.meeting_id = t.meeting_id
                AND vote.topic_id = t.id AND vote.user_id = @UserId
            WHERE t.meeting_id = @MeetingId AND t.id = @TopicId;
            """,
            new
            {
                MeetingId = meetingId,
                UserId = userId,
                TopicId = topicId,
            },
            transaction);
        if (topic is null)
        {
            return new VotingCommandResult(VotingCommandStatus.NotFound);
        }

        var settings = MapSettings(state);
        var originalCardCount = state.InitialCardCount
            ?? throw new InvalidOperationException("Voting has no original card count.");
        var budget = settings.BudgetFor(originalCardCount);
        var spent = await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(count), 0) FROM votes WHERE meeting_id = @MeetingId AND user_id = @UserId;",
            new { MeetingId = meetingId, UserId = userId },
            transaction);
        var currentCount = topic.Count ?? 0;
        var remaining = budget - spent;
        if (spent - currentCount + count > budget)
        {
            return new VotingCommandResult(
                VotingCommandStatus.BudgetExceeded,
                state.Version,
                expectedBallotVersion,
                remaining);
        }

        if (currentCount == count)
        {
            return new VotingCommandResult(
                VotingCommandStatus.Success,
                state.Version,
                expectedBallotVersion,
                remaining,
                IsReplay: true);
        }

        if (count == 0)
        {
            await connection.ExecuteAsync(
                "DELETE FROM votes WHERE meeting_id = @MeetingId AND topic_id = @TopicId AND user_id = @UserId;",
                new
                {
                    MeetingId = meetingId,
                    TopicId = topicId,
                    UserId = userId,
                },
                transaction);
        }
        else
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO votes (meeting_id, topic_id, user_id, count)
                VALUES (@MeetingId, @TopicId, @UserId, @Count)
                ON CONFLICT (meeting_id, topic_id, user_id)
                DO UPDATE SET count = excluded.count;
                """,
                new
                {
                    MeetingId = meetingId,
                    TopicId = topicId,
                    UserId = userId,
                    Count = count,
                },
                transaction);
        }

        var newBallotVersion = expectedBallotVersion + 1;
        var ballotUpdated = await connection.ExecuteAsync(
            """
            UPDATE personal_ballots SET version = @NewVersion
            WHERE meeting_id = @MeetingId AND user_id = @UserId AND version = @ExpectedVersion;
            """,
            new
            {
                MeetingId = meetingId,
                UserId = userId,
                NewVersion = newBallotVersion,
                ExpectedVersion = expectedBallotVersion,
            },
            transaction);
        if (ballotUpdated == 0)
        {
            return new VotingCommandResult(VotingCommandStatus.VersionConflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return new VotingCommandResult(
            VotingCommandStatus.Success,
            state.Version,
            newBallotVersion,
            budget - (spent - currentCount + count));
    }

    public async Task<VotingCommandResult> UpdateSettingsAsync(
        Guid meetingId,
        Guid facilitatorId,
        VoteBudgetSettings settings,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connections.BeginMeetingTransactionAsync(connection, meetingId, cancellationToken);
        var state = await connection.QuerySingleOrDefaultAsync<VotingStateRow>(
            """
            SELECT m.phase AS Phase, m.version AS Version, m.facilitator_id AS FacilitatorId,
                   m.vote_mode AS VoteMode, m.vote_percent AS VotePercent,
                   m.vote_minimum AS VoteMinimum, m.vote_fixed AS VoteFixed,
                   m.initial_card_count AS InitialCardCount,
                   member.status AS MemberStatus, ballot.version AS BallotVersion
            FROM meetings m
            LEFT JOIN members member ON member.meeting_id = m.id AND member.user_id = @UserId
            LEFT JOIN personal_ballots ballot ON ballot.meeting_id = m.id AND ballot.user_id = @UserId
            WHERE m.id = @MeetingId;
            """,
            new { MeetingId = meetingId, UserId = facilitatorId },
            transaction);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        if (state!.FacilitatorId != facilitatorId)
        {
            return new VotingCommandResult(VotingCommandStatus.Forbidden);
        }

        if (state.Phase != MeetingPhase.Vote.ToString())
        {
            return new VotingCommandResult(VotingCommandStatus.InvalidPhase);
        }

        if (state.Version != expectedVersion)
        {
            return new VotingCommandResult(VotingCommandStatus.VersionConflict);
        }

        var originalCardCount = state.InitialCardCount
            ?? throw new InvalidOperationException("Voting has no original card count.");
        var budget = settings.BudgetFor(originalCardCount);
        var minimumBudget = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COALESCE(MAX(spent), 0) FROM (
                SELECT user_id, SUM(count) AS spent FROM votes WHERE meeting_id = @MeetingId GROUP BY user_id);
            """,
            new { MeetingId = meetingId },
            transaction);
        if (budget < minimumBudget)
        {
            return new VotingCommandResult(
                VotingCommandStatus.BudgetTooLow,
                state.Version);
        }

        var current = MapSettings(state);
        if (current == settings)
        {
            return new VotingCommandResult(
                VotingCommandStatus.Success,
                state.Version,
                state.BallotVersion ?? 0,
                IsReplay: true);
        }

        var newVersion = expectedVersion + 1;
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings SET
                vote_mode = @VoteMode,
                vote_percent = @VotePercent,
                vote_minimum = @VoteMinimum,
                vote_fixed = @VoteFixed,
                version = @NewVersion,
                updated_at_utc = @Now
            WHERE id = @MeetingId AND facilitator_id = @FacilitatorId
              AND phase = 'Vote' AND version = @ExpectedVersion;
            """,
            new
            {
                MeetingId = meetingId,
                FacilitatorId = facilitatorId,
                VoteMode = settings.Mode.ToString(),
                VotePercent = settings.Percent,
                VoteMinimum = settings.Minimum,
                VoteFixed = settings.Fixed,
                NewVersion = newVersion,
                ExpectedVersion = expectedVersion,
                Now = nowUtc,
            },
            transaction);
        if (affected == 0)
        {
            return new VotingCommandResult(VotingCommandStatus.VersionConflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return new VotingCommandResult(
            VotingCommandStatus.Success,
            newVersion,
            state.BallotVersion ?? 0);
    }

    private static VotingCommandResult? CheckMemberAccess(VotingStateRow? state)
    {
        if (state is null || state.MemberStatus is null)
        {
            return new VotingCommandResult(VotingCommandStatus.NotFound);
        }

        return state.MemberStatus == MemberStatus.Excluded.ToString()
            ? new VotingCommandResult(VotingCommandStatus.Forbidden)
            : null;
    }

    private static VoteBudgetSettings MapSettings(VotingStateRow state) =>
        new(
            Enum.Parse<VoteMode>(state.VoteMode),
            state.VotePercent,
            state.VoteMinimum,
            state.VoteFixed);

    private sealed class VotingStateRow
    {
        public required string Phase { get; init; }
        public int Version { get; init; }
        public Guid? FacilitatorId { get; init; }
        public required string VoteMode { get; init; }
        public int VotePercent { get; init; }
        public int VoteMinimum { get; init; }
        public int? VoteFixed { get; init; }
        public int? InitialCardCount { get; init; }
        public string? MemberStatus { get; init; }
        public int? BallotVersion { get; init; }
    }

    private sealed class TopicVoteRow
    {
        public Guid TopicId { get; init; }
        public int? Count { get; init; }
    }
}
