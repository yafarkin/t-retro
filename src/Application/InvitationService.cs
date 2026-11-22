using Domain;

namespace Application;

public sealed class InvitationService(
    IMeetingRepository meetings,
    MeetingAccessService access,
    IInvitationTokenService tokens)
{
    public async Task<(string Token, int Version)> GetAsync(
        Guid userId,
        Guid meetingId,
        CancellationToken cancellationToken)
    {
        var snapshot = await access.GetAsync(userId, meetingId, true, cancellationToken);
        return (tokens.Create(meetingId, snapshot.Meeting.InvitationVersion), snapshot.Meeting.InvitationVersion);
    }

    public async Task<(string Token, int Version)> RotateAsync(
        Guid userId,
        Guid meetingId,
        CancellationToken cancellationToken)
    {
        await access.GetAsync(userId, meetingId, true, cancellationToken);
        var version = await meetings.RotateInvitationAsync(meetingId, userId, cancellationToken);
        if (version == 0)
        {
            throw new ConflictException("VersionConflict", "Приглашение изменилось. Обновите состояние и повторите.");
        }

        return (tokens.Create(meetingId, version), version);
    }

    public async Task<Guid> JoinAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken)
    {
        if (!tokens.TryRead(token, out var invitation) || invitation is null)
        {
            throw new DomainValidationException("InvalidInvitation", "Приглашение имеет неверный формат.");
        }

        var result = await meetings.JoinAsync(
            invitation.MeetingId,
            userId,
            invitation.Version,
            cancellationToken);
        return result switch
        {
            JoinMeetingResult.Joined or JoinMeetingResult.AlreadyMember => invitation.MeetingId,
            JoinMeetingResult.NotFound => throw new NotFoundException("Встреча не найдена."),
            JoinMeetingResult.Closed => throw new ConflictException("MeetingClosed", "Встреча уже завершена."),
            JoinMeetingResult.InvitationMismatch => throw new ConflictException("InvitationRevoked", "Ссылка приглашения больше не действует."),
            JoinMeetingResult.Excluded => throw new ForbiddenException("AccessRevoked", "Доступ к встрече закрыт."),
            _ => throw new InvalidOperationException("Unknown join result."),
        };
    }
}
