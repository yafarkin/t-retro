namespace Domain;

public enum DiscussionStatus
{
    Pending,
    Discussed,
    Skipped,
}

public sealed record DiscussionTopic(
    Guid MeetingId,
    Guid TopicId,
    int Position,
    DiscussionStatus Status);

public sealed record DiscussionRanking(
    Guid TopicId,
    int Votes,
    int ColumnPosition);

public static class DiscussionQueue
{
    public static IReadOnlyList<DiscussionTopic> Create(
        Guid meetingId,
        IEnumerable<DiscussionRanking> topics) =>
        topics
            .OrderByDescending(topic => topic.Votes)
            .ThenBy(topic => topic.ColumnPosition)
            .ThenBy(topic => topic.TopicId.ToString(), StringComparer.Ordinal)
            .Select((topic, position) => new DiscussionTopic(
                meetingId,
                topic.TopicId,
                position,
                DiscussionStatus.Pending))
            .ToArray();
}

public sealed record TopicComment(
    Guid Id,
    Guid MeetingId,
    Guid TopicId,
    string Text,
    int? CreatedVersion = null);

public sealed record TopicDecision(
    Guid Id,
    Guid MeetingId,
    Guid TopicId,
    string Text,
    int? CreatedVersion = null);

public sealed record ActionItem(
    Guid Id,
    Guid MeetingId,
    Guid TopicId,
    string Text,
    Guid? AssigneeId,
    string? Description = null,
    int? CreatedVersion = null);
