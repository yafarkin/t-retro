using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class DiscussionEndpoints
{
    public static void MapDiscussionEndpoints(this WebApplication app)
    {
        var meetings = app.MapGroup("/api/meetings/{meetingId:guid}").RequireAuthorization();

        meetings.MapPut("/discussion/current", async (
            Guid meetingId,
            SelectCurrentTopicRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SelectCurrentAsync(
                CurrentUser.Id(user),
                meetingId,
                request.TopicId,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapPut("/topics/{topicId:guid}/status", async (
            Guid meetingId,
            Guid topicId,
            SetTopicStatusRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetTopicStatusAsync(
                CurrentUser.Id(user),
                meetingId,
                topicId,
                request.Status,
                request.Advance,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapPost("/topics/{topicId:guid}/comments", async (
            Guid meetingId,
            Guid topicId,
            CreateCommentRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateCommentAsync(
                CurrentUser.Id(user),
                meetingId,
                topicId,
                request.Text,
                request.ExpectedVersion,
                request.OperationId,
                cancellationToken);
            return Results.Created(
                $"/api/meetings/{meetingId}/comments/{result.EntityId}",
                new DiscussionEntryCreatedResponse(result.EntityId!.Value.ToString(), result.Version));
        });

        meetings.MapPut("/comments/{commentId:guid}", async (
            Guid meetingId,
            Guid commentId,
            UpdateCommentRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateCommentAsync(
                CurrentUser.Id(user),
                meetingId,
                commentId,
                request.Text,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapDelete("/comments/{commentId:guid}", async (
            Guid meetingId,
            Guid commentId,
            [Microsoft.AspNetCore.Mvc.FromBody] DeleteDiscussionEntryRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteCommentAsync(
                CurrentUser.Id(user),
                meetingId,
                commentId,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapPost("/topics/{topicId:guid}/decisions", async (
            Guid meetingId,
            Guid topicId,
            CreateDecisionRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateDecisionAsync(
                CurrentUser.Id(user),
                meetingId,
                topicId,
                request.Text,
                request.ExpectedVersion,
                request.OperationId,
                cancellationToken);
            return Results.Created(
                $"/api/meetings/{meetingId}/decisions/{result.EntityId}",
                new DiscussionEntryCreatedResponse(result.EntityId!.Value.ToString(), result.Version));
        });

        meetings.MapPut("/decisions/{decisionId:guid}", async (
            Guid meetingId,
            Guid decisionId,
            UpdateDecisionRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateDecisionAsync(
                CurrentUser.Id(user),
                meetingId,
                decisionId,
                request.Text,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapDelete("/decisions/{decisionId:guid}", async (
            Guid meetingId,
            Guid decisionId,
            [Microsoft.AspNetCore.Mvc.FromBody] DeleteDiscussionEntryRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteDecisionAsync(
                CurrentUser.Id(user),
                meetingId,
                decisionId,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapPost("/topics/{topicId:guid}/actions", async (
            Guid meetingId,
            Guid topicId,
            CreateActionRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateActionAsync(
                CurrentUser.Id(user),
                meetingId,
                topicId,
                request.Text,
                request.Description,
                request.AssigneeId,
                request.ExpectedVersion,
                request.OperationId,
                cancellationToken);
            return Results.Created(
                $"/api/meetings/{meetingId}/actions/{result.EntityId}",
                new DiscussionEntryCreatedResponse(result.EntityId!.Value.ToString(), result.Version));
        });

        meetings.MapPut("/actions/{actionId:guid}", async (
            Guid meetingId,
            Guid actionId,
            UpdateActionRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateActionAsync(
                CurrentUser.Id(user),
                meetingId,
                actionId,
                request.Text,
                request.Description,
                request.AssigneeId,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });

        meetings.MapDelete("/actions/{actionId:guid}", async (
            Guid meetingId,
            Guid actionId,
            [Microsoft.AspNetCore.Mvc.FromBody] DeleteDiscussionEntryRequest request,
            ClaimsPrincipal user,
            DiscussionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteActionAsync(
                CurrentUser.Id(user),
                meetingId,
                actionId,
                request.ExpectedVersion,
                cancellationToken);
            return Results.Ok(new VersionResponse(result.Version));
        });
    }
}
