namespace Domain;

public static class MeetingPhaseRules
{
    public static MeetingPhase Next(MeetingPhase phase) => phase switch
    {
        MeetingPhase.Draft => MeetingPhase.Collect,
        MeetingPhase.Collect => MeetingPhase.Group,
        MeetingPhase.Group => MeetingPhase.Vote,
        MeetingPhase.Vote => MeetingPhase.Discuss,
        MeetingPhase.Discuss => MeetingPhase.Closed,
        MeetingPhase.Closed => throw new DomainValidationException(
            "MeetingClosed",
            "Встреча уже завершена."),
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };
}
