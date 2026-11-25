using System.Net;
using System.Net.Http.Json;
using Api.Contracts;

namespace Api.Tests;

public sealed class MeetingApiTests
{
    [Fact]
    public async Task CreatorGetsFacilitatorSnapshotAndUpdatesDraftAtomically()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var creatorClient = factory.CreateClient();
        using var outsiderClient = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(creatorClient, "creator", "Создатель");
        await ApiTestHelpers.LoginAsync(outsiderClient, "outsider", "Посторонний");

        var created = await ApiTestHelpers.CreateMeetingAsync(creatorClient);
        var state = await creatorClient.GetFromJsonAsync<MeetingStateResponse>(
            $"/api/meetings/{created.Id}/state");

        Assert.Equal(created.Id, state!.Id);
        Assert.True(state.CanManage);
        Assert.Equal(3, state.Columns.Count);
        Assert.Equal(3, state.Columns.Select(column => column.Id).Distinct().Count());
        Assert.Single(state.Participants);
        Assert.Equal(state.Participants[0].Id, state.FacilitatorId);

        var outsiderResponse = await outsiderClient.GetAsync($"/api/meetings/{created.Id}/state");
        Assert.Equal(HttpStatusCode.NotFound, outsiderResponse.StatusCode);

        var originalColumnIds = state.Columns.Select(column => column.Id).ToArray();
        var update = ApiTestHelpers.DefaultMeeting("Новое название");
        update.Columns = [update.Columns[2], update.Columns[0]];
        update.AnonymousCards = false;
        update.VoteSettings = new VoteSettingsRequest { Mode = "fixed", Fixed = 4 };
        var expectedVersion = state.Version;
        var updateResponse = await creatorClient.PutAsJsonAsync(
            $"/api/meetings/{created.Id}/settings",
            new UpdateMeetingSettingsRequest
            {
                ExpectedVersion = expectedVersion,
                Name = update.Name,
                Columns = update.Columns,
                AnonymousCards = update.AnonymousCards,
                PrivateCollection = update.PrivateCollection,
                VoteSettings = update.VoteSettings,
            });

        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<MeetingStateResponse>();
        Assert.Equal(expectedVersion + 1, updated!.Version);
        Assert.Equal("Новое название", updated.Name);
        Assert.False(updated.AnonymousCards);
        Assert.Equal(4, updated.VoteSettings.Fixed);
        Assert.Equal(2, updated.Columns.Count);
        Assert.DoesNotContain(updated.Columns, column => originalColumnIds.Contains(column.Id));
        Assert.Equal("Продолжать", updated.Columns[0].Name);

        var staleResponse = await creatorClient.PutAsJsonAsync(
            $"/api/meetings/{created.Id}/settings",
            new UpdateMeetingSettingsRequest
            {
                ExpectedVersion = expectedVersion,
                Name = "Конфликт",
                Columns = update.Columns,
                VoteSettings = update.VoteSettings,
            });
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        var forbiddenResponse = await outsiderClient.PutAsJsonAsync(
            $"/api/meetings/{created.Id}/settings",
            new UpdateMeetingSettingsRequest
            {
                ExpectedVersion = updated.Version,
                Name = "Чужая встреча",
                Columns = update.Columns,
                VoteSettings = update.VoteSettings,
            });
        Assert.Equal(HttpStatusCode.NotFound, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task InvalidMeetingDoesNotAppearInList()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var client = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(client, "creator", "Создатель");
        var request = ApiTestHelpers.DefaultMeeting("Невалидная встреча");
        request.Columns[0].Color = "red";

        var response = await client.PostAsJsonAsync("/api/meetings", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var meetings = await client.GetFromJsonAsync<MeetingSummaryResponse[]>("/api/meetings");
        Assert.Empty(meetings!);
    }
}
