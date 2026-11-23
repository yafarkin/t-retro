using Domain;

namespace Application;

public sealed class MeetingAccessService(
    IMeetingRepository meetings,
    IPresenceService presence,
    IGroupSuggestionProvider groupSuggestions,
    IJiraIssueExporter jiraExporter,
    IMeetingTimer timer,
    TimeProvider timeProvider)
{
    public async Task<MeetingSnapshot> GetAsync(
        Guid userId,
        Guid meetingId,
        bool requireFacilitator,
        CancellationToken cancellationToken)
    {
        var state = await meetings.GetStateAsync(meetingId, cancellationToken)
            ?? throw new NotFoundException("Встреча не найдена.");
        var member = state.Participants.SingleOrDefault(participant => participant.UserId == userId);
        if (member is null)
        {
            throw new NotFoundException("Встреча не найдена.");
        }

        if (member.Status == MemberStatus.Excluded)
        {
            throw new ForbiddenException("AccessRevoked", "Доступ к встрече закрыт.");
        }

        if (requireFacilitator && state.Meeting.FacilitatorId != userId)
        {
            throw new ForbiddenException("FacilitatorRequired", "Действие доступно только фасилитатору.");
        }

        var participants = state.Participants.Select(participant => participant with
        {
            IsOnline = presence.IsOnline(meetingId, participant.UserId),
        }).ToArray();
        var cards = state.Cards
            .Select(card => MeetingCardVisibility.Project(state.Meeting, card, userId))
            .ToArray();
        var activeParticipantIds = participants
            .Where(participant => participant.Status == MemberStatus.Active)
            .Select(participant => participant.UserId)
            .ToHashSet();
        var readiness = state.Meeting.Phase is MeetingPhase.Collect or MeetingPhase.Vote
            ? new MeetingReadiness(
                state.ReadyUserIds.Count(activeParticipantIds.Contains),
                activeParticipantIds.Count,
                state.ReadyUserIds.Contains(userId))
            : null;
        var showTopics = state.Meeting.Phase is MeetingPhase.Vote or MeetingPhase.Discuss or MeetingPhase.Closed;
        var topics = showTopics
            ? state.Topics.Select(topic => ProjectTopic(state, participants, topic, userId)).ToArray()
            : [];
        var voting = state.Meeting.Phase == MeetingPhase.Vote
            ? ProjectVoting(state, userId)
            : null;
        var discussion = state.Meeting.Phase is MeetingPhase.Discuss or MeetingPhase.Closed
            ? new MeetingDiscussion(
                state.DiscussionQueue,
                state.CurrentTopicId,
                state.Comments,
                state.Decisions,
                state.Actions,
                state.Exports)
            : null;
        var currentTimer = state.Meeting.Phase is MeetingPhase.Collect or MeetingPhase.Group or MeetingPhase.Vote or MeetingPhase.Discuss
            ? timer.Get(meetingId, timeProvider.GetUtcNow())
            : null;
        if (currentTimer?.Phase != state.Meeting.Phase)
        {
            currentTimer = null;
        }

        return new MeetingSnapshot(
            state.Meeting,
            state.Columns,
            participants,
            cards,
            state.Groups,
            topics,
            voting,
            discussion,
            readiness,
            currentTimer,
            new MeetingIntegrations(groupSuggestions.IsAvailable, jiraExporter.IsAvailable),
            userId,
            state.Completion);
    }

    private static MeetingTopicResult ProjectTopic(
        MeetingStateData state,
        IReadOnlyList<MeetingParticipant> participants,
        MeetingTopic topic,
        Guid viewerId)
    {
        var votes = state.Votes.Where(vote => vote.TopicId == topic.Id).ToArray();
        var voters = state.Meeting.AnonymousVotes
            ? null
            : votes.Select(vote =>
            {
                var participant = participants.Single(item => item.UserId == vote.UserId);
                return new MeetingTopicVoter(vote.UserId, participant.DisplayName, vote.Count);
            }).ToArray();
        var totalVotes = state.TopicTotals?.SingleOrDefault(total => total.TopicId == topic.Id)?.TotalVotes
            ?? votes.Sum(vote => vote.Count);
        return new MeetingTopicResult(
            topic.Id,
            topic.Kind,
            topic.SourceId,
            totalVotes,
            votes.Where(vote => vote.UserId == viewerId).Sum(vote => vote.Count),
            voters);
    }

    private static MeetingVoting ProjectVoting(MeetingStateData state, Guid viewerId)
    {
        var originalCardCount = state.Meeting.InitialCardCount
            ?? throw new InvalidOperationException("Voting has no original card count.");
        var budget = state.Meeting.VoteSettings.BudgetFor(originalCardCount);
        var spent = state.Votes.Where(vote => vote.UserId == viewerId).Sum(vote => vote.Count);
        var ballotVersion = state.Ballots.SingleOrDefault(ballot => ballot.UserId == viewerId)?.Version ?? 0;
        return new MeetingVoting(
            originalCardCount,
            budget,
            spent,
            budget - spent,
            ballotVersion);
    }
}
