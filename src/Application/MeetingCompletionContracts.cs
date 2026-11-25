using Domain;

namespace Application;

public sealed record MeetingCompletionTotals(
    DateTimeOffset ClosedAtUtc,
    int ParticipantCount,
    int CardCount,
    int TopicCount,
    int VoteCount);

public sealed record MeetingTopicTotal(
    Guid MeetingId,
    Guid TopicId,
    int TotalVotes);

public interface IMeetingReportGenerator
{
    byte[] Generate(MeetingStateData state);
}

public interface IMeetingCompletionRepository
{
    Task<MeetingCommandResult> CompleteAsync(
        Guid meetingId,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<byte[]?> GetReportAsync(Guid meetingId, CancellationToken cancellationToken);
}

public static class MeetingCompletionFactory
{
    public static MeetingStateData Create(MeetingStateData state, DateTimeOffset closedAtUtc)
    {
        var topicTotals = state.Topics.Select(topic => new MeetingTopicTotal(
            state.Meeting.Id,
            topic.Id,
            state.Votes.Where(vote => vote.TopicId == topic.Id).Sum(vote => vote.Count))).ToArray();
        var totals = new MeetingCompletionTotals(
            closedAtUtc,
            state.Participants.Count(participant => participant.Status == Domain.MemberStatus.Active),
            state.Cards.Count,
            state.Topics.Count,
            state.Votes.Sum(vote => vote.Count));
        var cards = state.Cards.Select(card => state.Meeting.AnonymousCards || card.Card.Anonymous
            ? card with { AuthorId = null, AuthorDisplayName = null }
            : card).ToArray();
        return state with
        {
            Cards = cards,
            Votes = [],
            ReadyUserIds = new HashSet<Guid>(),
            Ballots = [],
            Completion = totals,
            TopicTotals = topicTotals,
        };
    }
}
