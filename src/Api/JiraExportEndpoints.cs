using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class JiraExportEndpoints
{
    public static void MapJiraExportEndpoints(this WebApplication app)
    {
        app.MapPost("/api/meetings/{meetingId:guid}/actions/{actionId:guid}/export", async (
            Guid meetingId,
            Guid actionId,
            ClaimsPrincipal user,
            JiraExportService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ExportAsync(
                CurrentUser.Id(user),
                meetingId,
                actionId,
                cancellationToken);
            return Results.Ok(new JiraExportResponse(
                result.Export.Status.ToString().ToLowerInvariant(),
                result.Export.ExternalRequestId.ToString(),
                result.Export.IssueKey,
                result.Export.IssueUrl,
                result.Version));
        }).RequireAuthorization();
    }
}
