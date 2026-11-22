using Domain;

namespace Application;

public sealed class MeetingAccessService(IMeetingRepository meetings)
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

        return new MeetingSnapshot(state.Meeting, state.Columns, state.Participants, userId);
    }
}
