using Application;
using Microsoft.AspNetCore.SignalR;

namespace Api;

public sealed class SignalRMeetingNotifier(
    IHubContext<RetroHub> hub,
    IPresenceService presence) : IMeetingNotifier
{
    public Task StateChangedAsync(Guid meetingId, int version, CancellationToken cancellationToken) =>
        hub.Clients.Group(RetroHub.MeetingGroup(meetingId))
            .SendAsync("StateChanged", meetingId, version, cancellationToken);

    public Task PresenceChangedAsync(Guid meetingId, CancellationToken cancellationToken) =>
        hub.Clients.Group(RetroHub.MeetingGroup(meetingId))
            .SendAsync("PresenceChanged", meetingId, cancellationToken);

    public async Task AccessRevokedAsync(Guid meetingId, Guid userId, CancellationToken cancellationToken)
    {
        var connectionIds = presence.RevokeUser(meetingId, userId);
        foreach (var connectionId in connectionIds)
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, RetroHub.MeetingGroup(meetingId), cancellationToken);
        }

        await hub.Clients.Group(RetroHub.UserGroup(userId))
            .SendAsync("AccessRevoked", meetingId, cancellationToken);
        await PresenceChangedAsync(meetingId, cancellationToken);
    }
}
