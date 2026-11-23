using Application;
using Domain;

namespace Infrastructure.Timers;

public sealed class InMemoryMeetingTimer : IMeetingTimer
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, TimerState> states = new();
    private readonly List<TimerExpiredNotification> expired = [];

    public MeetingTimer? Get(Guid meetingId, DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            if (!states.TryGetValue(meetingId, out var state) || state.Current is null)
            {
                return null;
            }

            if (state.Current.Timer.EndsAtUtc <= nowUtc)
            {
                ExpireLocked(meetingId, state);
                return null;
            }

            return state.Current.Timer with { ServerNowUtc = nowUtc };
        }
    }

    public TimerCommandResult Start(
        Guid meetingId,
        MeetingPhase phase,
        int seconds,
        Guid operationId,
        string fingerprint,
        Guid runId,
        DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            var state = GetOrCreateState(meetingId);
            var replay = FindReplayLocked(state, operationId, "Start", fingerprint);
            if (replay is not null)
            {
                return replay;
            }

            if (state.Current?.Timer.EndsAtUtc <= nowUtc)
            {
                ExpireLocked(meetingId, state);
            }

            if (state.Current is not null)
            {
                return new TimerCommandResult(TimerCommandStatus.AlreadyRunning);
            }

            var timer = new MeetingTimer(runId, phase, nowUtc.AddSeconds(seconds), nowUtc);
            state.Current = new TimerRun(timer);
            return SaveReplayLocked(
                state,
                operationId,
                "Start",
                fingerprint,
                new TimerCommandResult(TimerCommandStatus.Success, timer));
        }
    }

    public TimerCommandResult Add(
        Guid meetingId,
        Guid runId,
        int seconds,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            var state = GetOrCreateState(meetingId);
            var replay = FindReplayLocked(state, operationId, "Add", fingerprint);
            if (replay is not null)
            {
                return replay;
            }

            if (state.Current is null || state.Current.Timer.RunId != runId)
            {
                return new TimerCommandResult(TimerCommandStatus.NotRunning);
            }

            if (state.Current.Timer.EndsAtUtc <= nowUtc)
            {
                ExpireLocked(meetingId, state);
                return new TimerCommandResult(TimerCommandStatus.Expired);
            }

            var timer = state.Current.Timer with
            {
                EndsAtUtc = state.Current.Timer.EndsAtUtc.AddSeconds(seconds),
                ServerNowUtc = nowUtc,
            };
            state.Current = new TimerRun(timer);
            return SaveReplayLocked(
                state,
                operationId,
                "Add",
                fingerprint,
                new TimerCommandResult(TimerCommandStatus.Success, timer));
        }
    }

    public TimerCommandResult Stop(
        Guid meetingId,
        Guid runId,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            var state = GetOrCreateState(meetingId);
            var replay = FindReplayLocked(state, operationId, "Stop", fingerprint);
            if (replay is not null)
            {
                return replay;
            }

            if (state.Current is null || state.Current.Timer.RunId != runId)
            {
                return new TimerCommandResult(TimerCommandStatus.NotRunning);
            }

            if (state.Current.Timer.EndsAtUtc <= nowUtc)
            {
                ExpireLocked(meetingId, state);
                return new TimerCommandResult(TimerCommandStatus.Expired);
            }

            state.Current = null;
            return SaveReplayLocked(
                state,
                operationId,
                "Stop",
                fingerprint,
                new TimerCommandResult(TimerCommandStatus.Success));
        }
    }

    public void Cancel(Guid meetingId, Guid? runId = null)
    {
        lock (sync)
        {
            if (!states.TryGetValue(meetingId, out var state) || state.Current is null)
            {
                return;
            }

            if (runId is null || state.Current.Timer.RunId == runId)
            {
                state.Current = null;
            }
        }
    }

    public IReadOnlyList<TimerExpiredNotification> DrainExpired(DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            foreach (var (meetingId, state) in states)
            {
                if (state.Current?.Timer.EndsAtUtc <= nowUtc)
                {
                    ExpireLocked(meetingId, state);
                }
            }

            var result = expired.ToArray();
            expired.Clear();
            return result;
        }
    }

    private TimerState GetOrCreateState(Guid meetingId)
    {
        if (!states.TryGetValue(meetingId, out var state))
        {
            state = new TimerState();
            states.Add(meetingId, state);
        }

        return state;
    }

    private void ExpireLocked(Guid meetingId, TimerState state)
    {
        if (state.Current is null)
        {
            return;
        }

        expired.Add(new TimerExpiredNotification(
            meetingId,
            state.Current.Timer.RunId,
            state.Current.Timer.Phase));
        state.Current = null;
    }

    private static TimerCommandResult? FindReplayLocked(
        TimerState state,
        Guid operationId,
        string operationType,
        string fingerprint)
    {
        if (!state.Commands.TryGetValue(operationId, out var command))
        {
            return null;
        }

        return command.OperationType == operationType && command.Fingerprint == fingerprint
            ? command.Result with { IsReplay = true }
            : new TimerCommandResult(TimerCommandStatus.OperationConflict);
    }

    private static TimerCommandResult SaveReplayLocked(
        TimerState state,
        Guid operationId,
        string operationType,
        string fingerprint,
        TimerCommandResult result)
    {
        state.Commands.Add(operationId, new TimerCommand(operationType, fingerprint, result));
        return result;
    }

    private sealed class TimerState
    {
        public TimerRun? Current { get; set; }

        public Dictionary<Guid, TimerCommand> Commands { get; } = new();
    }

    private sealed record TimerRun(MeetingTimer Timer);

    private sealed record TimerCommand(
        string OperationType,
        string Fingerprint,
        TimerCommandResult Result);
}
