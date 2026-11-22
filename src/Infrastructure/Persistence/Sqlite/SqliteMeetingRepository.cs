using System.Data;
using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteMeetingRepository(SqliteConnectionFactory connections) : IMeetingRepository
{
    public async Task CreateAsync(
        Meeting meeting,
        IReadOnlyList<MeetingColumn> columns,
        MeetingMember creator,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertMeetingAsync(connection, transaction, meeting, null);
        await connection.ExecuteAsync(
            """
            INSERT INTO members (meeting_id, user_id, status, joined_at_utc, excluded_at_utc)
            VALUES (@MeetingId, @UserId, @Status, @JoinedAtUtc, NULL);
            """,
            new
            {
                MeetingId = creator.MeetingId.ToString(),
                UserId = creator.UserId.ToString(),
                Status = creator.Status.ToString(),
                JoinedAtUtc = creator.JoinedAtUtc.ToString("O"),
            },
            transaction);
        await InsertColumnsAsync(connection, transaction, columns);
        await connection.ExecuteAsync(
            "UPDATE meetings SET facilitator_id = @FacilitatorId WHERE id = @MeetingId;",
            new { FacilitatorId = creator.UserId.ToString(), MeetingId = meeting.Id.ToString() },
            transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<MeetingStateData?> GetStateAsync(
        Guid meetingId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var meetingRow = await connection.QuerySingleOrDefaultAsync<MeetingRow>(
            "SELECT * FROM meetings WHERE id = @MeetingId;",
            new { MeetingId = meetingId.ToString() });
        if (meetingRow is null)
        {
            return null;
        }

        var columns = (await connection.QueryAsync<MeetingColumnRow>(
            "SELECT * FROM columns WHERE meeting_id = @MeetingId ORDER BY position;",
            new { MeetingId = meetingId.ToString() }))
            .Select(row => new MeetingColumn(
                Guid.Parse(row.Id),
                Guid.Parse(row.MeetingId),
                row.Name,
                row.Color,
                row.Position))
            .ToArray();
        var participants = (await connection.QueryAsync<ParticipantRow>(
            """
            SELECT m.user_id AS UserId, u.subject AS Subject, u.display_name AS DisplayName, m.status AS Status
            FROM members m
            JOIN users u ON u.id = m.user_id
            WHERE m.meeting_id = @MeetingId
            ORDER BY m.joined_at_utc, u.id;
            """,
            new { MeetingId = meetingId.ToString() }))
            .Select(row => new MeetingParticipant(
                Guid.Parse(row.UserId),
                row.Subject,
                row.DisplayName,
                Enum.Parse<MemberStatus>(row.Status)))
            .ToArray();
        return new MeetingStateData(MapMeeting(meetingRow), columns, participants);
    }

    public async Task<MeetingMember?> FindMemberAsync(
        Guid meetingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<MemberRow>(
            "SELECT * FROM members WHERE meeting_id = @MeetingId AND user_id = @UserId;",
            new { MeetingId = meetingId.ToString(), UserId = userId.ToString() });
        return row is null
            ? null
            : new MeetingMember(
                Guid.Parse(row.MeetingId),
                Guid.Parse(row.UserId),
                Enum.Parse<MemberStatus>(row.Status),
                SqliteMapping.Time(row.JoinedAtUtc),
                SqliteMapping.NullableTime(row.ExcludedAtUtc));
    }

    public async Task<IReadOnlyList<MeetingSummary>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<MeetingSummaryRow>(
            """
            SELECT m.id AS Id, m.name AS Name, m.phase AS Phase, m.created_at_utc AS CreatedAtUtc
            FROM meetings m
            JOIN members member ON member.meeting_id = m.id AND member.status = 'Active'
            WHERE member.user_id = @UserId
            ORDER BY m.created_at_utc DESC, m.id;
            """,
            new { UserId = userId.ToString() });
        return rows.Select(row => new MeetingSummary(
            Guid.Parse(row.Id),
            row.Name,
            Enum.Parse<MeetingPhase>(row.Phase),
            SqliteMapping.Time(row.CreatedAtUtc))).ToArray();
    }

    public async Task<int> UpdateDraftAsync(
        Meeting meeting,
        IReadOnlyList<MeetingColumn> columns,
        int expectedVersion,
        Guid facilitatorId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await connection.QuerySingleOrDefaultAsync<MeetingCommandRow>(
            "SELECT facilitator_id AS FacilitatorId, phase AS Phase, version AS Version FROM meetings WHERE id = @MeetingId;",
            new { MeetingId = meeting.Id.ToString() },
            transaction);
        if (state is null
            || state.FacilitatorId != facilitatorId.ToString()
            || state.Phase != MeetingPhase.Draft.ToString()
            || state.Version != expectedVersion)
        {
            return 0;
        }

        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings SET
                name = @Name,
                anonymous_cards = @AnonymousCards,
                private_collection = @PrivateCollection,
                anonymous_votes = @AnonymousVotes,
                vote_mode = @VoteMode,
                vote_percent = @VotePercent,
                vote_minimum = @VoteMinimum,
                vote_fixed = @VoteFixed,
                version = version + 1,
                updated_at_utc = @UpdatedAtUtc
            WHERE id = @Id AND version = @ExpectedVersion AND facilitator_id = @FacilitatorId AND phase = 'Draft';
            """,
            new
            {
                meeting.Name,
                AnonymousCards = meeting.AnonymousCards ? 1 : 0,
                PrivateCollection = meeting.PrivateCollection ? 1 : 0,
                AnonymousVotes = meeting.AnonymousVotes ? 1 : 0,
                VoteMode = meeting.VoteSettings.Mode.ToString(),
                VotePercent = meeting.VoteSettings.Percent,
                VoteMinimum = meeting.VoteSettings.Minimum,
                VoteFixed = meeting.VoteSettings.Fixed,
                UpdatedAtUtc = meeting.UpdatedAtUtc.ToString("O"),
                Id = meeting.Id.ToString(),
                ExpectedVersion = expectedVersion,
                FacilitatorId = facilitatorId.ToString(),
            },
            transaction);
        if (affected == 0)
        {
            return 0;
        }

        await connection.ExecuteAsync(
            "DELETE FROM columns WHERE meeting_id = @MeetingId;",
            new { MeetingId = meeting.Id.ToString() },
            transaction);
        await InsertColumnsAsync(connection, transaction, columns);
        await transaction.CommitAsync(cancellationToken);
        return affected;
    }

    public async Task<int> RotateInvitationAsync(
        Guid meetingId,
        Guid facilitatorId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET invitation_version = invitation_version + 1,
                version = version + 1,
                updated_at_utc = @Now
            WHERE id = @MeetingId AND facilitator_id = @FacilitatorId AND phase <> 'Closed';
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                FacilitatorId = facilitatorId.ToString(),
                Now = DateTimeOffset.UtcNow.ToString("O"),
            },
            transaction);
        if (affected == 0)
        {
            return 0;
        }

        var version = await connection.ExecuteScalarAsync<int>(
            "SELECT invitation_version FROM meetings WHERE id = @MeetingId;",
            new { MeetingId = meetingId.ToString() },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return version;
    }

    public async Task<JoinMeetingResult> JoinAsync(
        Guid meetingId,
        Guid userId,
        int invitationVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var meeting = await connection.QuerySingleOrDefaultAsync<InvitationRow>(
            "SELECT invitation_version AS InvitationVersion, phase AS Phase FROM meetings WHERE id = @MeetingId;",
            new { MeetingId = meetingId.ToString() },
            transaction);
        if (meeting is null)
        {
            return JoinMeetingResult.NotFound;
        }

        if (meeting.Phase == MeetingPhase.Closed.ToString())
        {
            return JoinMeetingResult.Closed;
        }

        if (meeting.InvitationVersion != invitationVersion)
        {
            return JoinMeetingResult.InvitationMismatch;
        }

        var member = await connection.QuerySingleOrDefaultAsync<MemberRow>(
            "SELECT * FROM members WHERE meeting_id = @MeetingId AND user_id = @UserId;",
            new { MeetingId = meetingId.ToString(), UserId = userId.ToString() },
            transaction);
        if (member is not null)
        {
            return member.Status == MemberStatus.Excluded.ToString()
                ? JoinMeetingResult.Excluded
                : JoinMeetingResult.AlreadyMember;
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO members (meeting_id, user_id, status, joined_at_utc, excluded_at_utc)
            VALUES (@MeetingId, @UserId, 'Active', @Now, NULL);
            """,
            new { MeetingId = meetingId.ToString(), UserId = userId.ToString(), Now = DateTimeOffset.UtcNow.ToString("O") },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return JoinMeetingResult.Joined;
    }

    public async Task<bool> ExcludeMemberAsync(
        Guid meetingId,
        Guid facilitatorId,
        Guid targetUserId,
        DateTimeOffset excludedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            """
            UPDATE members
            SET status = 'Excluded', excluded_at_utc = @ExcludedAtUtc
            WHERE meeting_id = @MeetingId
              AND user_id = @TargetUserId
              AND user_id <> @FacilitatorId
              AND status = 'Active'
              AND EXISTS (
                  SELECT 1 FROM meetings
                  WHERE id = @MeetingId AND facilitator_id = @FacilitatorId AND phase <> 'Closed');
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                TargetUserId = targetUserId.ToString(),
                FacilitatorId = facilitatorId.ToString(),
                ExcludedAtUtc = excludedAtUtc.ToString("O"),
            },
            transaction);
        if (affected == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(
            "DELETE FROM readiness WHERE meeting_id = @MeetingId AND user_id = @UserId;",
            new { MeetingId = meetingId.ToString(), UserId = targetUserId.ToString() },
            transaction);
        await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET version = version + 1,
                facilitator_generation = facilitator_generation + 1,
                facilitator_election_deadline_utc = CASE
                    WHEN facilitator_id = @TargetUserId THEN NULL
                    ELSE facilitator_election_deadline_utc END,
                facilitator_id = CASE
                    WHEN facilitator_id = @TargetUserId THEN NULL
                    ELSE facilitator_id END,
                updated_at_utc = @Now
            WHERE id = @MeetingId;
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                TargetUserId = targetUserId.ToString(),
                Now = excludedAtUtc.ToString("O"),
            },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TransferFacilitatorAsync(
        Guid meetingId,
        Guid currentFacilitatorId,
        Guid targetUserId,
        DateTimeOffset? electionDeadlineUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET facilitator_id = @TargetUserId,
                facilitator_election_deadline_utc = @Deadline,
                facilitator_generation = facilitator_generation + 1,
                version = version + 1,
                updated_at_utc = @Now
            WHERE id = @MeetingId
              AND facilitator_id = @CurrentFacilitatorId
              AND facilitator_id <> @TargetUserId
              AND phase <> 'Closed'
              AND EXISTS (
                  SELECT 1 FROM members
                  WHERE meeting_id = @MeetingId AND user_id = @TargetUserId AND status = 'Active');
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                TargetUserId = targetUserId.ToString(),
                CurrentFacilitatorId = currentFacilitatorId.ToString(),
                Deadline = electionDeadlineUtc?.ToString("O"),
                Now = DateTimeOffset.UtcNow.ToString("O"),
            });
        return affected > 0;
    }

    public async Task<bool> ScheduleElectionAsync(
        Guid meetingId,
        Guid facilitatorId,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET facilitator_election_deadline_utc = @Deadline,
                facilitator_generation = facilitator_generation + 1
            WHERE id = @MeetingId AND facilitator_id = @FacilitatorId AND phase <> 'Closed';
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                FacilitatorId = facilitatorId.ToString(),
                Deadline = deadlineUtc.ToString("O"),
            });
        return affected > 0;
    }

    public async Task<bool> CancelElectionAsync(
        Guid meetingId,
        Guid facilitatorId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET facilitator_election_deadline_utc = NULL,
                facilitator_generation = facilitator_generation + 1
            WHERE id = @MeetingId AND facilitator_id = @FacilitatorId;
            """,
            new { MeetingId = meetingId.ToString(), FacilitatorId = facilitatorId.ToString() });
        return affected > 0;
    }

    public async Task<bool> AssignFacilitatorIfVacantAsync(
        Guid meetingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET facilitator_id = @UserId,
                facilitator_election_deadline_utc = NULL,
                facilitator_generation = facilitator_generation + 1,
                version = version + 1,
                updated_at_utc = @Now
            WHERE id = @MeetingId
              AND facilitator_id IS NULL
              AND phase <> 'Closed'
              AND EXISTS (
                  SELECT 1 FROM members
                  WHERE meeting_id = @MeetingId AND user_id = @UserId AND status = 'Active');
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                UserId = userId.ToString(),
                Now = DateTimeOffset.UtcNow.ToString("O"),
            });
        return affected > 0;
    }

    public async Task<IReadOnlyList<Guid>> ScheduleRecoveryAsync(
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var meetingIds = (await connection.QueryAsync<string>(
            """
            SELECT id FROM meetings
            WHERE facilitator_id IS NOT NULL
              AND facilitator_election_deadline_utc IS NULL
              AND phase <> 'Closed';
            """,
            transaction)).ToArray();
        await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET facilitator_election_deadline_utc = @Deadline,
                facilitator_generation = facilitator_generation + 1
            WHERE facilitator_id IS NOT NULL
              AND facilitator_election_deadline_utc IS NULL
              AND phase <> 'Closed';
            """,
            new { Deadline = deadlineUtc.ToString("O") },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return meetingIds.Select(Guid.Parse).ToArray();
    }

    public async Task<IReadOnlyList<FacilitatorElection>> GetDueElectionsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ElectionRow>(
            """
            SELECT id AS MeetingId, facilitator_id AS FacilitatorId,
                   facilitator_election_deadline_utc AS DeadlineUtc,
                   facilitator_generation AS Generation
            FROM meetings
            WHERE facilitator_election_deadline_utc IS NOT NULL
              AND facilitator_election_deadline_utc <= @Now
              AND phase <> 'Closed';
            """,
            new { Now = nowUtc.ToString("O") });
        return rows.Select(row => new FacilitatorElection(
            Guid.Parse(row.MeetingId),
            row.FacilitatorId is null ? null : Guid.Parse(row.FacilitatorId),
            SqliteMapping.Time(row.DeadlineUtc),
            row.Generation)).ToArray();
    }

    public async Task<bool> CompleteElectionAsync(
        Guid meetingId,
        Guid? expectedFacilitatorId,
        int expectedGeneration,
        Guid? newFacilitatorId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await connection.QuerySingleOrDefaultAsync<ElectionRow>(
            """
            SELECT id AS MeetingId, facilitator_id AS FacilitatorId,
                   facilitator_election_deadline_utc AS DeadlineUtc,
                   facilitator_generation AS Generation
            FROM meetings WHERE id = @MeetingId;
            """,
            new { MeetingId = meetingId.ToString() },
            transaction);
        if (current is null
            || current.Generation != expectedGeneration
            || current.FacilitatorId != expectedFacilitatorId?.ToString()
            || (current.DeadlineUtc is not null && SqliteMapping.Time(current.DeadlineUtc) > nowUtc))
        {
            return false;
        }

        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET facilitator_id = @NewFacilitatorId,
                facilitator_election_deadline_utc = NULL,
                facilitator_generation = facilitator_generation + 1,
                version = version + 1,
                updated_at_utc = @Now
            WHERE id = @MeetingId
              AND facilitator_generation = @ExpectedGeneration
              AND facilitator_id = @ExpectedFacilitatorId
              AND facilitator_election_deadline_utc <= @Now
              AND phase <> 'Closed'
              AND (@NewFacilitatorId IS NULL OR EXISTS (
                  SELECT 1 FROM members
                  WHERE meeting_id = @MeetingId AND user_id = @NewFacilitatorId AND status = 'Active'));
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                NewFacilitatorId = newFacilitatorId?.ToString(),
                ExpectedGeneration = expectedGeneration,
                ExpectedFacilitatorId = expectedFacilitatorId?.ToString(),
                Now = nowUtc.ToString("O"),
            },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return affected > 0;
    }

    private static async Task InsertMeetingAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Meeting meeting,
        string? facilitatorId)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO meetings (
                id, name, phase, anonymous_cards, private_collection, anonymous_votes,
                vote_mode, vote_percent, vote_minimum, vote_fixed, facilitator_id,
                invitation_version, version, board_version, created_at_utc, updated_at_utc)
            VALUES (
                @Id, @Name, @Phase, @AnonymousCards, @PrivateCollection, @AnonymousVotes,
                @VoteMode, @VotePercent, @VoteMinimum, @VoteFixed, @FacilitatorId,
                @InvitationVersion, @Version, @BoardVersion, @CreatedAtUtc, @UpdatedAtUtc);
            """,
            new
            {
                Id = meeting.Id.ToString(),
                meeting.Name,
                Phase = meeting.Phase.ToString(),
                AnonymousCards = meeting.AnonymousCards ? 1 : 0,
                PrivateCollection = meeting.PrivateCollection ? 1 : 0,
                AnonymousVotes = meeting.AnonymousVotes ? 1 : 0,
                VoteMode = meeting.VoteSettings.Mode.ToString(),
                VotePercent = meeting.VoteSettings.Percent,
                VoteMinimum = meeting.VoteSettings.Minimum,
                VoteFixed = meeting.VoteSettings.Fixed,
                FacilitatorId = facilitatorId,
                meeting.InvitationVersion,
                meeting.Version,
                meeting.BoardVersion,
                CreatedAtUtc = meeting.CreatedAtUtc.ToString("O"),
                UpdatedAtUtc = meeting.UpdatedAtUtc.ToString("O"),
            },
            transaction);
    }

    private static async Task InsertColumnsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyList<MeetingColumn> columns)
    {
        foreach (var column in columns)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO columns (meeting_id, id, name, color, position)
                VALUES (@MeetingId, @Id, @Name, @Color, @Position);
                """,
                new
                {
                    MeetingId = column.MeetingId.ToString(),
                    Id = column.Id.ToString(),
                    column.Name,
                    column.Color,
                    column.Position,
                },
                transaction);
        }
    }

    private static Meeting MapMeeting(MeetingRow row) =>
        new(
            Guid.Parse(row.Id),
            row.Name,
            Enum.Parse<MeetingPhase>(row.Phase),
            row.AnonymousCards != 0,
            row.PrivateCollection != 0,
            row.AnonymousVotes != 0,
            new VoteBudgetSettings(
                Enum.Parse<VoteMode>(row.VoteMode),
                row.VotePercent,
                row.VoteMinimum,
                row.VoteFixed),
            row.FacilitatorId is null ? null : Guid.Parse(row.FacilitatorId),
            row.InvitationVersion,
            row.Version,
            row.BoardVersion,
            SqliteMapping.Time(row.CreatedAtUtc),
            SqliteMapping.Time(row.UpdatedAtUtc));

    private sealed class ElectionRow
    {
        public required string MeetingId { get; init; }
        public string? FacilitatorId { get; init; }
        public required string DeadlineUtc { get; init; }
        public int Generation { get; init; }
    }

    private sealed class MeetingRow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Phase { get; init; }
        public int AnonymousCards { get; init; }
        public int PrivateCollection { get; init; }
        public int AnonymousVotes { get; init; }
        public required string VoteMode { get; init; }
        public int VotePercent { get; init; }
        public int VoteMinimum { get; init; }
        public int? VoteFixed { get; init; }
        public string? FacilitatorId { get; init; }
        public int InvitationVersion { get; init; }
        public int Version { get; init; }
        public int BoardVersion { get; init; }
        public required string CreatedAtUtc { get; init; }
        public required string UpdatedAtUtc { get; init; }
    }

    private sealed class MeetingColumnRow
    {
        public required string MeetingId { get; init; }
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Color { get; init; }
        public int Position { get; init; }
    }

    private sealed class ParticipantRow
    {
        public required string UserId { get; init; }
        public required string Subject { get; init; }
        public required string DisplayName { get; init; }
        public required string Status { get; init; }
    }

    private sealed class MemberRow
    {
        public required string MeetingId { get; init; }
        public required string UserId { get; init; }
        public required string Status { get; init; }
        public required string JoinedAtUtc { get; init; }
        public string? ExcludedAtUtc { get; init; }
    }

    private sealed class MeetingSummaryRow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Phase { get; init; }
        public required string CreatedAtUtc { get; init; }
    }

    private sealed class MeetingCommandRow
    {
        public string? FacilitatorId { get; init; }
        public required string Phase { get; init; }
        public int Version { get; init; }
    }

    private sealed class InvitationRow
    {
        public int InvitationVersion { get; init; }
        public required string Phase { get; init; }
    }
}
