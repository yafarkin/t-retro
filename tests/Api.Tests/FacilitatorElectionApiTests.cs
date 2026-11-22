using System.Net.Http.Json;
using Api.Contracts;
using Application;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

public sealed class FacilitatorElectionApiTests
{
    [Fact]
    public async Task TabsReconnectDeadlineAndVacancyFollowAssignmentGeneration()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var first = await ApiTestHelpers.LoginAsync(firstClient, "first", "Первый");
        var second = await ApiTestHelpers.LoginAsync(secondClient, "second", "Второй");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(firstClient);
        var meetingId = Guid.Parse(meeting.Id);
        var invite = await firstClient.GetFromJsonAsync<InviteResponse>($"/api/meetings/{meeting.Id}/invite");
        await secondClient.PostAsJsonAsync("/api/join", new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url)));

        var firstTab = await SignalRTestHelpers.ConnectAsync(factory, firstClient, "first", meetingId);
        var secondTab = await SignalRTestHelpers.ConnectAsync(factory, firstClient, "first", meetingId);
        var secondConnection = await SignalRTestHelpers.ConnectAsync(factory, secondClient, "second", meetingId);
        var initial = await firstClient.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.True(initial!.Participants.Single(participant => participant.Id == first.Id).IsOnline);

        await firstTab.StopAsync();
        var afterFirstTab = await SignalRTestHelpers.WaitForStateAsync(
            firstClient,
            meetingId,
            state => state.Participants.Single(participant => participant.Id == first.Id).IsOnline);
        Assert.Equal(first.Id, afterFirstTab.FacilitatorId);

        await secondTab.StopAsync();
        await SignalRTestHelpers.WaitForStateAsync(
            firstClient,
            meetingId,
            state => !state.Participants.Single(participant => participant.Id == first.Id).IsOnline);
        factory.Time.Advance(TimeSpan.FromSeconds(59));
        var reconnected = await SignalRTestHelpers.ConnectAsync(factory, firstClient, "first", meetingId);
        factory.Time.Advance(TimeSpan.FromSeconds(3));
        var beforeDeadline = await firstClient.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(first.Id, beforeDeadline!.FacilitatorId);

        await reconnected.StopAsync();
        await SignalRTestHelpers.WaitForStateAsync(
            firstClient,
            meetingId,
            state => !state.Participants.Single(participant => participant.Id == first.Id).IsOnline);
        factory.Time.Advance(TimeSpan.FromSeconds(61));
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Same(factory.Time, scope.ServiceProvider.GetRequiredService<TimeProvider>());
            var repository = scope.ServiceProvider.GetRequiredService<IMeetingRepository>();
            var due = await repository.GetDueElectionsAsync(factory.Time.GetUtcNow(), CancellationToken.None);
            Assert.NotEmpty(due);
        }

        var elected = await SignalRTestHelpers.WaitForStateAsync(
            secondClient,
            meetingId,
            state => state.FacilitatorId == second.Id);
        Assert.True(elected.CanManage);

        await secondConnection.StopAsync();
        await SignalRTestHelpers.WaitForStateAsync(
            firstClient,
            meetingId,
            state => !state.Participants.Single(participant => participant.Id == second.Id).IsOnline);
        factory.Time.Advance(TimeSpan.FromSeconds(61));
        var vacant = await SignalRTestHelpers.WaitForStateAsync(
            firstClient,
            meetingId,
            state => state.FacilitatorId is null);
        Assert.False(vacant.CanManage);

        var firstReturning = await SignalRTestHelpers.ConnectAsync(factory, firstClient, "first", meetingId);
        var secondReturning = await SignalRTestHelpers.ConnectAsync(factory, secondClient, "second", meetingId);
        var final = await firstClient.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(first.Id, final!.FacilitatorId);

        await firstReturning.StopAsync();
        await secondReturning.StopAsync();
    }
}
