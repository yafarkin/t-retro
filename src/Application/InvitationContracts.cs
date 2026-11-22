namespace Application;

public sealed record InvitationToken(Guid MeetingId, int Version);

public interface IInvitationTokenService
{
    string Create(Guid meetingId, int version);

    bool TryRead(string token, out InvitationToken? invitation);
}
