using System.Net.Http.Json;
using Api.Contracts;

namespace Api.Tests;

public sealed class FacilitatorRestartApiTests
{
    [Fact]
    public async Task RestartKeepsOriginalDeadlineAndFirstReturningMemberGetsVacantRole()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "TestData", Guid.NewGuid().ToString());
        var databasePath = Path.Combine(directory, "test.db");
        TRetroWebApplicationFactory? firstFactory = null;
        TRetroWebApplicationFactory? secondFactory = null;
        try
        {
            firstFactory = new TRetroWebApplicationFactory(databasePath: databasePath);
            await ApiTestHelpers.MigrateAsync(firstFactory);
            using var firstClient = firstFactory.CreateClient();
            using var secondClient = firstFactory.CreateClient();
            var first = await ApiTestHelpers.LoginAsync(firstClient, "first", "Первый");
            var second = await ApiTestHelpers.LoginAsync(secondClient, "second", "Второй");
            var meeting = await ApiTestHelpers.CreateMeetingAsync(firstClient);
            var meetingId = Guid.Parse(meeting.Id);
            var invite = await firstClient.GetFromJsonAsync<InviteResponse>($"/api/meetings/{meeting.Id}/invite");
            await secondClient.PostAsJsonAsync("/api/join", new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url)));
            var connection = await SignalRTestHelpers.ConnectAsync(firstFactory, firstClient, "first", meetingId);
            await connection.StopAsync();
            await SignalRTestHelpers.WaitForStateAsync(
                firstClient,
                meetingId,
                state => !state.Participants.Single(participant => participant.Id == first.Id).IsOnline);
            var shutdownTime = firstFactory.Time.GetUtcNow();
            firstFactory.Dispose();
            firstFactory = null;

            secondFactory = new TRetroWebApplicationFactory(databasePath: databasePath);
            secondFactory.Time.SetUtcNow(shutdownTime.AddSeconds(61));
            using var firstAfterRestart = secondFactory.CreateClient();
            using var secondAfterRestart = secondFactory.CreateClient();
            await ApiTestHelpers.LoginAsync(firstAfterRestart, "first", "Первый");
            await ApiTestHelpers.LoginAsync(secondAfterRestart, "second", "Второй");
            await SignalRTestHelpers.WaitForStateAsync(
                firstAfterRestart,
                meetingId,
                state => state.FacilitatorId is null);

            var secondReturning = await SignalRTestHelpers.ConnectAsync(secondFactory, secondAfterRestart, "second", meetingId);
            var firstReturning = await SignalRTestHelpers.ConnectAsync(secondFactory, firstAfterRestart, "first", meetingId);
            var final = await firstAfterRestart.GetFromJsonAsync<MeetingStateResponse>(
                $"/api/meetings/{meeting.Id}/state");
            Assert.Equal(second.Id, final!.FacilitatorId);
            await firstReturning.StopAsync();
            await secondReturning.StopAsync();
        }
        finally
        {
            firstFactory?.Dispose();
            secondFactory?.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
