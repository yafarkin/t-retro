using Application;
using Domain;
using Infrastructure.Timers;

namespace Infrastructure.Tests;

public sealed class InMemoryMeetingTimerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CommandsAreIdempotentAndStopDoesNotExpire()
    {
        var timer = new InMemoryMeetingTimer();
        var meetingId = Guid.NewGuid();
        var startOperation = Guid.NewGuid();
        var started = timer.Start(meetingId, MeetingPhase.Collect, 60, startOperation, "start", Guid.NewGuid(), Now);

        Assert.Equal(TimerCommandStatus.Success, started.Status);
        Assert.Equal(Now.AddSeconds(60), started.Timer!.EndsAtUtc);
        var replay = timer.Start(meetingId, MeetingPhase.Collect, 60, startOperation, "start", Guid.NewGuid(), Now);
        Assert.True(replay.IsReplay);
        Assert.Equal(started.Timer, replay.Timer);
        Assert.Equal(
            TimerCommandStatus.OperationConflict,
            timer.Start(meetingId, MeetingPhase.Collect, 61, startOperation, "other", Guid.NewGuid(), Now).Status);
        Assert.Equal(
            TimerCommandStatus.AlreadyRunning,
            timer.Start(meetingId, MeetingPhase.Collect, 60, Guid.NewGuid(), "new", Guid.NewGuid(), Now).Status);

        var addOperation = Guid.NewGuid();
        var added = timer.Add(meetingId, started.Timer.RunId, 30, addOperation, "add", Now);
        Assert.Equal(Now.AddSeconds(90), added.Timer!.EndsAtUtc);
        var addedReplay = timer.Add(meetingId, started.Timer.RunId, 30, addOperation, "add", Now);
        Assert.Equal(added.Timer, addedReplay.Timer);
        Assert.Equal(
            TimerCommandStatus.NotRunning,
            timer.Add(meetingId, Guid.NewGuid(), 30, Guid.NewGuid(), "wrong", Now).Status);

        var stopOperation = Guid.NewGuid();
        var stopped = timer.Stop(meetingId, started.Timer.RunId, stopOperation, "stop", Now);
        Assert.Equal(TimerCommandStatus.Success, stopped.Status);
        Assert.Null(timer.Get(meetingId, Now));
        Assert.True(timer.Stop(meetingId, started.Timer.RunId, stopOperation, "stop", Now).IsReplay);
        Assert.Empty(timer.DrainExpired(Now.AddMinutes(5)));
    }

    [Fact]
    public void ExpirationIsEmittedOnceAndLateAddIsRejected()
    {
        var timer = new InMemoryMeetingTimer();
        var meetingId = Guid.NewGuid();
        var started = timer.Start(meetingId, MeetingPhase.Vote, 1, Guid.NewGuid(), "start", Guid.NewGuid(), Now);

        Assert.NotNull(timer.Get(meetingId, Now.AddMilliseconds(500)));
        Assert.Equal(
            TimerCommandStatus.Expired,
            timer.Add(meetingId, started.Timer!.RunId, 1, Guid.NewGuid(), "late", Now.AddSeconds(2)).Status);
        Assert.Null(timer.Get(meetingId, Now.AddSeconds(2)));
        var notification = Assert.Single(timer.DrainExpired(Now.AddSeconds(2)));
        Assert.Equal(meetingId, notification.MeetingId);
        Assert.Equal(started.Timer.RunId, notification.RunId);
        Assert.Equal(MeetingPhase.Vote, notification.Phase);
        Assert.Empty(timer.DrainExpired(Now.AddSeconds(3)));

        var restarted = timer.Start(meetingId, MeetingPhase.Vote, 5, Guid.NewGuid(), "new", Guid.NewGuid(), Now.AddSeconds(3));
        Assert.Equal(TimerCommandStatus.Success, restarted.Status);
        Assert.NotEqual(started.Timer.RunId, restarted.Timer!.RunId);
    }

    [Fact]
    public void CancelHasNoSignalAndNewInstanceLosesStateAndHistory()
    {
        var meetingId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var timer = new InMemoryMeetingTimer();
        timer.Start(meetingId, MeetingPhase.Group, 60, operationId, "start", Guid.NewGuid(), Now);

        timer.Cancel(meetingId);
        Assert.Null(timer.Get(meetingId, Now));
        Assert.Empty(timer.DrainExpired(Now.AddMinutes(2)));

        var afterRestart = new InMemoryMeetingTimer();
        var started = afterRestart.Start(meetingId, MeetingPhase.Group, 60, operationId, "start", Guid.NewGuid(), Now);
        Assert.Equal(TimerCommandStatus.Success, started.Status);
        Assert.False(started.IsReplay);
    }

    [Fact]
    public async Task ParallelStartsCreateOnlyOneRun()
    {
        var timer = new InMemoryMeetingTimer();
        var meetingId = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            timer.Start(meetingId, MeetingPhase.Discuss, 60, Guid.NewGuid(), Guid.NewGuid().ToString(), Guid.NewGuid(), Now))));

        Assert.Equal(1, results.Count(result => result.Status == TimerCommandStatus.Success));
        Assert.Equal(19, results.Count(result => result.Status == TimerCommandStatus.AlreadyRunning));
    }
}
