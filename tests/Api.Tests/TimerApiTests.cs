using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace Api.Tests;

public sealed class TimerApiTests
{
    [Fact]
    public async Task TimerCommandsAreSharedIdempotentAndNeverPersisted()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "timer-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "timer-member", "Участник");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Таймер M7");
        await JoinAsync(facilitator, member, meeting.Id);

        var draftStart = await StartAsync(facilitator, meeting.Id, 60, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, draftStart.StatusCode);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var invalidDuration = await StartAsync(facilitator, meeting.Id, 0, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, invalidDuration.StatusCode);
        var forbidden = await StartAsync(member, meeting.Id, 60, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var startOperation = Guid.NewGuid();
        var started = await StartAsync(facilitator, meeting.Id, 60, startOperation);
        started.EnsureSuccessStatusCode();
        var timer = (await started.Content.ReadFromJsonAsync<MeetingTimerResponse>())!;
        Assert.Equal("Collect", timer.Phase);
        Assert.Equal(factory.Time.GetUtcNow().AddSeconds(60), timer.EndsAtUtc);
        var replay = await StartAsync(facilitator, meeting.Id, 60, startOperation);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(timer, await replay.Content.ReadFromJsonAsync<MeetingTimerResponse>());
        var conflicting = await StartAsync(facilitator, meeting.Id, 61, startOperation);
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        var alreadyRunning = await StartAsync(facilitator, meeting.Id, 60, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, alreadyRunning.StatusCode);
        var memberState = await StateAsync(member, meeting.Id);
        Assert.Equal(timer.RunId, memberState.Timer!.RunId);

        var addOperation = Guid.NewGuid();
        var added = await AddAsync(facilitator, meeting.Id, timer.RunId, 30, addOperation);
        added.EnsureSuccessStatusCode();
        var extended = (await added.Content.ReadFromJsonAsync<MeetingTimerResponse>())!;
        Assert.Equal(timer.EndsAtUtc.AddSeconds(30), extended.EndsAtUtc);
        var addReplay = await AddAsync(facilitator, meeting.Id, timer.RunId, 30, addOperation);
        Assert.Equal(extended, await addReplay.Content.ReadFromJsonAsync<MeetingTimerResponse>());

        await using (var connection = factory.Database.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM command_deduplication WHERE operation_type LIKE 'Timer%';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
            await using var tables = connection.CreateCommand();
            tables.CommandText = factory.Database.IsPostgreSql
                ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND lower(table_name) LIKE '%timer%';"
                : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND lower(name) LIKE '%timer%';";
            Assert.Equal(0L, (long)(await tables.ExecuteScalarAsync())!);
        }

        var stopOperation = Guid.NewGuid();
        var stopped = await StopAsync(facilitator, meeting.Id, timer.RunId, stopOperation);
        Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
        var stopReplay = await StopAsync(facilitator, meeting.Id, timer.RunId, stopOperation);
        Assert.Equal(HttpStatusCode.NoContent, stopReplay.StatusCode);
        Assert.Null((await StateAsync(member, meeting.Id)).Timer);
        var lateAdd = await AddAsync(facilitator, meeting.Id, timer.RunId, 30, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, lateAdd.StatusCode);
    }

    [Fact]
    public async Task ExpirationIsEmittedOnceAndPhaseAdvanceCancelsWithoutSignal()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "expiry-facilitator", "Ведущий");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Истечение M7");
        var meetingId = Guid.Parse(meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var connection = await SignalRTestHelpers.ConnectAsync(factory, facilitator, "expiry-facilitator", meetingId);
        var notifications = new List<string>();
        var firstNotification = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<string, string, string>("TimerExpired", (id, runId, phase) =>
        {
            if (id == meeting.Id)
            {
                lock (notifications)
                {
                    notifications.Add(runId);
                }
                firstNotification.TrySetResult(runId);
            }
        });
        var started = await StartAsync(facilitator, meeting.Id, 1, Guid.NewGuid());
        started.EnsureSuccessStatusCode();
        var timer = (await started.Content.ReadFromJsonAsync<MeetingTimerResponse>())!;
        factory.Time.Advance(TimeSpan.FromSeconds(2));
        var notifiedRunId = await firstNotification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(timer.RunId, notifiedRunId);
        Assert.Null((await StateAsync(facilitator, meeting.Id)).Timer);
        var lateAdd = await AddAsync(facilitator, meeting.Id, timer.RunId, 1, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, lateAdd.StatusCode);

        var restarted = await StartAsync(facilitator, meeting.Id, 5, Guid.NewGuid());
        restarted.EnsureSuccessStatusCode();
        var newTimer = (await restarted.Content.ReadFromJsonAsync<MeetingTimerResponse>())!;
        Assert.NotEqual(timer.RunId, newTimer.RunId);
        var beforeAdvance = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", beforeAdvance.Version);
        Assert.Null((await StateAsync(facilitator, meeting.Id)).Timer);
        factory.Time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(700);
        lock (notifications)
        {
            Assert.Single(notifications);
        }
        await connection.StopAsync();
    }

    [Fact]
    public async Task RestartLosesTimerAndCommandHistory()
    {
        using var database = new ApiTestDatabase();
        TRetroWebApplicationFactory? firstFactory = null;
        TRetroWebApplicationFactory? secondFactory = null;
        try
        {
            firstFactory = new TRetroWebApplicationFactory(database: database);
            await ApiTestHelpers.MigrateAsync(firstFactory);
            using var firstClient = firstFactory.CreateClient();
            await ApiTestHelpers.LoginAsync(firstClient, "restart-timer", "Ведущий");
            var meeting = await ApiTestHelpers.CreateMeetingAsync(firstClient, "Рестарт M7");
            await AdvanceAsync(firstClient, meeting.Id, "Draft", 1);
            var operationId = Guid.NewGuid();
            var started = await StartAsync(firstClient, meeting.Id, 60, operationId);
            started.EnsureSuccessStatusCode();
            var timer = (await started.Content.ReadFromJsonAsync<MeetingTimerResponse>())!;
            firstFactory.Dispose();
            firstFactory = null;

            secondFactory = new TRetroWebApplicationFactory(database: database);
            using var secondClient = secondFactory.CreateClient();
            await ApiTestHelpers.LoginAsync(secondClient, "restart-timer", "Ведущий");
            Assert.Null((await StateAsync(secondClient, meeting.Id)).Timer);
            var restarted = await StartAsync(secondClient, meeting.Id, 60, operationId);
            restarted.EnsureSuccessStatusCode();
            var newTimer = (await restarted.Content.ReadFromJsonAsync<MeetingTimerResponse>())!;
            Assert.NotEqual(timer.RunId, newTimer.RunId);
        }
        finally
        {
            firstFactory?.Dispose();
            secondFactory?.Dispose();
        }
    }

    private static Task<HttpResponseMessage> StartAsync(
        HttpClient client,
        string meetingId,
        int seconds,
        Guid operationId) =>
        client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/timer/start",
            new StartTimerRequest { Seconds = seconds, OperationId = operationId });

    private static Task<HttpResponseMessage> AddAsync(
        HttpClient client,
        string meetingId,
        string runId,
        int seconds,
        Guid operationId) =>
        client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/timer/add",
            new AddTimerRequest { RunId = Guid.Parse(runId), Seconds = seconds, OperationId = operationId });

    private static Task<HttpResponseMessage> StopAsync(
        HttpClient client,
        string meetingId,
        string runId,
        Guid operationId) =>
        client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/timer/stop",
            new StopTimerRequest { RunId = Guid.Parse(runId), OperationId = operationId });

    private static async Task JoinAsync(HttpClient facilitator, HttpClient member, string meetingId)
    {
        var invite = await facilitator.GetFromJsonAsync<InviteResponse>($"/api/meetings/{meetingId}/invite");
        var response = await member.PostAsJsonAsync(
            "/api/join",
            new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url)));
        response.EnsureSuccessStatusCode();
    }

    private static async Task AdvanceAsync(HttpClient client, string meetingId, string phase, int version)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/advance",
            new AdvanceMeetingRequest { FromPhase = phase, ExpectedVersion = version, OperationId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<MeetingStateResponse> StateAsync(HttpClient client, string meetingId) =>
        (await client.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meetingId}/state"))!;
}
