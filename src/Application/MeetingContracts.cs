using Domain;

namespace Application;

public sealed record ColumnInput(string? Name, string? Color);

public sealed record PrivacyInput(bool AnonymousCards, bool PrivateCollection, bool AnonymousVotes);

public sealed record MeetingParticipant(
    Guid UserId,
    string Subject,
    string DisplayName,
    MemberStatus Status,
    bool IsOnline = false);

public sealed record MeetingStateData(
    Meeting Meeting,
    IReadOnlyList<MeetingColumn> Columns,
    IReadOnlyList<MeetingParticipant> Participants,
    IReadOnlyList<CardData> Cards,
    IReadOnlySet<Guid> ReadyUserIds,
    IReadOnlyList<MeetingGroup> Groups);

public sealed record MeetingIntegrations(bool Ai);

public sealed record MeetingSnapshot(
    Meeting Meeting,
    IReadOnlyList<MeetingColumn> Columns,
    IReadOnlyList<MeetingParticipant> Participants,
    IReadOnlyList<MeetingCard> Cards,
    IReadOnlyList<MeetingGroup> Groups,
    MeetingReadiness? Readiness,
    MeetingIntegrations Integrations,
    Guid CurrentUserId)
{
    public bool CanManage => Meeting.FacilitatorId == CurrentUserId;
}

public sealed record MeetingSummary(
    Guid Id,
    string Name,
    MeetingPhase Phase,
    DateTimeOffset CreatedAtUtc);

public sealed record TemplateDetails(
    MeetingTemplate Template,
    IReadOnlyList<TemplateColumn> Columns);

public enum JoinMeetingResult
{
    Joined,
    AlreadyMember,
    NotFound,
    Closed,
    InvitationMismatch,
    Excluded,
}
