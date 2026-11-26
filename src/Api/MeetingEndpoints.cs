using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class MeetingEndpoints
{
    public static void MapMeetingEndpoints(this WebApplication app)
    {
        var meetings = app.MapGroup("/api/meetings").RequireAuthorization();
        meetings.MapGet("/", (ClaimsPrincipal user, MeetingService service, CancellationToken cancellationToken) =>
            ListAsync(user, service, false, cancellationToken));
        meetings.MapGet("/archive", (ClaimsPrincipal user, MeetingService service, CancellationToken cancellationToken) =>
            ListAsync(user, service, true, cancellationToken));
        meetings.MapPut("/{meetingId:guid}/archive", async (
            Guid meetingId,
            ArchiveMeetingRequest request,
            ClaimsPrincipal user,
            MeetingService service,
            CancellationToken cancellationToken) =>
        {
            var version = await service.ArchiveAsync(CurrentUser.Id(user), meetingId, request.ExpectedVersion, cancellationToken);
            return Results.Ok(new VersionResponse(version));
        });
        meetings.MapPost("/", async (
            CreateMeetingRequest request,
            ClaimsPrincipal user,
            MeetingService service,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await service.CreateAsync(
                CurrentUser.Id(user),
                request.Name,
                request.Columns.Select(ApiMappings.ToInput).ToArray(),
                new PrivacyInput(request.AnonymousCards, request.PrivateCollection),
                request.VoteSettings.Mode,
                request.VoteSettings.Percent,
                request.VoteSettings.Minimum,
                request.VoteSettings.Fixed,
                request.VotingEnabled ?? true,
                request.StartCollecting,
                cancellationToken);
            return Results.Created(
                $"/api/meetings/{snapshot.Meeting.Id}",
                new MeetingCreatedResponse(snapshot.Meeting.Id.ToString(), snapshot.Meeting.Version));
        });
        meetings.MapGet("/{meetingId:guid}/state", async (
            Guid meetingId,
            ClaimsPrincipal user,
            MeetingService service,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await service.GetStateAsync(CurrentUser.Id(user), meetingId, cancellationToken);
            return Results.Ok(ApiMappings.ToResponse(snapshot));
        });
        meetings.MapGet("/{meetingId:guid}/report.pdf", async (
            Guid meetingId,
            ClaimsPrincipal user,
            MeetingReportService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var report = await service.GetAsync(CurrentUser.Id(user), meetingId, cancellationToken);
            context.Response.Headers["Cache-Control"] = "private, no-store";
            return Results.File(report, "application/pdf");
        });
        meetings.MapGet("/{meetingId:guid}/report.json", async (
            Guid meetingId,
            ClaimsPrincipal user,
            MeetingReportService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await service.GetCompletedSnapshotAsync(CurrentUser.Id(user), meetingId, cancellationToken);
            context.Response.Headers["Cache-Control"] = "private, no-store";
            return Results.File(MeetingJsonReport.Serialize(snapshot), "application/json", "retro-results.json");
        });
        meetings.MapPut("/{meetingId:guid}/settings", async (
            Guid meetingId,
            UpdateMeetingSettingsRequest request,
            ClaimsPrincipal user,
            MeetingService service,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await service.UpdateDraftAsync(
                CurrentUser.Id(user),
                meetingId,
                request.ExpectedVersion,
                request.Name,
                request.Columns.Select(ApiMappings.ToInput).ToArray(),
                new PrivacyInput(request.AnonymousCards, request.PrivateCollection),
                request.VoteSettings.Mode,
                request.VoteSettings.Percent,
                request.VoteSettings.Minimum,
                request.VoteSettings.Fixed,
                request.VotingEnabled,
                cancellationToken);
            return Results.Ok(ApiMappings.ToResponse(snapshot));
        });
        meetings.MapGet("/{meetingId:guid}/invite", async (
            Guid meetingId,
            ClaimsPrincipal user,
            HttpRequest httpRequest,
            InvitationService service,
            CancellationToken cancellationToken) =>
        {
            var invitation = await service.GetAsync(CurrentUser.Id(user), meetingId, cancellationToken);
            return Results.Ok(new InviteResponse(
                BuildInviteUrl(httpRequest, invitation.Token),
                invitation.Version));
        });
        meetings.MapPost("/{meetingId:guid}/invite/rotate", async (
            Guid meetingId,
            ClaimsPrincipal user,
            HttpRequest httpRequest,
            InvitationService service,
            CancellationToken cancellationToken) =>
        {
            var invitation = await service.RotateAsync(CurrentUser.Id(user), meetingId, cancellationToken);
            return Results.Ok(new InviteResponse(
                BuildInviteUrl(httpRequest, invitation.Token),
                invitation.Version));
        });

        app.MapPost("/api/join", async (
            JoinRequest request,
            ClaimsPrincipal user,
            InvitationService service,
            CancellationToken cancellationToken) =>
        {
            var meetingId = await service.JoinAsync(CurrentUser.Id(user), request.Token ?? string.Empty, cancellationToken);
            return Results.Ok(new JoinResponse(meetingId.ToString()));
        }).RequireAuthorization();
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user, MeetingService service, bool archived, CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(CurrentUser.Id(user), archived, cancellationToken);
        return Results.Ok(result.Select(meeting => new MeetingSummaryResponse(
            meeting.Id.ToString(), meeting.Name, meeting.Phase.ToString(), meeting.CreatedAtUtc)));
    }

    private static string BuildInviteUrl(HttpRequest request, string token) =>
        $"{request.Scheme}://{request.Host}/join?token={Uri.EscapeDataString(token)}";
}
