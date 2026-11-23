using Domain;

namespace Application;

public sealed record MeetingDiscussion(
    IReadOnlyList<DiscussionTopic> Queue,
    Guid? CurrentTopicId,
    IReadOnlyList<TopicComment> Comments,
    IReadOnlyList<TopicDecision> Decisions,
    IReadOnlyList<ActionItem> Actions);

public interface IDiscussionRepository
{
    Task<MeetingCommandResult> SelectCurrentAsync(
        Guid meetingId,
        Guid facilitatorId,
        Guid topicId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> SetTopicStatusAsync(
        Guid meetingId,
        Guid facilitatorId,
        Guid topicId,
        DiscussionStatus status,
        bool advance,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> CreateCommentAsync(
        TopicComment comment,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> UpdateCommentAsync(
        Guid meetingId,
        Guid commentId,
        Guid facilitatorId,
        string text,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> DeleteCommentAsync(
        Guid meetingId,
        Guid commentId,
        Guid facilitatorId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> CreateDecisionAsync(
        TopicDecision decision,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> UpdateDecisionAsync(
        Guid meetingId,
        Guid decisionId,
        Guid facilitatorId,
        string text,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> DeleteDecisionAsync(
        Guid meetingId,
        Guid decisionId,
        Guid facilitatorId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> CreateActionAsync(
        ActionItem action,
        Guid facilitatorId,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> UpdateActionAsync(
        Guid meetingId,
        Guid actionId,
        Guid facilitatorId,
        string text,
        Guid? assigneeId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> DeleteActionAsync(
        Guid meetingId,
        Guid actionId,
        Guid facilitatorId,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
