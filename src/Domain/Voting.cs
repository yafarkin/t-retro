namespace Domain;

public enum TopicKind
{
    Card,
    Group,
}

public sealed record MeetingTopic(
    Guid Id,
    Guid MeetingId,
    TopicKind Kind,
    Guid SourceId);

public sealed record VoteAllocation(
    Guid MeetingId,
    Guid TopicId,
    Guid UserId,
    int Count);

public sealed record PersonalBallot(
    Guid MeetingId,
    Guid UserId,
    int Version);
