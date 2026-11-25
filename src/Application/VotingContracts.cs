using Domain;

namespace Application;

public sealed record MeetingTopicResult(
    Guid Id,
    TopicKind Kind,
    Guid SourceId,
    int? TotalVotes,
    int? MyVotes);

public sealed record MeetingVoting(
    int OriginalCardCount,
    int Budget,
    int Spent,
    int Remaining,
    int BallotVersion);

public enum VotingCommandStatus
{
    Success,
    NotFound,
    Forbidden,
    InvalidPhase,
    VersionConflict,
    BudgetExceeded,
    BudgetTooLow,
}

public sealed record VotingCommandResult(
    VotingCommandStatus Status,
    int Version = 0,
    int BallotVersion = 0,
    int Remaining = 0,
    bool IsReplay = false);

public interface IVotingRepository
{
    Task<VotingCommandResult> SetMyVotesAsync(
        Guid meetingId,
        Guid userId,
        Guid topicId,
        int count,
        int expectedBallotVersion,
        CancellationToken cancellationToken);

    Task<VotingCommandResult> UpdateSettingsAsync(
        Guid meetingId,
        Guid facilitatorId,
        VoteBudgetSettings settings,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
