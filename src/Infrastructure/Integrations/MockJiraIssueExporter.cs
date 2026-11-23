using Application;

namespace Infrastructure.Integrations;

public sealed class MockJiraIssueExporter(bool enabled, string outcome, int delayMilliseconds = 0) : IJiraIssueExporter
{
    private int createCallCount;

    public bool IsAvailable => enabled;

    public int CreateCallCount => createCallCount;

    public async Task<JiraExportResult> CreateAsync(
        JiraIssueExportRequest request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref createCallCount);
        await Task.Delay(delayMilliseconds, cancellationToken);
        return Result(request.ExternalRequestId);
    }

    public Task<JiraExportResult> ReconcileAsync(
        Guid externalRequestId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result(externalRequestId));

    private JiraExportResult Result(Guid externalRequestId) => outcome switch
    {
        "Created" => new JiraExportResult(
            JiraExportResultStatus.Created,
            $"MOCK-{externalRequestId:N}",
            $"https://jira.local.invalid/browse/MOCK-{externalRequestId:N}"),
        "Rejected" => new JiraExportResult(JiraExportResultStatus.Rejected),
        "Unknown" => new JiraExportResult(JiraExportResultStatus.Unknown),
        "Unavailable" => new JiraExportResult(JiraExportResultStatus.Unavailable),
        _ => throw new InvalidOperationException("Unknown mock Jira outcome."),
    };
}
