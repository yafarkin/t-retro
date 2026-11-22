using System.Net.Http.Json;
using Api.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Api.Tests;

public sealed class SignalRApiTests
{
    [Fact]
    public async Task SubscriberReceivesStateChangedAndOutsiderCannotSubscribe()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitatorClient = factory.CreateClient();
        using var outsiderClient = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitatorClient, "facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(outsiderClient, "outsider", "Посторонний");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitatorClient);
        var meetingId = Guid.Parse(meeting.Id);
        var connection = await SignalRTestHelpers.ConnectAsync(factory, facilitatorClient, "facilitator", meetingId);
        var changed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<Guid, int>("StateChanged", (id, version) =>
        {
            if (id == meetingId)
            {
                changed.TrySetResult(version);
            }
        });

        var update = ApiTestHelpers.DefaultMeeting("Обновлённое название");
        var response = await facilitatorClient.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/settings",
            new UpdateMeetingSettingsRequest
            {
                ExpectedVersion = 1,
                Name = update.Name,
                Columns = update.Columns,
                VoteSettings = update.VoteSettings,
            });
        response.EnsureSuccessStatusCode();

        var version = await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, version);

        var outsider = await outsiderClient.PostAsJsonAsync(
            "/api/dev/login",
            new DevLoginRequest("outsider", "Посторонний"));
        outsider.EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<HubException>(async () =>
        {
            await using var outsiderConnection = await SignalRTestHelpers.ConnectAsync(factory, outsiderClient, "outsider", meetingId);
        });
        await connection.StopAsync();
    }
}
