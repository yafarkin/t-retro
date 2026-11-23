namespace Domain;

public enum JiraExportStatus
{
    Pending,
    Created,
    Rejected,
    Unknown,
    Unavailable,
}

public sealed record JiraExport(
    Guid MeetingId,
    Guid ActionId,
    Guid ExternalRequestId,
    JiraExportStatus Status,
    string? IssueKey,
    string? IssueUrl);
