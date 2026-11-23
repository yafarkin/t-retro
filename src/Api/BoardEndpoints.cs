using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class BoardEndpoints
{
    public static void MapBoardEndpoints(this WebApplication app)
    {
        var meetings = app.MapGroup("/api/meetings/{meetingId:guid}").RequireAuthorization();

        meetings.MapPost("/cards", async (
            Guid meetingId,
            CreateCardRequest request,
            ClaimsPrincipal user,
            MeetingBoardService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateCardAsync(
                CurrentUser.Id(user),
                meetingId,
                request.ColumnId,
                request.Text,
                request.OperationId,
                cancellationToken);
            return Results.Created(
                $"/api/meetings/{meetingId}/cards/{result.EntityId}",
                new CardCreatedResponse(result.EntityId!.Value.ToString(), result.Version));
        });

        meetings.MapPut("/cards/{cardId:guid}", async (
            Guid meetingId,
            Guid cardId,
            UpdateCardRequest request,
            ClaimsPrincipal user,
            MeetingBoardService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateCardAsync(
                CurrentUser.Id(user),
                meetingId,
                cardId,
                request.Text,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapDelete("/cards/{cardId:guid}", async (
            Guid meetingId,
            Guid cardId,
            [Microsoft.AspNetCore.Mvc.FromBody] DeleteCardRequest request,
            ClaimsPrincipal user,
            MeetingBoardService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteCardAsync(
                CurrentUser.Id(user),
                meetingId,
                cardId,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapPut("/ready", async (
            Guid meetingId,
            SetReadinessRequest request,
            ClaimsPrincipal user,
            MeetingBoardService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetReadinessAsync(
                CurrentUser.Id(user),
                meetingId,
                request.Phase,
                request.Ready,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapPost("/advance", async (
            Guid meetingId,
            AdvanceMeetingRequest request,
            ClaimsPrincipal user,
            MeetingBoardService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AdvanceAsync(
                CurrentUser.Id(user),
                meetingId,
                request.FromPhase,
                request.ExpectedVersion,
                request.OperationId,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });
    }
}
