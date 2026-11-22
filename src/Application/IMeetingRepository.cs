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
}
