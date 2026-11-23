using System.Data;
using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteDiscussionRepository(SqliteConnectionFactory connections) : IDiscussionRepository
{
    public async Task<MeetingCommandResult> SelectCurrentAsync(
        Guid meetingId,
        Guid facilitatorId,
        Guid topicId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await GetStateAsync(connection, transaction, meetingId, facilitatorId);
        var accessError = CheckFacilitatorAccess(state, facilitatorId);
        if (accessError is not null)
        {
            return accessError;
        }

        var phaseError = CheckPhaseAndVersion(state, expectedVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        var current = await connection.ExecuteScalarAsync<string?>(
            "SELECT current_topic_id FROM discussion_state WHERE meeting_id = @MeetingId;",
            new { MeetingId = meetingId.ToString() },
            transaction);
        var topicExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM discussion_topics WHERE meeting_id = @MeetingId AND topic_id = @TopicId;",
            new { MeetingId = meetingId.ToString(), TopicId = topicId.ToString() },
            transaction);
        if (topicExists == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (current == topicId.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.Success, expectedVersion, topicId, true);
        }

        await connection.ExecuteAsync(
            "UPDATE discussion_state SET current_topic_id = @TopicId WHERE meeting_id = @MeetingId;",
            new { MeetingId = meetingId.ToString(), TopicId = topicId.ToString() },
            transaction);
        return await CompleteMutationAsync(
            connection,
            transaction,
            meetingId,
            expectedVersion,
            nowUtc,
            topicId);
    }

    public async Task<MeetingCommandResult> SetTopicStatusAsync(
        Guid meetingId,
        Guid facilitatorId,
        Guid topicId,
        DiscussionStatus status,
        bool advance,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await GetStateAsync(connection, transaction, meetingId, facilitatorId);
        var accessError = CheckFacilitatorAccess(state, facilitatorId);
        if (accessError is not null)
        {
            return accessError;
        }

        var phaseError = CheckPhaseAndVersion(state, expectedVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        var topic = await connection.QuerySingleOrDefaultAsync<DiscussionTopicRow>(
            """
            SELECT d.position AS Position, d.status AS Status, s.current_topic_id AS CurrentTopicId
            FROM discussion_topics d
            LEFT JOIN discussion_state s ON s.meeting_id = d.meeting_id
            WHERE d.meeting_id = @MeetingId AND d.topic_id = @TopicId;
            """,
            new { MeetingId = meetingId.ToString(), TopicId = topicId.ToString() },
            transaction);
        if (topic is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (advance && topic.CurrentTopicId != topicId.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidNavigation);
        }

        if (!advance && topic.Status == status.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.Success, expectedVersion, topicId, true);
        }

        await connection.ExecuteAsync(
            """
            UPDATE discussion_topics SET status = @Status
            WHERE meeting_id = @MeetingId AND topic_id = @TopicId;
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                TopicId = topicId.ToString(),
                Status = status.ToString(),
            },
            transaction);
        if (advance)
        {
            await connection.ExecuteAsync(
                """
                UPDATE discussion_state SET current_topic_id = (
                    SELECT topic_id FROM discussion_topics
                    WHERE meeting_id = @MeetingId AND position = @NextPosition)
                WHERE meeting_id = @MeetingId;
                """,
                new { MeetingId = meetingId.ToString(), NextPosition = topic.Position + 1 },
                transaction);
        }

        return await CompleteMutationAsync(
            connection,
            transaction,
            meetingId,
            expectedVersion,
            nowUtc,
            topicId);
    }

    public Task<MeetingCommandResult> CreateCommentAsync(
        TopicComment comment,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        CreateEntryAsync(
            EntryKind.Comment,
            comment.Id,
            comment.MeetingId,
            comment.TopicId,
            comment.Text,
            null,
            facilitatorId,
            expectedVersion,
            operationId,
            fingerprint,
            nowUtc,
            cancellationToken);

    public Task<MeetingCommandResult> UpdateCommentAsync(
        Guid meetingId,
        Guid commentId,
        Guid facilitatorId,
        string text,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        UpdateEntryAsync(
            EntryKind.Comment,
            meetingId,
            commentId,
            facilitatorId,
            text,
            null,
            expectedVersion,
            nowUtc,
            cancellationToken);

    public Task<MeetingCommandResult> DeleteCommentAsync(
        Guid meetingId,
        Guid commentId,
        Guid facilitatorId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        DeleteEntryAsync(
            EntryKind.Comment,
            meetingId,
            commentId,
            facilitatorId,
            expectedVersion,
            nowUtc,
            cancellationToken);

    public Task<MeetingCommandResult> CreateDecisionAsync(
        TopicDecision decision,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        CreateEntryAsync(
            EntryKind.Decision,
            decision.Id,
            decision.MeetingId,
            decision.TopicId,
            decision.Text,
            null,
            facilitatorId,
            expectedVersion,
            operationId,
            fingerprint,
            nowUtc,
            cancellationToken);

    public Task<MeetingCommandResult> UpdateDecisionAsync(
        Guid meetingId,
        Guid decisionId,
        Guid facilitatorId,
        string text,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        UpdateEntryAsync(
            EntryKind.Decision,
            meetingId,
            decisionId,
            facilitatorId,
            text,
            null,
            expectedVersion,
            nowUtc,
            cancellationToken);

    public Task<MeetingCommandResult> DeleteDecisionAsync(
        Guid meetingId,
        Guid decisionId,
        Guid facilitatorId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        DeleteEntryAsync(
            EntryKind.Decision,
            meetingId,
            decisionId,
            facilitatorId,
            expectedVersion,
            nowUtc,
            cancellationToken);

    public Task<MeetingCommandResult> CreateActionAsync(
        ActionItem action,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        CreateEntryAsync(
            EntryKind.Action,
            action.Id,
            action.MeetingId,
            action.TopicId,
            action.Text,
            action.AssigneeId,
            facilitatorId,
            expectedVersion,
            operationId,
            fingerprint,
            nowUtc,
            cancellationToken);

    public Task<MeetingCommandResult> UpdateActionAsync(
        Guid meetingId,
        Guid actionId,
        Guid facilitatorId,
        string text,
        Guid? assigneeId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        UpdateEntryAsync(
            EntryKind.Action,
            meetingId,
            actionId,
            facilitatorId,
            text,
            assigneeId,
            expectedVersion,
            nowUtc,
            cancellationToken);

    public Task<MeetingCommandResult> DeleteActionAsync(
        Guid meetingId,
        Guid actionId,
        Guid facilitatorId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        DeleteEntryAsync(
            EntryKind.Action,
            meetingId,
            actionId,
            facilitatorId,
            expectedVersion,
            nowUtc,
            cancellationToken);

    private async Task<MeetingCommandResult> CreateEntryAsync(
        EntryKind kind,
        Guid entryId,
        Guid meetingId,
        Guid topicId,
        string text,
        Guid? assigneeId,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await GetStateAsync(connection, transaction, meetingId, facilitatorId);
        var accessError = CheckFacilitatorAccess(state, facilitatorId);
        if (accessError is not null)
        {
            return accessError;
        }

        var operationType = OperationType(kind);
        var replay = await FindReplayAsync(
            connection,
            transaction,
            meetingId,
            facilitatorId,
            operationId,
            operationType,
            fingerprint);
        if (replay is not null)
        {
            return replay;
        }

        var phaseError = CheckPhaseAndVersion(state, expectedVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        var topicExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM discussion_topics WHERE meeting_id = @MeetingId AND topic_id = @TopicId;",
            new { MeetingId = meetingId.ToString(), TopicId = topicId.ToString() },
            transaction);
        if (topicExists == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (kind == EntryKind.Action
            && assigneeId is not null
            && !await IsActiveAssigneeAsync(connection, transaction, meetingId, assigneeId.Value))
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidAssignee);
        }

        var sql = kind switch
        {
            EntryKind.Comment =>
                "INSERT INTO comments (meeting_id, id, topic_id, text) VALUES (@MeetingId, @Id, @TopicId, @Text);",
            EntryKind.Decision =>
                "INSERT INTO decisions (meeting_id, id, topic_id, text) VALUES (@MeetingId, @Id, @TopicId, @Text);",
            EntryKind.Action =>
                "INSERT INTO actions (meeting_id, id, topic_id, text, assignee_id) VALUES (@MeetingId, @Id, @TopicId, @Text, @AssigneeId);",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        await connection.ExecuteAsync(
            sql,
            new
            {
                MeetingId = meetingId.ToString(),
                Id = entryId.ToString(),
                TopicId = topicId.ToString(),
                Text = text,
                AssigneeId = assigneeId?.ToString(),
            },
            transaction);
        var newVersion = expectedVersion + 1;
        var affected = await UpdateMeetingVersionAsync(
            connection,
            transaction,
            meetingId,
            expectedVersion,
            newVersion,
            nowUtc);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await SaveReplayAsync(
            connection,
            transaction,
            meetingId,
            facilitatorId,
            operationId,
            operationType,
            fingerprint,
            entryId,
            newVersion,
            nowUtc);
        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(MeetingCommandStatus.Success, newVersion, entryId);
    }

    private async Task<MeetingCommandResult> UpdateEntryAsync(
        EntryKind kind,
        Guid meetingId,
        Guid entryId,
        Guid facilitatorId,
        string text,
        Guid? assigneeId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await GetStateAsync(connection, transaction, meetingId, facilitatorId);
        var accessError = CheckFacilitatorAccess(state, facilitatorId);
        if (accessError is not null)
        {
            return accessError;
        }

        var phaseError = CheckPhaseAndVersion(state, expectedVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        var current = await FindEntryAsync(connection, transaction, kind, meetingId, entryId);
        if (current is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (kind == EntryKind.Action
            && await IsActionLockedAsync(connection, transaction, meetingId, entryId))
        {
            return new MeetingCommandResult(MeetingCommandStatus.ActionLocked);
        }

        var currentAssignee = current.AssigneeId is null ? (Guid?)null : Guid.Parse(current.AssigneeId);
        if (kind == EntryKind.Action
            && assigneeId != currentAssignee
            && assigneeId is not null
            && !await IsActiveAssigneeAsync(connection, transaction, meetingId, assigneeId.Value))
        {
            return new MeetingCommandResult(MeetingCommandStatus.InvalidAssignee);
        }

        if (current.Text == text && (kind != EntryKind.Action || currentAssignee == assigneeId))
        {
            return new MeetingCommandResult(MeetingCommandStatus.Success, expectedVersion, entryId, true);
        }

        var sql = kind switch
        {
            EntryKind.Comment => "UPDATE comments SET text = @Text WHERE meeting_id = @MeetingId AND id = @Id;",
            EntryKind.Decision => "UPDATE decisions SET text = @Text WHERE meeting_id = @MeetingId AND id = @Id;",
            EntryKind.Action => "UPDATE actions SET text = @Text, assignee_id = @AssigneeId WHERE meeting_id = @MeetingId AND id = @Id;",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        await connection.ExecuteAsync(
            sql,
            new
            {
                MeetingId = meetingId.ToString(),
                Id = entryId.ToString(),
                Text = text,
                AssigneeId = assigneeId?.ToString(),
            },
            transaction);
        return await CompleteMutationAsync(
            connection,
            transaction,
            meetingId,
            expectedVersion,
            nowUtc,
            entryId);
    }

    private async Task<MeetingCommandResult> DeleteEntryAsync(
        EntryKind kind,
        Guid meetingId,
        Guid entryId,
        Guid facilitatorId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await GetStateAsync(connection, transaction, meetingId, facilitatorId);
        var accessError = CheckFacilitatorAccess(state, facilitatorId);
        if (accessError is not null)
        {
            return accessError;
        }

        var phaseError = CheckPhaseAndVersion(state, expectedVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        var current = await FindEntryAsync(connection, transaction, kind, meetingId, entryId);
        if (current is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (kind == EntryKind.Action
            && await IsActionLockedAsync(connection, transaction, meetingId, entryId))
        {
            return new MeetingCommandResult(MeetingCommandStatus.ActionLocked);
        }

        var table = TableName(kind);
        await connection.ExecuteAsync(
            $"DELETE FROM {table} WHERE meeting_id = @MeetingId AND id = @Id;",
            new { MeetingId = meetingId.ToString(), Id = entryId.ToString() },
            transaction);
        return await CompleteMutationAsync(
            connection,
            transaction,
            meetingId,
            expectedVersion,
            nowUtc,
            entryId);
    }

    private static async Task<EntryRow?> FindEntryAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        EntryKind kind,
        Guid meetingId,
        Guid entryId)
    {
        var table = TableName(kind);
        var assignee = kind == EntryKind.Action ? "assignee_id" : "NULL";
        return await connection.QuerySingleOrDefaultAsync<EntryRow>(
            $"SELECT text AS Text, {assignee} AS AssigneeId FROM {table} WHERE meeting_id = @MeetingId AND id = @Id;",
            new { MeetingId = meetingId.ToString(), Id = entryId.ToString() },
            transaction);
    }

    private static async Task<bool> IsActionLockedAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid actionId) =>
        await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM exports
            WHERE meeting_id = @MeetingId AND action_id = @ActionId
              AND status IN ('Pending', 'Created', 'Unknown');
            """,
            new { MeetingId = meetingId.ToString(), ActionId = actionId.ToString() },
            transaction) == 1;

    private static async Task<bool> IsActiveAssigneeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid assigneeId) =>
        await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM members WHERE meeting_id = @MeetingId AND user_id = @UserId AND status = 'Active';",
            new { MeetingId = meetingId.ToString(), UserId = assigneeId.ToString() },
            transaction) == 1;

    private static async Task<MeetingCommandResult> CompleteMutationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        Guid entityId)
    {
        var newVersion = expectedVersion + 1;
        var affected = await UpdateMeetingVersionAsync(
            connection,
            transaction,
            meetingId,
            expectedVersion,
            newVersion,
            nowUtc);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        transaction.Commit();
        return new MeetingCommandResult(MeetingCommandStatus.Success, newVersion, entityId);
    }

    private static async Task<int> UpdateMeetingVersionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        int expectedVersion,
        int newVersion,
        DateTimeOffset nowUtc) =>
        await connection.ExecuteAsync(
            """
            UPDATE meetings SET version = @NewVersion, updated_at_utc = @Now
            WHERE id = @MeetingId AND phase = 'Discuss' AND version = @ExpectedVersion;
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                ExpectedVersion = expectedVersion,
                NewVersion = newVersion,
                Now = nowUtc.ToString("O"),
            },
            transaction);

    private static async Task<DiscussionStateRow?> GetStateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId) =>
        await connection.QuerySingleOrDefaultAsync<DiscussionStateRow>(
            """
            SELECT m.phase AS Phase, m.version AS Version, m.facilitator_id AS FacilitatorId,
                   member.status AS MemberStatus
            FROM meetings m
            LEFT JOIN members member ON member.meeting_id = m.id AND member.user_id = @UserId
            WHERE m.id = @MeetingId;
            """,
            new { MeetingId = meetingId.ToString(), UserId = userId.ToString() },
            transaction);

    private static MeetingCommandResult? CheckFacilitatorAccess(DiscussionStateRow? state, Guid facilitatorId)
    {
        if (state is null || state.MemberStatus is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (state.MemberStatus == MemberStatus.Excluded.ToString()
            || state.FacilitatorId != facilitatorId.ToString())
        {
            return new MeetingCommandResult(MeetingCommandStatus.Forbidden);
        }

        return null;
    }

    private static MeetingCommandResult? CheckPhaseAndVersion(DiscussionStateRow? state, int expectedVersion) =>
        state!.Phase != MeetingPhase.Discuss.ToString()
            ? new MeetingCommandResult(MeetingCommandStatus.InvalidPhase)
            : state.Version != expectedVersion
                ? new MeetingCommandResult(MeetingCommandStatus.VersionConflict)
                : null;

    private static async Task<MeetingCommandResult?> FindReplayAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId,
        Guid operationId,
        string operationType,
        string fingerprint)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ReplayRow>(
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
        Guid resultId,
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
                ResultId = resultId.ToString(),
                ResultVersion = resultVersion,
                CreatedAtUtc = nowUtc.ToString("O"),
            },
            transaction);

    private static string OperationType(EntryKind kind) => kind switch
    {
        EntryKind.Comment => "CreateComment",
        EntryKind.Decision => "CreateDecision",
        EntryKind.Action => "CreateAction",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string TableName(EntryKind kind) => kind switch
    {
        EntryKind.Comment => "comments",
        EntryKind.Decision => "decisions",
        EntryKind.Action => "actions",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private enum EntryKind
    {
        Comment,
        Decision,
        Action,
    }

    private sealed class DiscussionStateRow
    {
        public required string Phase { get; init; }
        public int Version { get; init; }
        public string? FacilitatorId { get; init; }
        public string? MemberStatus { get; init; }
    }

    private sealed class DiscussionTopicRow
    {
        public int Position { get; init; }
        public required string Status { get; init; }
        public string? CurrentTopicId { get; init; }
    }

    private sealed class EntryRow
    {
        public required string Text { get; init; }
        public string? AssigneeId { get; init; }
    }

    private sealed class ReplayRow
    {
        public required string OperationType { get; init; }
        public required string Fingerprint { get; init; }
        public string? ResultId { get; init; }
        public int ResultVersion { get; init; }
    }
}
