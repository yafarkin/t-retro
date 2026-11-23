using Domain;

namespace Application;

public sealed class TimerService(
    MeetingAccessService access,
    IMeetingTimer timer,
    IMeetingNotifier notifier,
    TimeProvider timeProvider)
{
    public async Task<MeetingTimer> StartAsync(
        Guid userId,
        Guid meetingId,
        int seconds,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateSeconds(seconds);
        ValidateOperationId(operationId);
        var snapshot = await access.GetAsync(userId, meetingId, true, cancellationToken);
        EnsureTimerPhase(snapshot.Meeting.Phase);
        var result = timer.Start(
            meetingId,
            snapshot.Meeting.Phase,
            seconds,
            operationId,
            CommandFingerprint.Create("TimerStart", seconds),
            Guid.NewGuid(),
            timeProvider.GetUtcNow());
        var completed = await CompleteAsync(
            userId,
            meetingId,
            snapshot.Meeting.Version,
            result,
            cancellationToken);
        return completed.Timer
            ?? throw new InvalidOperationException("A running timer was expected.");
    }

    public async Task<MeetingTimer> AddAsync(
        Guid userId,
        Guid meetingId,
        Guid runId,
        int seconds,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateSeconds(seconds);
        ValidateOperationId(operationId);
        ValidateRunId(runId);
        var snapshot = await access.GetAsync(userId, meetingId, true, cancellationToken);
        EnsureTimerPhase(snapshot.Meeting.Phase);
        var result = timer.Add(
            meetingId,
            runId,
            seconds,
            operationId,
            CommandFingerprint.Create("TimerAdd", runId, seconds),
            timeProvider.GetUtcNow());
        var completed = await CompleteAsync(
            userId,
            meetingId,
            snapshot.Meeting.Version,
            result,
            cancellationToken);
        return completed.Timer
            ?? throw new InvalidOperationException("A running timer was expected.");
    }

    public async Task StopAsync(
        Guid userId,
        Guid meetingId,
        Guid runId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        ValidateRunId(runId);
        var snapshot = await access.GetAsync(userId, meetingId, true, cancellationToken);
        EnsureTimerPhase(snapshot.Meeting.Phase);
        var result = timer.Stop(
            meetingId,
            runId,
            operationId,
            CommandFingerprint.Create("TimerStop", runId),
            timeProvider.GetUtcNow());
        await CompleteAsync(
            userId,
            meetingId,
            snapshot.Meeting.Version,
            result,
            cancellationToken);
    }

    private async Task<TimerCommandResult> CompleteAsync(
        Guid userId,
        Guid meetingId,
        int meetingVersion,
        TimerCommandResult result,
        CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case TimerCommandStatus.AlreadyRunning:
                throw new ConflictException("TimerAlreadyRunning", "Таймер уже запущен.");
            case TimerCommandStatus.NotRunning:
                throw new ConflictException("TimerNotRunning", "Этот запуск таймера уже не активен.");
            case TimerCommandStatus.Expired:
                throw new ConflictException("TimerExpired", "Таймер уже истёк. Запустите новый.");
            case TimerCommandStatus.OperationConflict:
                throw new ConflictException("OperationConflict", "Идентификатор операции уже использован для другой команды.");
        }

        if (!result.IsReplay)
        {
            var current = await access.GetAsync(userId, meetingId, true, cancellationToken);
            if (result.Timer is not null && current.Meeting.Phase != result.Timer.Phase)
            {
                timer.Cancel(meetingId, result.Timer.RunId);
                throw new ConflictException("InvalidPhase", "Фаза встречи изменилась. Повторите команду таймера.");
            }

            await notifier.StateChangedAsync(meetingId, meetingVersion, cancellationToken);
        }

        return result;
    }

    private static void EnsureTimerPhase(MeetingPhase phase)
    {
        if (phase is not (MeetingPhase.Collect or MeetingPhase.Group or MeetingPhase.Vote or MeetingPhase.Discuss))
        {
            throw new ConflictException("InvalidPhase", "Таймер недоступен в текущей фазе.");
        }
    }

    private static void ValidateSeconds(int seconds)
    {
        if (seconds <= 0)
        {
            throw new DomainValidationException("InvalidTimerDuration", "Длительность таймера должна быть положительной.");
        }
    }

    private static void ValidateRunId(Guid runId)
    {
        if (runId == Guid.Empty)
        {
            throw new DomainValidationException("RunIdRequired", "Укажите идентификатор запуска таймера.");
        }
    }

    private static void ValidateOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new DomainValidationException("OperationIdRequired", "Укажите идентификатор операции.");
        }
    }
}
