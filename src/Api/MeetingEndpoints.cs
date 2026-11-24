using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class MeetingEndpoints
{
    public static void MapMeetingEndpoints(this WebApplication app)
    {
        var meetings = app.MapGroup("/api/meetings").RequireAuthorization();
        meetings.MapGet("/", async (
            ClaimsPrincipal user,
            MeetingService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(CurrentUser.Id(user), cancellationToken);
            return Results.Ok(result.Select(meeting => new MeetingSummaryResponse(
                meeting.Id.ToString(),
                meeting.Name,
                meeting.Phase.ToString(),
                meeting.CreatedAtUtc)));
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
                new PrivacyInput(request.AnonymousCards, request.PrivateCollection, request.AnonymousVotes),
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
                new PrivacyInput(request.AnonymousCards, request.PrivateCollection, request.AnonymousVotes),
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

    private static string BuildInviteUrl(HttpRequest request, string token) =>
        $"{request.Scheme}://{request.Host}/join?token={Uri.EscapeDataString(token)}";
}
