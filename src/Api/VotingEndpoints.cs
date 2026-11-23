using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class VotingEndpoints
{
    public static void MapVotingEndpoints(this WebApplication app)
    {
        var meetings = app.MapGroup("/api/meetings/{meetingId:guid}").RequireAuthorization();

        meetings.MapPut("/vote-settings", async (
            Guid meetingId,
            UpdateVoteSettingsRequest request,
            ClaimsPrincipal user,
            VotingService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateSettingsAsync(
                CurrentUser.Id(user),
                meetingId,
                request.Mode,
                request.Percent,
                request.Minimum,
                request.Fixed,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapPut("/topics/{topicId:guid}/my-votes", async (
            Guid meetingId,
            Guid topicId,
            SetMyVotesRequest request,
            ClaimsPrincipal user,
            VotingService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetMyVotesAsync(
                CurrentUser.Id(user),
                meetingId,
                topicId,
                request.Count,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new MyVotesResponse(
                result.Version,
                result.BallotVersion,
                result.Remaining));
        });
    }
}
