using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace Api.Tests;

public sealed class MemberApiTests
{
    [Fact]
    public async Task ExclusionRevokesAllTabsAndTransferWorksForOfflineMember()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitatorClient = factory.CreateClient();
        using var memberClient = factory.CreateClient();
        using var offlineClient = factory.CreateClient();
        var facilitator = await ApiTestHelpers.LoginAsync(facilitatorClient, "facilitator", "Ведущий");
        var member = await ApiTestHelpers.LoginAsync(memberClient, "member", "Участник");
        var offline = await ApiTestHelpers.LoginAsync(offlineClient, "offline", "Офлайн участник");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitatorClient);
        var meetingId = Guid.Parse(meeting.Id);
        var invite = await facilitatorClient.GetFromJsonAsync<InviteResponse>($"/api/meetings/{meeting.Id}/invite");
        await memberClient.PostAsJsonAsync("/api/join", new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url)));
        await offlineClient.PostAsJsonAsync("/api/join", new JoinRequest(ApiTestHelpers.TokenFromUrl(invite.Url)));
        var firstTab = await SignalRTestHelpers.ConnectAsync(factory, memberClient, "member", meetingId);
        var secondTab = await SignalRTestHelpers.ConnectAsync(factory, memberClient, "member", meetingId);
        var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revokedCount = 0;
        void OnRevoked(Guid id)
        {
            if (id == meetingId && Interlocked.Increment(ref revokedCount) == 2)
            {
                revoked.TrySetResult();
            }
        }
        firstTab.On<Guid>("AccessRevoked", OnRevoked);
        secondTab.On<Guid>("AccessRevoked", OnRevoked);

        var excludeResponse = await facilitatorClient.PostAsync(
            $"/api/meetings/{meeting.Id}/members/{member.Id}/exclude",
            null);
        Assert.Equal(HttpStatusCode.NoContent, excludeResponse.StatusCode);
        await revoked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var revokedState = await memberClient.GetAsync($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(HttpStatusCode.Forbidden, revokedState.StatusCode);
        var repeatedJoin = await memberClient.PostAsJsonAsync(
            "/api/join",
            new JoinRequest(ApiTestHelpers.TokenFromUrl(invite.Url)));
        Assert.Equal(HttpStatusCode.Forbidden, repeatedJoin.StatusCode);
        var memberList = await memberClient.GetFromJsonAsync<MeetingSummaryResponse[]>("/api/meetings");
        Assert.Empty(memberList!);

        var facilitatorState = await facilitatorClient.GetFromJsonAsync<MeetingStateResponse>(
            $"/api/meetings/{meeting.Id}/state");
        var excluded = facilitatorState!.Participants.Single(participant => participant.Id == member.Id);
        Assert.Equal("Excluded", excluded.Status);
        Assert.False(excluded.IsOnline);

        var transferResponse = await facilitatorClient.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/facilitator",
            new UpdateFacilitatorRequest(Guid.Parse(offline.Id)));
        Assert.Equal(HttpStatusCode.NoContent, transferResponse.StatusCode);
        var formerState = await facilitatorClient.GetFromJsonAsync<MeetingStateResponse>(
            $"/api/meetings/{meeting.Id}/state");
        Assert.False(formerState!.CanManage);
        var newState = await offlineClient.GetFromJsonAsync<MeetingStateResponse>(
            $"/api/meetings/{meeting.Id}/state");
        Assert.True(newState!.CanManage);
        Assert.Equal(offline.Id, newState.FacilitatorId);

        var selfExclude = await facilitatorClient.PostAsync(
            $"/api/meetings/{meeting.Id}/members/{facilitator.Id}/exclude",
            null);
        Assert.Equal(HttpStatusCode.BadRequest, selfExclude.StatusCode);
        await firstTab.StopAsync();
        await secondTab.StopAsync();
    }
}
