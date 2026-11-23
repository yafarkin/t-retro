using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class GroupingEndpoints
{
    public static void MapGroupingEndpoints(this WebApplication app)
    {
        var meetings = app.MapGroup("/api/meetings/{meetingId:guid}").RequireAuthorization();

        meetings.MapPost("/moves", async (
            Guid meetingId,
            MoveCardRequest request,
            ClaimsPrincipal user,
            GroupingService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.MoveAsync(
                CurrentUser.Id(user),
                meetingId,
                request.SourceKind,
                request.SourceId,
                request.TargetKind,
                request.TargetId,
                request.ExpectedBoardVersion,
                request.OperationId,
                cancellationToken);
            return Results.Ok(new BoardVersionResponse(result.Version, result.BoardVersion));
        });

        meetings.MapPut("/groups/{groupId:guid}", async (
            Guid meetingId,
            Guid groupId,
            RenameGroupRequest request,
            ClaimsPrincipal user,
            GroupingService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RenameGroupAsync(
                CurrentUser.Id(user),
                meetingId,
                groupId,
                request.Name,
                request.ExpectedBoardVersion,
                cancellationToken);
            return Results.Ok(new BoardVersionResponse(result.Version, result.BoardVersion));
        });

        meetings.MapPost("/ai/preview", async (
            Guid meetingId,
            ClaimsPrincipal user,
            GroupingService service,
            CancellationToken cancellationToken) =>
        {
            var proposal = await service.PreviewAsync(CurrentUser.Id(user), meetingId, cancellationToken);
            return Results.Ok(new GroupProposalResponse(
                proposal.Id.ToString(),
                proposal.BoardVersion,
                proposal.Groups.Select(group => new SuggestedGroupResponse(
                    group.Name,
                    group.ColumnId.ToString(),
                    group.CardIds.Select(id => id.ToString()).ToArray())).ToArray()));
        });

        meetings.MapPost("/ai/apply", async (
            Guid meetingId,
            ApplyGroupProposalRequest request,
            ClaimsPrincipal user,
            GroupingService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ApplyProposalAsync(
                CurrentUser.Id(user),
                meetingId,
                request.ProposalId,
                request.ExpectedBoardVersion,
                request.OperationId,
                cancellationToken);
            return Results.Ok(new BoardVersionResponse(result.Version, result.BoardVersion));
        });
    }
}
