using System.Data;
using System.Text.Json;
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
        return await ReadStateAsync(connection, meetingId);
    }

    internal static async Task<MeetingStateData?> ReadStateAsync(IDbConnection connection, Guid meetingId)
    {
        var meetingRow = await connection.QuerySingleOrDefaultAsync<MeetingRow>(
            "SELECT * FROM meetings WHERE id = @MeetingId;",
            new { MeetingId = meetingId.ToString() });
        if (meetingRow is null)
        {
            return null;
        }

        if (meetingRow.Phase == MeetingPhase.Closed.ToString())
        {
            return await ReadClosedStateAsync(connection, meetingRow);
        }

        var columns = (await connection.QueryAsync<MeetingColumnRow>(
            "SELECT * FROM columns WHERE meeting_id = @MeetingId ORDER BY position;",
            new { MeetingId = meetingId.ToString() }))
            .Select(row => new MeetingColumn(
                Guid.Parse(row.Id),
                Guid.Parse(row.MeetingId),
                row.Name,
                row.Color,
                row.Position,
                row.Description))
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
        var cards = (await connection.QueryAsync<CardRow>(
            """
            SELECT c.id AS Id, c.meeting_id AS MeetingId, c.column_id AS ColumnId,
                   c.text AS Text, c.version AS Version, c.group_id AS GroupId,
                   c.created_at_utc AS CreatedAtUtc, c.updated_at_utc AS UpdatedAtUtc,
                   a.user_id AS AuthorId, u.display_name AS AuthorDisplayName
            FROM cards c
            JOIN card_authors a ON a.meeting_id = c.meeting_id AND a.card_id = c.id
            JOIN users u ON u.id = a.user_id
            WHERE c.meeting_id = @MeetingId
            ORDER BY c.created_at_utc, c.id;
            """,
            new { MeetingId = meetingId.ToString() }))
            .Select(row => new CardData(
                new Card(
                    Guid.Parse(row.Id),
                    Guid.Parse(row.MeetingId),
                    Guid.Parse(row.ColumnId),
                    row.Text,
                    row.Version,
                    SqliteMapping.Time(row.CreatedAtUtc),
                    SqliteMapping.Time(row.UpdatedAtUtc),
                    row.GroupId is null ? null : Guid.Parse(row.GroupId)),
                Guid.Parse(row.AuthorId),
                row.AuthorDisplayName))
            .ToArray();
        var readyUserIds = (await connection.QueryAsync<string>(
            "SELECT user_id FROM readiness WHERE meeting_id = @MeetingId AND phase = @Phase;",
            new { MeetingId = meetingId.ToString(), Phase = meetingRow.Phase }))
            .Select(Guid.Parse)
            .ToHashSet();
        var groups = (await connection.QueryAsync<MeetingGroupRow>(
            "SELECT id AS Id, meeting_id AS MeetingId, column_id AS ColumnId, name AS Name FROM groups WHERE meeting_id = @MeetingId ORDER BY name, id;",
            new { MeetingId = meetingId.ToString() }))
            .Select(row => new MeetingGroup(
                Guid.Parse(row.Id),
                Guid.Parse(row.MeetingId),
                Guid.Parse(row.ColumnId),
                row.Name))
            .ToArray();
        var topics = (await connection.QueryAsync<TopicRow>(
            """
            SELECT t.id AS Id, t.meeting_id AS MeetingId, t.kind AS Kind,
                   COALESCE(t.card_id, t.group_id) AS SourceId
            FROM topics t
            LEFT JOIN cards c ON c.meeting_id = t.meeting_id AND c.id = t.card_id
            LEFT JOIN groups g ON g.meeting_id = t.meeting_id AND g.id = t.group_id
            LEFT JOIN columns col ON col.meeting_id = t.meeting_id
                AND col.id = COALESCE(c.column_id, g.column_id)
            WHERE t.meeting_id = @MeetingId
            ORDER BY col.position, t.id;
            """,
            new { MeetingId = meetingId.ToString() }))
            .Select(row => new MeetingTopic(
                Guid.Parse(row.Id),
                Guid.Parse(row.MeetingId),
                Enum.Parse<TopicKind>(row.Kind),
                Guid.Parse(row.SourceId)))
            .ToArray();
        var votes = (await connection.QueryAsync<VoteRow>(
            "SELECT meeting_id AS MeetingId, topic_id AS TopicId, user_id AS UserId, count AS Count FROM votes WHERE meeting_id = @MeetingId;",
            new { MeetingId = meetingId.ToString() }))
            .Select(row => new VoteAllocation(
                Guid.Parse(row.MeetingId),
                Guid.Parse(row.TopicId),
                Guid.Parse(row.UserId),
                row.Count))
            .ToArray();
        var ballots = (await connection.QueryAsync<BallotRow>(
            "SELECT meeting_id AS MeetingId, user_id AS UserId, version AS Version FROM personal_ballots WHERE meeting_id = @MeetingId;",
            new { MeetingId = meetingId.ToString() }))
            .Select(row => new PersonalBallot(
                Guid.Parse(row.MeetingId),
                Guid.Parse(row.UserId),
                row.Version))
            .ToArray();
        IReadOnlyList<DiscussionTopic> discussionQueue = [];
        Guid? currentTopicId = null;
        IReadOnlyList<TopicComment> comments = [];
        IReadOnlyList<TopicDecision> decisions = [];
        IReadOnlyList<ActionItem> actions = [];
        IReadOnlyList<JiraExport> exports = [];
        if (meetingRow.Phase == MeetingPhase.Discuss.ToString())
        {
            discussionQueue = (await connection.QueryAsync<DiscussionQueueRow>(
                """
                SELECT meeting_id AS MeetingId, topic_id AS TopicId, position AS Position, status AS Status
                FROM discussion_topics WHERE meeting_id = @MeetingId ORDER BY position;
                """,
                new { MeetingId = meetingId.ToString() }))
                .Select(row => new DiscussionTopic(
                    Guid.Parse(row.MeetingId),
                    Guid.Parse(row.TopicId),
                    row.Position,
                    Enum.Parse<DiscussionStatus>(row.Status)))
                .ToArray();
            var currentTopic = await connection.ExecuteScalarAsync<string?>(
                "SELECT current_topic_id FROM discussion_state WHERE meeting_id = @MeetingId;",
                new { MeetingId = meetingId.ToString() });
            currentTopicId = currentTopic is null ? null : Guid.Parse(currentTopic);
            comments = (await connection.QueryAsync<TextEntryRow>(
                "SELECT id AS Id, meeting_id AS MeetingId, topic_id AS TopicId, text AS Text FROM comments WHERE meeting_id = @MeetingId ORDER BY id;",
                new { MeetingId = meetingId.ToString() }))
                .Select(row => new TopicComment(
                    Guid.Parse(row.Id),
                    Guid.Parse(row.MeetingId),
                    Guid.Parse(row.TopicId),
                    row.Text))
                .ToArray();
            decisions = (await connection.QueryAsync<TextEntryRow>(
                "SELECT id AS Id, meeting_id AS MeetingId, topic_id AS TopicId, text AS Text FROM decisions WHERE meeting_id = @MeetingId ORDER BY id;",
                new { MeetingId = meetingId.ToString() }))
                .Select(row => new TopicDecision(
                    Guid.Parse(row.Id),
                    Guid.Parse(row.MeetingId),
                    Guid.Parse(row.TopicId),
                    row.Text))
                .ToArray();
            actions = (await connection.QueryAsync<ActionRow>(
                """
                SELECT id AS Id, meeting_id AS MeetingId, topic_id AS TopicId,
                       text AS Text, assignee_id AS AssigneeId
                FROM actions WHERE meeting_id = @MeetingId ORDER BY id;
                """,
                new { MeetingId = meetingId.ToString() }))
                .Select(row => new ActionItem(
                    Guid.Parse(row.Id),
                    Guid.Parse(row.MeetingId),
                    Guid.Parse(row.TopicId),
                    row.Text,
                    row.AssigneeId is null ? null : Guid.Parse(row.AssigneeId)))
                .ToArray();
            exports = (await connection.QueryAsync<ExportRow>(
                """
                SELECT meeting_id AS MeetingId, action_id AS ActionId,
                       external_request_id AS ExternalRequestId, status AS Status,
                       issue_key AS IssueKey, issue_url AS IssueUrl
                FROM exports WHERE meeting_id = @MeetingId ORDER BY action_id;
                """,
                new { MeetingId = meetingId.ToString() }))
                .Select(row => new JiraExport(
                    Guid.Parse(row.MeetingId),
                    Guid.Parse(row.ActionId),
                    Guid.Parse(row.ExternalRequestId),
                    Enum.Parse<JiraExportStatus>(row.Status),
                    row.IssueKey,
                    row.IssueUrl))
                .ToArray();
        }

        return new MeetingStateData(
            MapMeeting(meetingRow),
            columns,
            participants,
            cards,
            readyUserIds,
            groups,
            topics,
            votes,
            ballots,
            discussionQueue,
            currentTopicId,
            comments,
            decisions,
            actions,
            exports);
    }

    private static async Task<MeetingStateData> ReadClosedStateAsync(
        IDbConnection connection,
        MeetingRow meetingRow)
    {
        var payload = await connection.ExecuteScalarAsync<string?>(
            "SELECT payload_json FROM meeting_totals WHERE meeting_id = @MeetingId;",
            new { MeetingId = meetingRow.Id });
        if (payload is null)
        {
            throw new InvalidOperationException("Closed meeting totals are missing.");
        }

        var state = JsonSerializer.Deserialize<MeetingStateData>(payload)
            ?? throw new InvalidOperationException("Closed meeting totals are invalid.");
        var exports = (await connection.QueryAsync<ExportRow>(
            """
            SELECT meeting_id AS MeetingId, action_id AS ActionId,
                   external_request_id AS ExternalRequestId, status AS Status,
                   issue_key AS IssueKey, issue_url AS IssueUrl
            FROM exports WHERE meeting_id = @MeetingId ORDER BY action_id;
            """,
            new { MeetingId = meetingRow.Id }))
            .Select(row => new JiraExport(
                Guid.Parse(row.MeetingId),
                Guid.Parse(row.ActionId),
                Guid.Parse(row.ExternalRequestId),
                Enum.Parse<JiraExportStatus>(row.Status),
                row.IssueKey,
                row.IssueUrl))
            .ToArray();
        return state with
        {
            Meeting = MapMeeting(meetingRow),
            ReadyUserIds = new HashSet<Guid>(),
            Ballots = [],
            Exports = exports,
        };
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
        await using var transaction = connection.BeginTransaction(deferred: false);
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
        if (meeting.Phase == MeetingPhase.Vote.ToString())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO personal_ballots (meeting_id, user_id, version)
                VALUES (@MeetingId, @UserId, 0);
                """,
                new { MeetingId = meetingId.ToString(), UserId = userId.ToString() },
                transaction);
        }

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

    public async Task<MeetingCommandResult> CreateCardAsync(
        Card card,
        Guid authorId,
        Guid operationId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await GetCommandStateAsync(connection, transaction, card.MeetingId, authorId);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        var replay = await FindReplayAsync(
            connection,
            transaction,
            card.MeetingId,
            authorId,
            operationId,
            "CreateCard",
            fingerprint);
        if (replay is not null)
        {
            return replay;
        }

        if (state!.Phase != MeetingPhase.Collect.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidPhase);
        }

        var columnExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM columns WHERE meeting_id = @MeetingId AND id = @ColumnId;",
            new { MeetingId = card.MeetingId.ToString(), ColumnId = card.ColumnId.ToString() },
            transaction);
        if (columnExists == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO cards (meeting_id, id, column_id, text, version, created_at_utc, updated_at_utc)
            VALUES (@MeetingId, @Id, @ColumnId, @Text, @Version, @CreatedAtUtc, @UpdatedAtUtc);
            """,
            new
            {
                MeetingId = card.MeetingId.ToString(),
                Id = card.Id.ToString(),
                ColumnId = card.ColumnId.ToString(),
                card.Text,
                card.Version,
                CreatedAtUtc = card.CreatedAtUtc.ToString("O"),
                UpdatedAtUtc = card.UpdatedAtUtc.ToString("O"),
            },
            transaction);
        await connection.ExecuteAsync(
            """
            INSERT INTO card_authors (meeting_id, card_id, user_id)
            VALUES (@MeetingId, @CardId, @UserId);
            """,
            new
            {
                MeetingId = card.MeetingId.ToString(),
                CardId = card.Id.ToString(),
                UserId = authorId.ToString(),
            },
            transaction);
        var newVersion = state.Version + 1;
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET version = @NewVersion, board_version = board_version + 1, updated_at_utc = @Now
            WHERE id = @MeetingId AND version = @ExpectedVersion AND phase = 'Collect';
            """,
            new
            {
                MeetingId = card.MeetingId.ToString(),
                NewVersion = newVersion,
                ExpectedVersion = state.Version,
                Now = card.UpdatedAtUtc.ToString("O"),
            },
            transaction);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await SaveReplayAsync(
            connection,
            transaction,
            card.MeetingId,
            authorId,
            operationId,
            "CreateCard",
            fingerprint,
            card.Id,
            newVersion,
            card.CreatedAtUtc);
        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(MeetingCommandStatus.Success, newVersion, card.Id);
    }

    public async Task<MeetingCommandResult> UpdateCardAsync(
        Card card,
        Guid authorId,
        int expectedCardVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await GetCommandStateAsync(connection, transaction, card.MeetingId, authorId);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        if (state!.Phase != MeetingPhase.Collect.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidPhase);
        }

        var current = await GetCardCommandStateAsync(connection, transaction, card.MeetingId, card.Id);
        var cardError = CheckCardAccess(current, authorId, expectedCardVersion);
        if (cardError is not null)
        {
            return cardError;
        }

        var newVersion = state.Version + 1;
        var affected = await connection.ExecuteAsync(
            """
            UPDATE cards
            SET text = @Text, version = @Version, updated_at_utc = @UpdatedAtUtc
            WHERE meeting_id = @MeetingId AND id = @Id AND version = @ExpectedVersion
              AND EXISTS (
                  SELECT 1 FROM card_authors
                  WHERE meeting_id = @MeetingId AND card_id = @Id AND user_id = @AuthorId);
            """,
            new
            {
                MeetingId = card.MeetingId.ToString(),
                Id = card.Id.ToString(),
                card.Text,
                card.Version,
                UpdatedAtUtc = card.UpdatedAtUtc.ToString("O"),
                ExpectedVersion = expectedCardVersion,
                AuthorId = authorId.ToString(),
            },
            transaction);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        affected = await connection.ExecuteAsync(
            """
            UPDATE meetings SET version = @NewVersion, updated_at_utc = @Now
            WHERE id = @MeetingId AND version = @ExpectedVersion AND phase = 'Collect';
            """,
            new
            {
                MeetingId = card.MeetingId.ToString(),
                NewVersion = newVersion,
                ExpectedVersion = state.Version,
                Now = card.UpdatedAtUtc.ToString("O"),
            },
            transaction);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(MeetingCommandStatus.Success, newVersion, card.Id);
    }

    public async Task<MeetingCommandResult> DeleteCardAsync(
        Guid meetingId,
        Guid cardId,
        Guid authorId,
        int expectedCardVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await GetCommandStateAsync(connection, transaction, meetingId, authorId);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        if (state!.Phase != MeetingPhase.Collect.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidPhase);
        }

        var current = await GetCardCommandStateAsync(connection, transaction, meetingId, cardId);
        var cardError = CheckCardAccess(current, authorId, expectedCardVersion);
        if (cardError is not null)
        {
            return cardError;
        }

        var affected = await connection.ExecuteAsync(
            """
            DELETE FROM cards
            WHERE meeting_id = @MeetingId AND id = @CardId AND version = @ExpectedVersion
              AND EXISTS (
                  SELECT 1 FROM card_authors
                  WHERE meeting_id = @MeetingId AND card_id = @CardId AND user_id = @AuthorId);
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                CardId = cardId.ToString(),
                ExpectedVersion = expectedCardVersion,
                AuthorId = authorId.ToString(),
            },
            transaction);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        var newVersion = state.Version + 1;
        affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET version = @NewVersion, board_version = board_version + 1, updated_at_utc = @Now
            WHERE id = @MeetingId AND version = @ExpectedVersion AND phase = 'Collect';
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                NewVersion = newVersion,
                ExpectedVersion = state.Version,
                Now = DateTimeOffset.UtcNow.ToString("O"),
            },
            transaction);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(MeetingCommandStatus.Success, newVersion, cardId);
    }

    public async Task<MeetingCommandResult> SetReadinessAsync(
        Guid meetingId,
        Guid userId,
        MeetingPhase phase,
        bool ready,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await GetCommandStateAsync(connection, transaction, meetingId, userId);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        if (state!.Phase != phase.ToString()
            || phase is not (MeetingPhase.Collect or MeetingPhase.Vote))
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidPhase);
        }

        var affected = ready
            ? await connection.ExecuteAsync(
                """
                INSERT INTO readiness (meeting_id, user_id, phase)
                VALUES (@MeetingId, @UserId, @Phase)
                ON CONFLICT (meeting_id, user_id, phase) DO NOTHING;
                """,
                new { MeetingId = meetingId.ToString(), UserId = userId.ToString(), Phase = phase.ToString() },
                transaction)
            : await connection.ExecuteAsync(
                "DELETE FROM readiness WHERE meeting_id = @MeetingId AND user_id = @UserId AND phase = @Phase;",
                new { MeetingId = meetingId.ToString(), UserId = userId.ToString(), Phase = phase.ToString() },
                transaction);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.Success, state.Version, IsReplay: true);
        }

        var newVersion = state.Version + 1;
        affected = await connection.ExecuteAsync(
            """
            UPDATE meetings SET version = @NewVersion, updated_at_utc = @Now
            WHERE id = @MeetingId AND version = @ExpectedVersion AND phase = @Phase;
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                NewVersion = newVersion,
                ExpectedVersion = state.Version,
                Phase = phase.ToString(),
                Now = nowUtc.ToString("O"),
            },
            transaction);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(MeetingCommandStatus.Success, newVersion);
    }

    public async Task<MeetingCommandResult> AdvanceAsync(
        Guid meetingId,
        Guid facilitatorId,
        MeetingPhase fromPhase,
        MeetingPhase toPhase,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await GetCommandStateAsync(connection, transaction, meetingId, facilitatorId);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        if (state!.FacilitatorId != facilitatorId.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.Forbidden);
        }

        var replay = await FindReplayAsync(
            connection,
            transaction,
            meetingId,
            facilitatorId,
            operationId,
            "Advance",
            fingerprint);
        if (replay is not null)
        {
            return replay;
        }

        if (state.Phase != fromPhase.ToString() || fromPhase == MeetingPhase.Closed)
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidPhase);
        }

        if (state.Version != expectedVersion)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        if (fromPhase == MeetingPhase.Group && toPhase == MeetingPhase.Vote)
        {
            await InitializeVotingAsync(connection, transaction, meetingId);
        }

        if (fromPhase == MeetingPhase.Vote && toPhase == MeetingPhase.Discuss)
        {
            await InitializeDiscussionAsync(connection, transaction, meetingId);
        }

        var newVersion = expectedVersion + 1;
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET phase = @ToPhase, version = @NewVersion, updated_at_utc = @Now
            WHERE id = @MeetingId AND facilitator_id = @FacilitatorId
              AND phase = @FromPhase AND version = @ExpectedVersion;
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                FacilitatorId = facilitatorId.ToString(),
                FromPhase = fromPhase.ToString(),
                ToPhase = toPhase.ToString(),
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
            "DELETE FROM readiness WHERE meeting_id = @MeetingId;",
            new { MeetingId = meetingId.ToString() },
            transaction);
        await SaveReplayAsync(
            connection,
            transaction,
            meetingId,
            facilitatorId,
            operationId,
            "Advance",
            fingerprint,
            null,
            newVersion,
            nowUtc);
        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(MeetingCommandStatus.Success, newVersion);
    }

    private static async Task InitializeDiscussionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId)
    {
        var rankings = (await connection.QueryAsync<DiscussionRankingRow>(
            """
            SELECT t.id AS TopicId, COALESCE(SUM(v.count), 0) AS Votes,
                   col.position AS ColumnPosition
            FROM topics t
            LEFT JOIN votes v ON v.meeting_id = t.meeting_id AND v.topic_id = t.id
            LEFT JOIN cards c ON c.meeting_id = t.meeting_id AND c.id = t.card_id
            LEFT JOIN groups g ON g.meeting_id = t.meeting_id AND g.id = t.group_id
            LEFT JOIN columns col ON col.meeting_id = t.meeting_id
                AND col.id = COALESCE(c.column_id, g.column_id)
            WHERE t.meeting_id = @MeetingId
            GROUP BY t.id, col.position;
            """,
            new { MeetingId = meetingId.ToString() },
            transaction))
            .Select(row => new DiscussionRanking(Guid.Parse(row.TopicId), row.Votes, row.ColumnPosition))
            .ToArray();
        var queue = DiscussionQueue.Create(meetingId, rankings);
        foreach (var topic in queue)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO discussion_topics (meeting_id, topic_id, position, status)
                VALUES (@MeetingId, @TopicId, @Position, 'Pending');
                """,
                new
                {
                    MeetingId = meetingId.ToString(),
                    TopicId = topic.TopicId.ToString(),
                    topic.Position,
                },
                transaction);
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO discussion_state (meeting_id, current_topic_id)
            VALUES (@MeetingId, @CurrentTopicId);
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                CurrentTopicId = queue.FirstOrDefault()?.TopicId.ToString(),
            },
            transaction);
    }

    private static async Task InitializeVotingAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId)
    {
        var parameters = new { MeetingId = meetingId.ToString() };
        await connection.ExecuteAsync(
            """
            INSERT INTO topics (meeting_id, id, kind, card_id, group_id)
            SELECT meeting_id, id, 'Card', id, NULL FROM cards
            WHERE meeting_id = @MeetingId AND group_id IS NULL;
            """,
            parameters,
            transaction);
        await connection.ExecuteAsync(
            """
            INSERT INTO topics (meeting_id, id, kind, card_id, group_id)
            SELECT meeting_id, id, 'Group', NULL, id FROM groups WHERE meeting_id = @MeetingId;
            """,
            parameters,
            transaction);
        await connection.ExecuteAsync(
            """
            INSERT INTO personal_ballots (meeting_id, user_id, version)
            SELECT meeting_id, user_id, 0 FROM members
            WHERE meeting_id = @MeetingId AND status = 'Active';
            """,
            parameters,
            transaction);
        await connection.ExecuteAsync(
            """
            UPDATE meetings SET initial_card_count = (
                SELECT COUNT(*) FROM cards WHERE cards.meeting_id = meetings.id)
            WHERE id = @MeetingId;
            """,
            parameters,
            transaction);
    }

    private static async Task<CommandStateRow?> GetCommandStateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId) =>
        await connection.QuerySingleOrDefaultAsync<CommandStateRow>(
            """
            SELECT m.phase AS Phase, m.version AS Version, m.facilitator_id AS FacilitatorId,
                   member.status AS MemberStatus
            FROM meetings m
            LEFT JOIN members member ON member.meeting_id = m.id AND member.user_id = @UserId
            WHERE m.id = @MeetingId;
            """,
            new { MeetingId = meetingId.ToString(), UserId = userId.ToString() },
            transaction);

    private static MeetingCommandResult? CheckMemberAccess(CommandStateRow? state)
    {
        if (state is null || state.MemberStatus is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        return state.MemberStatus == MemberStatus.Excluded.ToString()
            ? new MeetingCommandResult(MeetingCommandStatus.Forbidden)
            : null;
    }

    private static async Task<CardCommandRow?> GetCardCommandStateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid cardId) =>
        await connection.QuerySingleOrDefaultAsync<CardCommandRow>(
            """
            SELECT c.version AS Version, a.user_id AS AuthorId
            FROM cards c
            LEFT JOIN card_authors a ON a.meeting_id = c.meeting_id AND a.card_id = c.id
            WHERE c.meeting_id = @MeetingId AND c.id = @CardId;
            """,
            new { MeetingId = meetingId.ToString(), CardId = cardId.ToString() },
            transaction);

    private static MeetingCommandResult? CheckCardAccess(
        CardCommandRow? card,
        Guid authorId,
        int expectedVersion)
    {
        if (card is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (card.AuthorId != authorId.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.Forbidden);
        }

        return card.Version != expectedVersion
            ? new MeetingCommandResult(MeetingCommandStatus.VersionConflict)
            : null;
    }

    private static async Task<MeetingCommandResult?> FindReplayAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId,
        Guid operationId,
        string operationType,
        string fingerprint)
    {
        var row = await connection.QuerySingleOrDefaultAsync<CommandReplayRow>(
            """
            SELECT operation_type AS OperationType, fingerprint AS Fingerprint,
                   result_id AS ResultId, result_version AS ResultVersion
            FROM command_deduplication
            WHERE meeting_id = @MeetingId AND user_id = @UserId AND operation_id = @OperationId;
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                UserId = userId.ToString(),
                OperationId = operationId.ToString(),
            },
            transaction);
        if (row is null)
        {
            return null;
        }

        if (row.OperationType != operationType || row.Fingerprint != fingerprint)
        {
            return new MeetingCommandResult(MeetingCommandStatus.OperationConflict);
        }

        return new MeetingCommandResult(
            MeetingCommandStatus.Success,
            row.ResultVersion,
            row.ResultId is null ? null : Guid.Parse(row.ResultId),
            true);
    }

    private static async Task SaveReplayAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId,
        Guid operationId,
        string operationType,
        string fingerprint,
        Guid? resultId,
        int resultVersion,
        DateTimeOffset nowUtc) =>
        await connection.ExecuteAsync(
            """
            INSERT INTO command_deduplication (
                meeting_id, user_id, operation_id, operation_type, fingerprint,
                result_id, result_version, created_at_utc)
            VALUES (
                @MeetingId, @UserId, @OperationId, @OperationType, @Fingerprint,
                @ResultId, @ResultVersion, @CreatedAtUtc);
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                UserId = userId.ToString(),
                OperationId = operationId.ToString(),
                OperationType = operationType,
                Fingerprint = fingerprint,
                ResultId = resultId?.ToString(),
                ResultVersion = resultVersion,
                CreatedAtUtc = nowUtc.ToString("O"),
            },
            transaction);

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
                INSERT INTO columns (meeting_id, id, name, color, position, description)
                VALUES (@MeetingId, @Id, @Name, @Color, @Position, @Description);
                """,
                new
                {
                    MeetingId = column.MeetingId.ToString(),
                    Id = column.Id.ToString(),
                    column.Name,
                    column.Color,
                    column.Position,
                    column.Description,
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
            row.InitialCardCount,
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
        public int? InitialCardCount { get; init; }
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
        public string? Description { get; init; }
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

    private sealed class CardRow
    {
        public required string Id { get; init; }
        public required string MeetingId { get; init; }
        public required string ColumnId { get; init; }
        public required string Text { get; init; }
        public int Version { get; init; }
        public string? GroupId { get; init; }
        public required string CreatedAtUtc { get; init; }
        public required string UpdatedAtUtc { get; init; }
        public required string AuthorId { get; init; }
        public required string AuthorDisplayName { get; init; }
    }

    private sealed class MeetingGroupRow
    {
        public required string Id { get; init; }
        public required string MeetingId { get; init; }
        public required string ColumnId { get; init; }
        public required string Name { get; init; }
    }

    private sealed class TopicRow
    {
        public required string Id { get; init; }
        public required string MeetingId { get; init; }
        public required string Kind { get; init; }
        public required string SourceId { get; init; }
    }

    private sealed class VoteRow
    {
        public required string MeetingId { get; init; }
        public required string TopicId { get; init; }
        public required string UserId { get; init; }
        public int Count { get; init; }
    }

    private sealed class BallotRow
    {
        public required string MeetingId { get; init; }
        public required string UserId { get; init; }
        public int Version { get; init; }
    }

    private sealed class CommandStateRow
    {
        public required string Phase { get; init; }
        public int Version { get; init; }
        public string? FacilitatorId { get; init; }
        public string? MemberStatus { get; init; }
    }

    private sealed class CardCommandRow
    {
        public int Version { get; init; }
        public string? AuthorId { get; init; }
    }

    private sealed class DiscussionRankingRow
    {
        public required string TopicId { get; init; }
        public int Votes { get; init; }
        public int ColumnPosition { get; init; }
    }

    private sealed class DiscussionQueueRow
    {
        public required string MeetingId { get; init; }
        public required string TopicId { get; init; }
        public int Position { get; init; }
        public required string Status { get; init; }
    }

    private sealed class TextEntryRow
    {
        public required string Id { get; init; }
        public required string MeetingId { get; init; }
        public required string TopicId { get; init; }
        public required string Text { get; init; }
    }

    private sealed class ActionRow
    {
        public required string Id { get; init; }
        public required string MeetingId { get; init; }
        public required string TopicId { get; init; }
        public required string Text { get; init; }
        public string? AssigneeId { get; init; }
    }

    private sealed class ExportRow
    {
        public required string MeetingId { get; init; }
        public required string ActionId { get; init; }
        public required string ExternalRequestId { get; init; }
        public required string Status { get; init; }
        public string? IssueKey { get; init; }
        public string? IssueUrl { get; init; }
    }

    private sealed class CommandReplayRow
    {
        public required string OperationType { get; init; }
        public required string Fingerprint { get; init; }
        public string? ResultId { get; init; }
        public int ResultVersion { get; init; }
    }
}
