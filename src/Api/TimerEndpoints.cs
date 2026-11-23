using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class TimerEndpoints
{
    public static void MapTimerEndpoints(this WebApplication app)
    {
        var meetings = app.MapGroup("/api/meetings/{meetingId:guid}").RequireAuthorization();

        meetings.MapPost("/timer/start", async (
            Guid meetingId,
            StartTimerRequest request,
            ClaimsPrincipal user,
            TimerService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.StartAsync(
                CurrentUser.Id(user),
                meetingId,
                request.Seconds,
                request.OperationId,
                cancellationToken);
            return Results.Ok(ToResponse(result));
        });

        meetings.MapPost("/timer/add", async (
            Guid meetingId,
            AddTimerRequest request,
            ClaimsPrincipal user,
            TimerService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AddAsync(
                CurrentUser.Id(user),
                meetingId,
                request.RunId,
                request.Seconds,
                request.OperationId,
                cancellationToken);
            return Results.Ok(ToResponse(result));
        });

        meetings.MapPost("/timer/stop", async (
            Guid meetingId,
            StopTimerRequest request,
            ClaimsPrincipal user,
            TimerService service,
            CancellationToken cancellationToken) =>
        {
            await service.StopAsync(
                CurrentUser.Id(user),
                meetingId,
                request.RunId,
                request.OperationId,
                cancellationToken);
            return Results.NoContent();
        });
    }

    private static MeetingTimerResponse ToResponse(MeetingTimer timer) =>
        new(
            timer.RunId.ToString(),
            timer.Phase.ToString(),
            timer.EndsAtUtc,
            timer.ServerNowUtc);
}
