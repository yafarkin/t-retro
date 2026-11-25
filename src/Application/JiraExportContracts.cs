using Domain;

namespace Application;

public sealed record JiraIssueExportRequest(
    Guid ExternalRequestId,
    string Text,
    string TopicTitle,
    string? AssigneeSubject,
    string? Description = null);

public enum JiraExportResultStatus
{
    Created,
    Rejected,
    Unknown,
    Unavailable,
}

public sealed record JiraExportResult(
    JiraExportResultStatus Status,
    string? IssueKey = null,
    string? IssueUrl = null);

public interface IJiraIssueExporter
{
    bool IsAvailable { get; }

    Task<JiraExportResult> CreateAsync(
        JiraIssueExportRequest request,
        CancellationToken cancellationToken);

    Task<JiraExportResult> ReconcileAsync(
        Guid externalRequestId,
        CancellationToken cancellationToken);
}

public sealed record JiraExportCommandView(JiraExport Export, int Version);

public sealed record JiraExportCommandResult(
    MeetingCommandStatus Status,
    JiraExport? Export = null,
    JiraIssueExportRequest? Request = null,
    bool ShouldExport = false,
    int Version = 0);

public interface IJiraExportRepository
{
    Task<JiraExportCommandResult> BeginAsync(
        Guid meetingId,
        Guid actionId,
        Guid facilitatorId,
        Guid externalRequestId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<JiraExport> CompleteAsync(
        JiraExport export,
        JiraExportResult result,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
