using Api.Contracts;
using Application;
using Domain;

namespace Api;

public static class ApiMappings
{
    public static MeetingStateResponse ToResponse(MeetingSnapshot snapshot) =>
        new(
            DateTimeOffset.UtcNow,
            snapshot.Meeting.Id.ToString(),
            snapshot.Meeting.Name,
            snapshot.Meeting.Phase.ToString(),
            snapshot.Meeting.Version,
            snapshot.Meeting.BoardVersion,
            snapshot.CanManage,
            snapshot.Meeting.FacilitatorId?.ToString(),
            snapshot.Meeting.AnonymousCards,
            snapshot.Meeting.PrivateCollection,
            snapshot.Meeting.AnonymousVotes,
            new VoteSettingsResponse(
                snapshot.Meeting.VoteSettings.Mode.ToString().ToLowerInvariant(),
                snapshot.Meeting.VoteSettings.Percent,
                snapshot.Meeting.VoteSettings.Minimum,
                snapshot.Meeting.VoteSettings.Fixed),
            snapshot.Columns.Select(column => new ColumnResponse(
                column.Id.ToString(),
                column.Name,
                column.Color,
                column.Position)).ToArray(),
            snapshot.Participants.Select(participant => new ParticipantResponse(
                participant.UserId.ToString(),
                participant.Subject,
                participant.DisplayName,
                participant.Status.ToString(),
                participant.IsOnline)).ToArray(),
            snapshot.Cards.Select(card => new MeetingCardResponse(
                card.Id.ToString(),
                card.ColumnId.ToString(),
                card.Text,
                card.AuthorId?.ToString(),
                card.AuthorDisplayName,
                card.IsMine,
                card.IsHidden,
                card.Version,
                card.GroupId?.ToString())).ToArray(),
            snapshot.Groups.Select(group => new MeetingGroupResponse(
                group.Id.ToString(),
                group.ColumnId.ToString(),
                group.Name)).ToArray(),
            snapshot.Topics.Select(topic => new MeetingTopicResponse(
                topic.Id.ToString(),
                topic.Kind.ToString().ToLowerInvariant(),
                topic.SourceId.ToString(),
                topic.TotalVotes,
                topic.MyVotes,
                topic.Voters?.Select(voter => new MeetingTopicVoterResponse(
                    voter.UserId.ToString(),
                    voter.DisplayName,
                    voter.Count)).ToArray())).ToArray(),
            snapshot.Voting is null
                ? null
                : new MeetingVotingResponse(
                    snapshot.Voting.OriginalCardCount,
                    snapshot.Voting.Budget,
                    snapshot.Voting.Spent,
                    snapshot.Voting.Remaining,
                    snapshot.Voting.BallotVersion),
            snapshot.Discussion is null ? null : ToResponse(snapshot.Discussion),
            snapshot.Timer is null
                ? null
                : new MeetingTimerResponse(
                    snapshot.Timer.RunId.ToString(),
                    snapshot.Timer.Phase.ToString(),
                    snapshot.Timer.EndsAtUtc,
                    snapshot.Timer.ServerNowUtc),
            new MeetingIntegrationsResponse(snapshot.Integrations.Ai, snapshot.Integrations.Jira),
            snapshot.Readiness is null
                ? null
                : new MeetingReadinessResponse(
                    snapshot.Readiness.ReadyCount,
                    snapshot.Readiness.TotalCount,
                    snapshot.Readiness.MyReady),
            snapshot.Completion is null
                ? null
                : new MeetingTotalsResponse(
                    snapshot.Completion.ClosedAtUtc,
                    snapshot.Completion.ParticipantCount,
                    snapshot.Completion.CardCount,
                    snapshot.Completion.TopicCount,
                    snapshot.Completion.VoteCount));

    public static ColumnInput ToInput(ColumnRequest column) => new(column.Name, column.Color);

    private static MeetingDiscussionResponse ToResponse(MeetingDiscussion discussion) =>
        new(
            discussion.CurrentTopicId?.ToString(),
            discussion.Queue.Select(topic => new DiscussionTopicResponse(
                topic.TopicId.ToString(),
                topic.Position,
                topic.Status.ToString().ToLowerInvariant())).ToArray(),
            discussion.Comments.Select(comment => new TopicCommentResponse(
                comment.Id.ToString(),
                comment.TopicId.ToString(),
                comment.Text)).ToArray(),
            discussion.Decisions.Select(decision => new TopicDecisionResponse(
                decision.Id.ToString(),
                decision.TopicId.ToString(),
                decision.Text)).ToArray(),
            discussion.Actions.Select(action => ToResponse(action, discussion.Exports)).ToArray());

    private static ActionItemResponse ToResponse(ActionItem action, IReadOnlyList<JiraExport> exports)
    {
        var export = exports.SingleOrDefault(item => item.ActionId == action.Id);
        return new ActionItemResponse(
            action.Id.ToString(),
            action.TopicId.ToString(),
            action.Text,
            action.AssigneeId?.ToString(),
            export?.Status.ToString().ToLowerInvariant() ?? "notExported",
            export?.ExternalRequestId.ToString(),
            export?.IssueKey,
            export?.IssueUrl);
    }
}
