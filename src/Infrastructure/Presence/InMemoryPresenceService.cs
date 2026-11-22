using Application;

namespace Infrastructure.Presence;

public sealed class InMemoryPresenceService : IPresenceService
{
    private readonly object sync = new();
    private readonly Dictionary<string, PresenceConnection> connections = new();

    public PresenceConnection? Connect(string connectionId, Guid meetingId, Guid userId)
    {
        lock (sync)
        {
            connections.Remove(connectionId, out var previous);
            connections[connectionId] = new PresenceConnection(connectionId, meetingId, userId);
            return previous;
        }
    }

    public PresenceConnection? Disconnect(string connectionId)
    {
        lock (sync)
        {
            connections.Remove(connectionId, out var previous);
            return previous;
        }
    }

    public bool IsOnline(Guid meetingId, Guid userId)
    {
        lock (sync)
        {
            return connections.Values.Any(connection => connection.MeetingId == meetingId && connection.UserId == userId);
        }
    }

    public IReadOnlyCollection<Guid> OnlineUsers(Guid meetingId)
    {
        lock (sync)
        {
            return connections.Values
                .Where(connection => connection.MeetingId == meetingId)
                .Select(connection => connection.UserId)
                .Distinct()
                .ToArray();
        }
    }

    public IReadOnlyCollection<string> RevokeUser(Guid meetingId, Guid userId)
    {
        lock (sync)
        {
            var revoked = connections.Values
                .Where(connection => connection.MeetingId == meetingId && connection.UserId == userId)
                .ToArray();
            foreach (var connection in revoked)
            {
                connections.Remove(connection.ConnectionId);
            }

            return revoked.Select(connection => connection.ConnectionId).ToArray();
        }
    }
}
