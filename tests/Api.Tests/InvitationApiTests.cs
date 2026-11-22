using System.Net;
using System.Net.Http.Json;
using Api.Contracts;

namespace Api.Tests;

public sealed class InvitationApiTests
{
    [Fact]
    public async Task JoinIsIdempotentAndRotationRevokesOldToken()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitatorClient = factory.CreateClient();
        using var memberClient = factory.CreateClient();
        using var lateClient = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitatorClient, "facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(memberClient, "member", "Участник");
        await ApiTestHelpers.LoginAsync(lateClient, "late", "Поздний участник");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitatorClient);

        var invite = await facilitatorClient.GetFromJsonAsync<InviteResponse>(
            $"/api/meetings/{meeting.Id}/invite");
        Assert.Equal(1, invite!.Version);
        var oldToken = ApiTestHelpers.TokenFromUrl(invite.Url);

        var beforeJoin = await memberClient.GetAsync($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(HttpStatusCode.NotFound, beforeJoin.StatusCode);

        var firstJoin = await memberClient.PostAsJsonAsync("/api/join", new JoinRequest(oldToken));
        var repeatedJoin = await memberClient.PostAsJsonAsync("/api/join", new JoinRequest(oldToken));
        firstJoin.EnsureSuccessStatusCode();
        repeatedJoin.EnsureSuccessStatusCode();
        Assert.Equal(meeting.Id, (await firstJoin.Content.ReadFromJsonAsync<JoinResponse>())!.MeetingId);
        Assert.Equal(meeting.Id, (await repeatedJoin.Content.ReadFromJsonAsync<JoinResponse>())!.MeetingId);

        var memberState = await memberClient.GetFromJsonAsync<MeetingStateResponse>(
            $"/api/meetings/{meeting.Id}/state");
        Assert.False(memberState!.CanManage);
        Assert.Equal(2, memberState.Participants.Count);

        var forbiddenInvite = await memberClient.GetAsync($"/api/meetings/{meeting.Id}/invite");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenInvite.StatusCode);

        var rotatedResponse = await facilitatorClient.PostAsync(
            $"/api/meetings/{meeting.Id}/invite/rotate",
            null);
        rotatedResponse.EnsureSuccessStatusCode();
        var rotated = await rotatedResponse.Content.ReadFromJsonAsync<InviteResponse>();
        Assert.Equal(2, rotated!.Version);

        var oldJoin = await lateClient.PostAsJsonAsync("/api/join", new JoinRequest(oldToken));
        Assert.Equal(HttpStatusCode.Conflict, oldJoin.StatusCode);
        var newJoin = await lateClient.PostAsJsonAsync(
            "/api/join",
            new JoinRequest(ApiTestHelpers.TokenFromUrl(rotated.Url)));
        newJoin.EnsureSuccessStatusCode();

        var repeatedInvite = await facilitatorClient.GetFromJsonAsync<InviteResponse>(
            $"/api/meetings/{meeting.Id}/invite");
        Assert.Equal(2, repeatedInvite!.Version);
        var repeatedTokenJoin = await lateClient.PostAsJsonAsync(
            "/api/join",
            new JoinRequest(ApiTestHelpers.TokenFromUrl(repeatedInvite.Url)));
        repeatedTokenJoin.EnsureSuccessStatusCode();
    }
}
