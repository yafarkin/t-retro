using Domain;

namespace Application;

public sealed record MeetingTopicVoter(Guid UserId, string DisplayName, int Count);

public sealed record MeetingTopicResult(
    Guid Id,
    TopicKind Kind,
    Guid SourceId,
    int TotalVotes,
    int MyVotes,
    IReadOnlyList<MeetingTopicVoter>? Voters);

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
    int MinimumBudget = 0,
    bool IsReplay = false);

public interface IVotingRepository
{
    Task<VotingCommandResult> SetMyVotesAsync(
        Guid meetingId,
        Guid userId,
        Guid topicId,
        int count,
        int expectedBallotVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<VotingCommandResult> UpdateSettingsAsync(
        Guid meetingId,
        Guid facilitatorId,
        VoteBudgetSettings settings,
        int expectedVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
