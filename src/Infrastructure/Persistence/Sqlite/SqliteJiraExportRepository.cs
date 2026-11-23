using System.Data;
using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteJiraExportRepository(SqliteConnectionFactory connections) : IJiraExportRepository
{
    public async Task<JiraExportCommandResult> BeginAsync(
        Guid meetingId,
        Guid actionId,
        Guid facilitatorId,
        Guid externalRequestId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var state = await GetStateAsync(connection, transaction, meetingId, facilitatorId);
        if (state is null || state.MemberStatus is null)
        {
            return new JiraExportCommandResult(MeetingCommandStatus.NotFound);
        }

        if (state.MemberStatus == MemberStatus.Excluded.ToString()
            || state.FacilitatorId != facilitatorId.ToString())
        {
            return new JiraExportCommandResult(MeetingCommandStatus.Forbidden);
        }

        if (state.Phase is not (nameof(MeetingPhase.Discuss) or nameof(MeetingPhase.Closed)))
        {
            return new JiraExportCommandResult(MeetingCommandStatus.InvalidPhase);
        }

        var existing = await FindExportAsync(connection, transaction, meetingId, actionId);
        if (existing is not null)
        {
            return new JiraExportCommandResult(
                MeetingCommandStatus.Success,
                MapExport(existing),
                Version: state.Version);
        }

        var action = await connection.QuerySingleOrDefaultAsync<ExportActionRow>(
            """
            SELECT a.text AS ActionText,
                   COALESCE(g.name, c.text) AS TopicTitle,
                   assignee.subject AS AssigneeSubject
            FROM actions a
            JOIN topics t ON t.meeting_id = a.meeting_id AND t.id = a.topic_id
            LEFT JOIN cards c ON c.meeting_id = t.meeting_id AND c.id = t.card_id
            LEFT JOIN groups g ON g.meeting_id = t.meeting_id AND g.id = t.group_id
            LEFT JOIN members member ON member.meeting_id = a.meeting_id AND member.user_id = a.assignee_id
            LEFT JOIN users assignee ON assignee.id = member.user_id
            WHERE a.meeting_id = @MeetingId AND a.id = @ActionId;
            """,
            new { MeetingId = meetingId.ToString(), ActionId = actionId.ToString() },
            transaction);
        if (action is null)
        {
            return new JiraExportCommandResult(MeetingCommandStatus.NotFound);
        }

        var export = new JiraExport(
            meetingId,
            actionId,
            externalRequestId,
            JiraExportStatus.Pending,
            null,
            null);
        await connection.ExecuteAsync(
            """
            INSERT INTO exports (
                meeting_id, action_id, external_request_id, status,
                issue_key, issue_url, created_at_utc, updated_at_utc)
            VALUES (
                @MeetingId, @ActionId, @ExternalRequestId, 'Pending',
                NULL, NULL, @CreatedAtUtc, @UpdatedAtUtc);
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                ActionId = actionId.ToString(),
                ExternalRequestId = externalRequestId.ToString(),
                CreatedAtUtc = nowUtc.ToString("O"),
                UpdatedAtUtc = nowUtc.ToString("O"),
            },
            transaction);
        var newVersion = state.Version + 1;
        var affected = await connection.ExecuteAsync(
            """
            UPDATE meetings SET version = @NewVersion, updated_at_utc = @Now
            WHERE id = @MeetingId AND version = @ExpectedVersion AND phase IN ('Discuss', 'Closed');
            """,
            new
            {
                MeetingId = meetingId.ToString(),
                NewVersion = newVersion,
                ExpectedVersion = state.Version,
                Now = nowUtc.ToString("O"),
            },
            transaction);
        if (affected == 0)
        {
            return new JiraExportCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return new JiraExportCommandResult(
            MeetingCommandStatus.Success,
            export,
            new JiraIssueExportRequest(
                externalRequestId,
                action.ActionText,
                action.TopicTitle,
                action.AssigneeSubject),
            true,
            newVersion);
    }

    public async Task<JiraExport> CompleteAsync(
        JiraExport export,
        JiraExportResult result,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var status = MapStatus(result.Status);
        if (status == JiraExportStatus.Created
            && (string.IsNullOrWhiteSpace(result.IssueKey) || string.IsNullOrWhiteSpace(result.IssueUrl)))
        {
            throw new InvalidOperationException("Created mock Jira result has no issue key or URL.");
        }

        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var current = await FindExportAsync(connection, transaction, export.MeetingId, export.ActionId);
        if (current is null || current.ExternalRequestId != export.ExternalRequestId.ToString())
        {
            throw new InvalidOperationException("Export intention is missing or changed.");
        }

        if (current.Status != JiraExportStatus.Pending.ToString())
        {
            return MapExport(current);
        }

        await connection.ExecuteAsync(
            """
            UPDATE exports SET status = @Status, issue_key = @IssueKey,
                issue_url = @IssueUrl, updated_at_utc = @UpdatedAtUtc
            WHERE meeting_id = @MeetingId AND action_id = @ActionId AND status = 'Pending';
            """,
            new
            {
                MeetingId = export.MeetingId.ToString(),
                ActionId = export.ActionId.ToString(),
                Status = status.ToString(),
                result.IssueKey,
                result.IssueUrl,
                UpdatedAtUtc = nowUtc.ToString("O"),
            },
            transaction);
        await connection.ExecuteAsync(
            """
            UPDATE meetings SET version = version + 1, updated_at_utc = @Now
            WHERE id = @MeetingId AND phase IN ('Discuss', 'Closed');
            """,
            new { MeetingId = export.MeetingId.ToString(), Now = nowUtc.ToString("O") },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return export with
        {
            Status = status,
            IssueKey = result.IssueKey,
            IssueUrl = result.IssueUrl,
        };
    }

    private static async Task<ExportRow?> FindExportAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid actionId) =>
        await connection.QuerySingleOrDefaultAsync<ExportRow>(
            """
            SELECT meeting_id AS MeetingId, action_id AS ActionId,
                   external_request_id AS ExternalRequestId, status AS Status,
                   issue_key AS IssueKey, issue_url AS IssueUrl
            FROM exports WHERE meeting_id = @MeetingId AND action_id = @ActionId;
            """,
            new { MeetingId = meetingId.ToString(), ActionId = actionId.ToString() },
            transaction);

    private static async Task<ExportStateRow?> GetStateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId) =>
        await connection.QuerySingleOrDefaultAsync<ExportStateRow>(
            """
            SELECT m.phase AS Phase, m.version AS Version, m.facilitator_id AS FacilitatorId,
                   member.status AS MemberStatus
            FROM meetings m
            LEFT JOIN members member ON member.meeting_id = m.id AND member.user_id = @UserId
            WHERE m.id = @MeetingId;
            """,
            new { MeetingId = meetingId.ToString(), UserId = userId.ToString() },
            transaction);

    private static JiraExport MapExport(ExportRow row) =>
        new(
            Guid.Parse(row.MeetingId),
            Guid.Parse(row.ActionId),
            Guid.Parse(row.ExternalRequestId),
            Enum.Parse<JiraExportStatus>(row.Status),
            row.IssueKey,
            row.IssueUrl);

    private static JiraExportStatus MapStatus(JiraExportResultStatus status) => status switch
    {
        JiraExportResultStatus.Created => JiraExportStatus.Created,
        JiraExportResultStatus.Rejected => JiraExportStatus.Rejected,
        JiraExportResultStatus.Unknown => JiraExportStatus.Unknown,
        JiraExportResultStatus.Unavailable => JiraExportStatus.Unavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private sealed class ExportStateRow
    {
        public required string Phase { get; init; }
        public int Version { get; init; }
        public string? FacilitatorId { get; init; }
        public string? MemberStatus { get; init; }
    }

    private sealed class ExportActionRow
    {
        public required string ActionText { get; init; }
        public required string TopicTitle { get; init; }
        public string? AssigneeSubject { get; init; }
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
}
