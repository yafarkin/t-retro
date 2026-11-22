using System.Security.Claims;
using Application;

namespace Api;

public sealed record UpdateFacilitatorRequest(Guid UserId);

public static class MemberEndpoints
{
    public static void MapMemberEndpoints(this WebApplication app)
    {
        var members = app.MapGroup("/api/meetings/{meetingId:guid}").RequireAuthorization();
        members.MapPost("/members/{targetUserId:guid}/exclude", async (
            Guid meetingId,
            Guid targetUserId,
            ClaimsPrincipal user,
            MemberService service,
            CancellationToken cancellationToken) =>
        {
            await service.ExcludeAsync(CurrentUser.Id(user), meetingId, targetUserId, cancellationToken);
            return Results.NoContent();
        });
        members.MapPut("/facilitator", async (
            Guid meetingId,
            UpdateFacilitatorRequest request,
            ClaimsPrincipal user,
            MemberService service,
            CancellationToken cancellationToken) =>
        {
            await service.TransferAsync(CurrentUser.Id(user), meetingId, request.UserId, cancellationToken);
            return Results.NoContent();
        });
    }
}
