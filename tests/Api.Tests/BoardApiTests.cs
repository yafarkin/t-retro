using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Contracts;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Api.Tests;

public sealed class BoardApiTests
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    public async Task PrivacyMatrixOmitsHiddenFieldsFromNetworkJson(bool anonymousCards, bool privateCollection, bool anonymous)
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "privacy-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "privacy-member", "Автор");
        var meeting = await CreateMeetingAsync(facilitator, anonymousCards, privateCollection);
        await JoinAsync(facilitator, member, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        const string secretText = "Скрытый текст карточки";
        await CreateCardAsync(member, meeting.Id, await FirstColumnIdAsync(member, meeting.Id), secretText, anonymous);

        var response = await facilitator.GetAsync($"/api/meetings/{meeting.Id}/state");
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        var state = JsonSerializer.Deserialize<MeetingStateResponse>(raw, JsonOptions);
        var otherCard = Assert.Single(state!.Cards);
        Assert.False(otherCard.IsMine);
        var authorHidden = anonymousCards || privateCollection || anonymous;
        Assert.Equal(privateCollection, otherCard.IsHidden);
        Assert.Equal(privateCollection ? null : secretText, otherCard.Text);
        Assert.Equal(authorHidden ? null : state.Participants.Single(p => p.DisplayName == "Автор").Id, otherCard.AuthorId);
        Assert.Equal(authorHidden ? null : "Автор", otherCard.AuthorDisplayName);

        using var document = JsonDocument.Parse(raw);
        var cardJson = document.RootElement.GetProperty("cards")[0];
        Assert.Equal(!privateCollection, cardJson.TryGetProperty("text", out _));
        Assert.Equal(!privateCollection, cardJson.TryGetProperty("originalColumnColor", out _));
        Assert.Equal(!authorHidden, cardJson.TryGetProperty("authorId", out _));
        Assert.Equal(!authorHidden, cardJson.TryGetProperty("authorDisplayName", out _));
        if (privateCollection)
        {
            Assert.DoesNotContain(secretText, raw);
        }

        var ownState = await member.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        var ownCard = Assert.Single(ownState!.Cards);
        Assert.True(ownCard.IsMine);
        Assert.False(ownCard.IsHidden);
        Assert.Equal(secretText, ownCard.Text);
    }

    [Fact]
    public async Task CardCommandsCheckTextOwnershipVersionsPhaseAndOperationId()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "cards-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "cards-member", "Участник");
        var meeting = await CreateMeetingAsync(facilitator, true, true);
        await JoinAsync(facilitator, member, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var columnId = await FirstColumnIdAsync(member, meeting.Id);

        var emptyResponse = await member.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = "   ", OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
        var longResponse = await member.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = new string('x', 4001), OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, longResponse.StatusCode);

        var created = await CreateCardAsync(member, meeting.Id, columnId, "  Идея участника  ");
        var memberState = await member.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        var card = Assert.Single(memberState!.Cards);
        Assert.Equal("Идея участника", card.Text);
        var cardId = Guid.Parse(created.Id);

        var foreignUpdate = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards/{cardId}",
            new UpdateCardRequest { Text = "Чужая правка", ExpectedVersion = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, foreignUpdate.StatusCode);
        var staleUpdate = await member.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards/{cardId}",
            new UpdateCardRequest { Text = "Устаревшая правка", ExpectedVersion = 0 });
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var update = await member.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards/{cardId}",
            new UpdateCardRequest { Text = "  Новая редакция  ", ExpectedVersion = 1 });
        update.EnsureSuccessStatusCode();

        var operationId = Guid.NewGuid();
        var firstCreate = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = "Карточка ведущего", OperationId = operationId });
        firstCreate.EnsureSuccessStatusCode();
        var firstResult = await firstCreate.Content.ReadFromJsonAsync<CardCreatedResponse>();
        var replayCreate = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = "Карточка ведущего", OperationId = operationId });
        replayCreate.EnsureSuccessStatusCode();
        var replayResult = await replayCreate.Content.ReadFromJsonAsync<CardCreatedResponse>();
        Assert.Equal(firstResult, replayResult);
        var conflictingCreate = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = "Другое тело", OperationId = operationId });
        Assert.Equal(HttpStatusCode.Conflict, conflictingCreate.StatusCode);
        var conflictingAnonymity = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = "Карточка ведущего", Anonymous = true, OperationId = operationId });
        Assert.Equal(HttpStatusCode.Conflict, conflictingAnonymity.StatusCode);

        var deleted = await CreateCardAsync(member, meeting.Id, columnId, "Удаляемая карточка");
        var delete = await member.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/meetings/{meeting.Id}/cards/{deleted.Id}")
        {
            Content = JsonContent.Create(new DeleteCardRequest { ExpectedVersion = 1 }),
        });
        delete.EnsureSuccessStatusCode();

        var current = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(2, current!.Cards.Count);
        var memberAdvance = await member.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/advance",
            new AdvanceMeetingRequest { FromPhase = "Collect", ExpectedVersion = current.Version, OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, memberAdvance.StatusCode);
        var staleAdvance = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/advance",
            new AdvanceMeetingRequest { FromPhase = "Collect", ExpectedVersion = current.Version - 1, OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, staleAdvance.StatusCode);
        var wrongPhaseAdvance = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/advance",
            new AdvanceMeetingRequest { FromPhase = "Vote", ExpectedVersion = current.Version, OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, wrongPhaseAdvance.StatusCode);

        var advanceOperation = Guid.NewGuid();
        var advance = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/advance",
            new AdvanceMeetingRequest { FromPhase = "Collect", ExpectedVersion = current.Version, OperationId = advanceOperation });
        advance.EnsureSuccessStatusCode();
        var advanceResult = await advance.Content.ReadFromJsonAsync<VersionResponse>();
        var advanceReplay = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/advance",
            new AdvanceMeetingRequest { FromPhase = "Collect", ExpectedVersion = current.Version, OperationId = advanceOperation });
        advanceReplay.EnsureSuccessStatusCode();
        Assert.Equal(advanceResult, await advanceReplay.Content.ReadFromJsonAsync<VersionResponse>());

        var grouped = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal("Group", grouped!.Phase);
        Assert.Equal(2, grouped.Cards.Count);
        Assert.All(grouped.Cards, item => Assert.Null(item.AuthorId));
        var lockedUpdate = await member.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards/{cardId}",
            new UpdateCardRequest { Text = "Поздняя правка", ExpectedVersion = 2 });
        Assert.Equal(HttpStatusCode.Conflict, lockedUpdate.StatusCode);
        var lockedDelete = await member.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/meetings/{meeting.Id}/cards/{cardId}")
        {
            Content = JsonContent.Create(new DeleteCardRequest { ExpectedVersion = 2 }),
        });
        Assert.Equal(HttpStatusCode.Conflict, lockedDelete.StatusCode);
        var lockedSettings = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/settings",
            new UpdateMeetingSettingsRequest
            {
                ExpectedVersion = grouped.Version,
                Name = "Поздние настройки",
                Columns = [new ColumnRequest { Name = "Начать", Color = "#36A269" }],
                VoteSettings = new VoteSettingsRequest(),
            });
        Assert.Equal(HttpStatusCode.Conflict, lockedSettings.StatusCode);

        await AdvanceAsync(facilitator, meeting.Id, "Group", grouped.Version);
        var voting = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        await AdvanceAsync(facilitator, meeting.Id, "Vote", voting!.Version);
        var discussion = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        await using var connection = factory.Database.CreateConnection();
        await connection.OpenAsync();
        await using var fingerprintCommand = connection.CreateCommand();
        fingerprintCommand.CommandText = "SELECT fingerprint FROM command_deduplication WHERE operation_type = 'CreateCard';";
        var fingerprint = (string)(await fingerprintCommand.ExecuteScalarAsync())!;
        Assert.DoesNotContain("Карточка", fingerprint);

        var completion = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/advance",
            new AdvanceMeetingRequest
            {
                FromPhase = "Discuss",
                ExpectedVersion = discussion!.Version,
                OperationId = Guid.NewGuid(),
            });
        completion.EnsureSuccessStatusCode();
        var afterCompletion = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal("Closed", afterCompletion!.Phase);

        await using var authorsCommand = connection.CreateCommand();
        authorsCommand.CommandText = "SELECT COUNT(*) FROM card_authors;";
        Assert.Equal(0L, (long)(await authorsCommand.ExecuteScalarAsync())!);
        await using var dedupCommand = connection.CreateCommand();
        dedupCommand.CommandText = "SELECT COUNT(*) FROM command_deduplication WHERE operation_type = 'CreateCard';";
        Assert.Equal(0L, (long)(await dedupCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ReadinessCountsActiveMembersAndClearsBetweenPhases()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        using var lateClient = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "ready-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "ready-member", "Участник");
        var late = await ApiTestHelpers.LoginAsync(lateClient, "ready-late", "Поздний");
        var meeting = await CreateMeetingAsync(facilitator, true, true);
        await JoinAsync(facilitator, member, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);

        await SetReadyAsync(facilitator, meeting.Id, true);
        var memberState = await member.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(1, memberState!.Readiness!.ReadyCount);
        Assert.Equal(2, memberState.Readiness.TotalCount);
        Assert.False(memberState.Readiness.MyReady);
        Assert.True(memberState.Participants.Single(participant => participant.Subject == "ready-facilitator").IsReady);
        Assert.False(memberState.Participants.Single(participant => participant.Subject == "ready-member").IsReady);
        await SetReadyAsync(member, meeting.Id, true);
        var allReady = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal("Collect", allReady!.Phase);
        Assert.Equal(2, allReady.Readiness!.ReadyCount);

        await JoinAsync(facilitator, lateClient, meeting.Id);
        var afterLateJoin = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(2, afterLateJoin!.Readiness!.ReadyCount);
        Assert.Equal(3, afterLateJoin.Readiness.TotalCount);
        var lateState = await lateClient.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.False(lateState!.Readiness!.MyReady);
        await SetReadyAsync(lateClient, meeting.Id, true);
        var beforeExclusion = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(3, beforeExclusion!.Readiness!.ReadyCount);
        var exclusion = await facilitator.PostAsync($"/api/meetings/{meeting.Id}/members/{late.Id}/exclude", null);
        exclusion.EnsureSuccessStatusCode();
        var afterExclusion = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(2, afterExclusion!.Readiness!.ReadyCount);
        Assert.Equal(2, afterExclusion.Readiness.TotalCount);
        Assert.False(afterExclusion.Participants.Single(participant => participant.Id == late.Id).IsReady);

        await AdvanceAsync(facilitator, meeting.Id, "Collect", afterExclusion.Version);
        var grouped = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(0, grouped!.Readiness!.ReadyCount);
        Assert.Equal(2, grouped.Readiness.TotalCount);
        Assert.All(grouped.Participants, participant => Assert.False(participant.IsReady));

        await SetReadyAsync(member, meeting.Id, true, "Group");
        var groupReady = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal("Group", groupReady!.Phase);
        Assert.Equal(1, groupReady.Readiness!.ReadyCount);
        Assert.False(groupReady.Readiness.MyReady);
        Assert.True(groupReady.Participants.Single(participant => participant.Subject == "ready-member").IsReady);

        await AdvanceAsync(facilitator, meeting.Id, "Group", groupReady.Version);
        var voting = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(0, voting!.Readiness!.ReadyCount);
        Assert.Equal(2, voting.Readiness.TotalCount);
        Assert.False(voting.Readiness.MyReady);

        await using var connection = factory.Database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM readiness;";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task StorageRejectsCardColumnFromAnotherMeeting()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "foreign-column", "Ведущий");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator);
        var other = await ApiTestHelpers.CreateMeetingAsync(facilitator);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var columnId = await FirstColumnIdAsync(facilitator, meeting.Id);
        var foreignColumnId = await FirstColumnIdAsync(facilitator, other.Id);
        var card = await CreateCardAsync(facilitator, meeting.Id, columnId, "Карточка");

        await using var connection = factory.Database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE cards SET column_id = @ColumnId WHERE meeting_id = @MeetingId AND id = @Id;";
        command.AddId("ColumnId", Guid.Parse(foreignColumnId));
        command.AddId("MeetingId", Guid.Parse(meeting.Id));
        command.AddId("Id", Guid.Parse(card.Id));
        var error = await Assert.ThrowsAnyAsync<DbException>(() => command.ExecuteNonQueryAsync());
        if (factory.Database.IsPostgreSql)
        {
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error).SqlState);
        }
        else
        {
            Assert.Equal(787, Assert.IsType<SqliteException>(error).SqliteExtendedErrorCode);
        }

        var state = await facilitator.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meeting.Id}/state");
        Assert.Equal(columnId, Assert.Single(state!.Cards).ColumnId);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<MeetingCreatedResponse> CreateMeetingAsync(
        HttpClient client,
        bool anonymousCards,
        bool privateCollection)
    {
        var request = ApiTestHelpers.DefaultMeeting("Ретро M3");
        request.AnonymousCards = anonymousCards;
        request.PrivateCollection = privateCollection;
        var response = await client.PostAsJsonAsync("/api/meetings", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeetingCreatedResponse>())!;
    }

    private static async Task JoinAsync(HttpClient facilitator, HttpClient member, string meetingId)
    {
        var invite = await facilitator.GetFromJsonAsync<InviteResponse>($"/api/meetings/{meetingId}/invite");
        var response = await member.PostAsJsonAsync(
            "/api/join",
            new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url)));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> FirstColumnIdAsync(HttpClient client, string meetingId)
    {
        var state = await client.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meetingId}/state");
        return state!.Columns[0].Id;
    }

    private static async Task<CardCreatedResponse> CreateCardAsync(
        HttpClient client,
        string meetingId,
        string columnId,
        string text,
        bool anonymous = false)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = text, Anonymous = anonymous, OperationId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardCreatedResponse>())!;
    }

    private static async Task AdvanceAsync(HttpClient client, string meetingId, string phase, int version)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/advance",
            new AdvanceMeetingRequest { FromPhase = phase, ExpectedVersion = version, OperationId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
    }

    private static async Task SetReadyAsync(HttpClient client, string meetingId, bool ready, string phase = "Collect")
    {
        var response = await client.PutAsJsonAsync(
            $"/api/meetings/{meetingId}/ready",
            new SetReadinessRequest { Phase = phase, Ready = ready });
        response.EnsureSuccessStatusCode();
    }
}
