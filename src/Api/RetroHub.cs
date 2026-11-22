using Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Api;

[Authorize]
public sealed class RetroHub(
    MeetingAccessService access,
    IPresenceService presence,
    FacilitatorElectionService elections,
    IMeetingNotifier notifier) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUser.Id(Context.User!);
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        await base.OnConnectedAsync();
    }

    public async Task Subscribe(Guid meetingId)
    {
        var userId = CurrentUser.Id(Context.User!);
        await access.GetAsync(userId, meetingId, false, Context.ConnectionAborted);
        var previous = presence.Connect(Context.ConnectionId, meetingId, userId);
        if (previous is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, MeetingGroup(previous.MeetingId));
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, MeetingGroup(meetingId));
        await elections.ConnectedAsync(meetingId, userId, Context.ConnectionAborted);
        var snapshot = await access.GetAsync(userId, meetingId, false, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("StateChanged", meetingId, snapshot.Meeting.Version);
        await notifier.PresenceChangedAsync(meetingId, Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var previous = presence.Disconnect(Context.ConnectionId);
        if (previous is not null)
        {
            await elections.DisconnectedAsync(previous.MeetingId, previous.UserId, CancellationToken.None);
            await notifier.PresenceChangedAsync(previous.MeetingId, CancellationToken.None);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string MeetingGroup(Guid meetingId) => $"meeting:{meetingId:N}";

    public static string UserGroup(Guid userId) => $"user:{userId:N}";
}
