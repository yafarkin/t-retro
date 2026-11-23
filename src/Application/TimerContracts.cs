using Domain;

namespace Application;

public sealed record MeetingTimer(
    Guid RunId,
    MeetingPhase Phase,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset ServerNowUtc);

public enum TimerCommandStatus
{
    Success,
    AlreadyRunning,
    NotRunning,
    Expired,
    OperationConflict,
}

public sealed record TimerCommandResult(
    TimerCommandStatus Status,
    MeetingTimer? Timer = null,
    bool IsReplay = false);

public sealed record TimerExpiredNotification(
    Guid MeetingId,
    Guid RunId,
    MeetingPhase Phase);

public interface IMeetingTimer
{
    MeetingTimer? Get(Guid meetingId, DateTimeOffset nowUtc);

    TimerCommandResult Start(
        Guid meetingId,
        MeetingPhase phase,
        int seconds,
        Guid operationId,
        string fingerprint,
        Guid runId,
        DateTimeOffset nowUtc);

    TimerCommandResult Add(
        Guid meetingId,
        Guid runId,
        int seconds,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc);

    TimerCommandResult Stop(
        Guid meetingId,
        Guid runId,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc);

    void Cancel(Guid meetingId, Guid? runId = null);

    IReadOnlyList<TimerExpiredNotification> DrainExpired(DateTimeOffset nowUtc);
}
