using Domain;

namespace Application;

public sealed record PresenceConnection(string ConnectionId, Guid MeetingId, Guid UserId);

public interface IPresenceService
{
    PresenceConnection? Connect(string connectionId, Guid meetingId, Guid userId);

    PresenceConnection? Disconnect(string connectionId);

    bool IsOnline(Guid meetingId, Guid userId);

    IReadOnlyCollection<Guid> OnlineUsers(Guid meetingId);

    IReadOnlyCollection<string> RevokeUser(Guid meetingId, Guid userId);
}

public interface IMeetingNotifier
{
    Task StateChangedAsync(Guid meetingId, int version, CancellationToken cancellationToken);

    Task PresenceChangedAsync(Guid meetingId, CancellationToken cancellationToken);

    Task AccessRevokedAsync(Guid meetingId, Guid userId, CancellationToken cancellationToken);

    Task TimerExpiredAsync(
        Guid meetingId,
        Guid runId,
        MeetingPhase phase,
        CancellationToken cancellationToken);
}
