using Application;
using Microsoft.AspNetCore.DataProtection;

namespace Infrastructure.Security;

public sealed class DataProtectionInvitationTokenService : IInvitationTokenService
{
    private readonly IDataProtector protector;

    public DataProtectionInvitationTokenService(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("T-Retro.Invites.V1");
    }

    public string Create(Guid meetingId, int version) =>
        protector.Protect($"{meetingId:N}:{version}");

    public bool TryRead(string token, out InvitationToken? invitation)
    {
        invitation = null;
        try
        {
            var payload = protector.Unprotect(token).Split(':');
            if (payload.Length != 2
                || !Guid.TryParseExact(payload[0], "N", out var meetingId)
                || !int.TryParse(payload[1], out var version)
                || version < 1)
            {
                return false;
            }

            invitation = new InvitationToken(meetingId, version);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
