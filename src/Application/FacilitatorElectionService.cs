using Domain;

namespace Application;

public sealed class FacilitatorElectionService(
    IMeetingRepository meetings,
    MeetingAccessService access,
    IPresenceService presence,
    IMeetingNotifier notifier,
    TimeProvider timeProvider)
{
    public async Task ConnectedAsync(Guid meetingId, Guid userId, CancellationToken cancellationToken)
    {
        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        if (snapshot.Meeting.Phase == MeetingPhase.Closed)
        {
            return;
        }

        if (snapshot.Meeting.FacilitatorId == userId)
        {
            await meetings.CancelElectionAsync(meetingId, userId, cancellationToken);
            return;
        }

        if (snapshot.Meeting.FacilitatorId is null
            && await meetings.AssignFacilitatorIfVacantAsync(meetingId, userId, cancellationToken))
        {
            var updated = await access.GetAsync(userId, meetingId, false, cancellationToken);
            await notifier.StateChangedAsync(meetingId, updated.Meeting.Version, cancellationToken);
        }
    }

    public async Task DisconnectedAsync(Guid meetingId, Guid userId, CancellationToken cancellationToken)
    {
        if (presence.IsOnline(meetingId, userId))
        {
            return;
        }

        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        if (snapshot.Meeting.FacilitatorId == userId && snapshot.Meeting.Phase != MeetingPhase.Closed)
        {
            await meetings.ScheduleElectionAsync(
                meetingId,
                userId,
                timeProvider.GetUtcNow().AddSeconds(60),
                cancellationToken);
        }
    }

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await meetings.ScheduleRecoveryAsync(timeProvider.GetUtcNow().AddSeconds(60), cancellationToken);
    }

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var elections = await meetings.GetDueElectionsAsync(now, cancellationToken);
        foreach (var election in elections)
        {
            var online = presence.OnlineUsers(election.MeetingId).ToArray();
            var newFacilitator = online.Length == 0
                ? (Guid?)null
                : online[Random.Shared.Next(online.Length)];
            if (!await meetings.CompleteElectionAsync(
                    election.MeetingId,
                    election.FacilitatorId,
                    election.Generation,
                    newFacilitator,
                    now,
                    cancellationToken))
            {
                continue;
            }

            await notifier.PresenceChangedAsync(election.MeetingId, cancellationToken);
            if (newFacilitator is not null)
            {
                var snapshot = await access.GetAsync(newFacilitator.Value, election.MeetingId, false, cancellationToken);
                await notifier.StateChangedAsync(election.MeetingId, snapshot.Meeting.Version, cancellationToken);
            }
        }
    }
}
