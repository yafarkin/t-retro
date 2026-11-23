using Domain;

namespace Application;

public interface IMeetingRepository
{
    Task CreateAsync(
        Meeting meeting,
        IReadOnlyList<MeetingColumn> columns,
        MeetingMember creator,
        CancellationToken cancellationToken);

    Task<MeetingStateData?> GetStateAsync(Guid meetingId, CancellationToken cancellationToken);

    Task<MeetingMember?> FindMemberAsync(
        Guid meetingId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MeetingSummary>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<int> UpdateDraftAsync(
        Meeting meeting,
        IReadOnlyList<MeetingColumn> columns,
        int expectedVersion,
        Guid facilitatorId,
        CancellationToken cancellationToken);

    Task<int> RotateInvitationAsync(
        Guid meetingId,
        Guid facilitatorId,
        CancellationToken cancellationToken);

    Task<JoinMeetingResult> JoinAsync(
        Guid meetingId,
        Guid userId,
        int invitationVersion,
        CancellationToken cancellationToken);

    Task<bool> ExcludeMemberAsync(
        Guid meetingId,
        Guid facilitatorId,
        Guid targetUserId,
        DateTimeOffset excludedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TransferFacilitatorAsync(
        Guid meetingId,
        Guid currentFacilitatorId,
        Guid targetUserId,
        DateTimeOffset? electionDeadlineUtc,
        CancellationToken cancellationToken);

    Task<bool> ScheduleElectionAsync(
        Guid meetingId,
        Guid facilitatorId,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken);

    Task<bool> CancelElectionAsync(
        Guid meetingId,
        Guid facilitatorId,
        CancellationToken cancellationToken);

    Task<bool> AssignFacilitatorIfVacantAsync(
        Guid meetingId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ScheduleRecoveryAsync(
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FacilitatorElection>> GetDueElectionsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<bool> CompleteElectionAsync(
        Guid meetingId,
        Guid? expectedFacilitatorId,
        int expectedGeneration,
        Guid? newFacilitatorId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> CreateCardAsync(
        Card card,
        Guid authorId,
        Guid operationId,
        string fingerprint,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> UpdateCardAsync(
        Card card,
        Guid authorId,
        int expectedCardVersion,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> DeleteCardAsync(
        Guid meetingId,
        Guid cardId,
        Guid authorId,
        int expectedCardVersion,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> SetReadinessAsync(
        Guid meetingId,
        Guid userId,
        MeetingPhase phase,
        bool ready,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> AdvanceAsync(
        Guid meetingId,
        Guid facilitatorId,
        MeetingPhase fromPhase,
        MeetingPhase toPhase,
        int expectedVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
