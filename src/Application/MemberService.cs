using Domain;

namespace Application;

public sealed class MemberService(
    IMeetingRepository meetings,
    MeetingAccessService access,
    IPresenceService presence,
    IMeetingNotifier notifier,
    TimeProvider timeProvider)
{
    public async Task ExcludeAsync(
        Guid currentUserId,
        Guid meetingId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (currentUserId == targetUserId)
        {
            throw new DomainValidationException("CannotExcludeSelf", "Фасилитатор не может исключить себя.");
        }

        await access.GetAsync(currentUserId, meetingId, true, cancellationToken);
        var excluded = await meetings.ExcludeMemberAsync(
            meetingId,
            currentUserId,
            targetUserId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (!excluded)
        {
            throw new ConflictException("MemberChanged", "Участник или роль изменились. Обновите состояние.");
        }

        await notifier.AccessRevokedAsync(meetingId, targetUserId, cancellationToken);
        var snapshot = await access.GetAsync(currentUserId, meetingId, false, cancellationToken);
        await notifier.StateChangedAsync(meetingId, snapshot.Meeting.Version, cancellationToken);
        await notifier.PresenceChangedAsync(meetingId, cancellationToken);
    }

    public async Task TransferAsync(
        Guid currentUserId,
        Guid meetingId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (currentUserId == targetUserId)
        {
            throw new DomainValidationException("InvalidFacilitatorTransfer", "Выберите другого участника.");
        }

        var snapshot = await access.GetAsync(currentUserId, meetingId, true, cancellationToken);
        var target = snapshot.Participants.SingleOrDefault(participant => participant.UserId == targetUserId);
        if (target is null || target.Status != MemberStatus.Active)
        {
            throw new NotFoundException("Участник не найден.");
        }

        var deadline = presence.IsOnline(meetingId, targetUserId)
            ? (DateTimeOffset?)null
            : timeProvider.GetUtcNow().AddSeconds(60);
        var transferred = await meetings.TransferFacilitatorAsync(
            meetingId,
            currentUserId,
            targetUserId,
            deadline,
            cancellationToken);
        if (!transferred)
        {
            throw new ConflictException("RoleChanged", "Роль изменилась. Обновите состояние и повторите передачу.");
        }

        var updated = await access.GetAsync(currentUserId, meetingId, false, cancellationToken);
        await notifier.StateChangedAsync(meetingId, updated.Meeting.Version, cancellationToken);
        await notifier.PresenceChangedAsync(meetingId, cancellationToken);
    }
}
