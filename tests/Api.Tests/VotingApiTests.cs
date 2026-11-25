using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Contracts;
using Microsoft.Data.Sqlite;

namespace Api.Tests;

public sealed class VotingApiTests
{
    [Fact]
    public async Task TopicsCountEveryCardOnceAndLateMemberGetsCurrentBudget()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        using var lateClient = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "vote-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "vote-member", "Участник");
        await ApiTestHelpers.LoginAsync(lateClient, "vote-late", "Поздний");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Ретро M5");
        await JoinAsync(facilitator, member, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var initial = await StateAsync(facilitator, meeting.Id);
        var cards = new List<string>();
        for (var index = 0; index < 41; index++)
        {
            cards.Add(await CreateCardAsync(facilitator, meeting.Id, initial.Columns[0].Id, $"Карточка {index + 1}"));
        }

        var collected = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", collected.Version);
        var boardVersion = (await StateAsync(facilitator, meeting.Id)).BoardVersion;
        for (var index = 1; index < 10; index++)
        {
            var move = await facilitator.PostAsJsonAsync(
                $"/api/meetings/{meeting.Id}/moves",
                new MoveCardRequest
                {
                    SourceKind = "card",
                    SourceId = Guid.Parse(cards[index]),
                    TargetKind = "card",
                    TargetId = Guid.Parse(cards[0]),
                    ExpectedBoardVersion = boardVersion,
                    OperationId = Guid.NewGuid(),
                });
            move.EnsureSuccessStatusCode();
            boardVersion = (await move.Content.ReadFromJsonAsync<BoardVersionResponse>())!.BoardVersion;
        }

        var grouped = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Group", grouped.Version);
        var voting = await StateAsync(member, meeting.Id);
        Assert.Equal(41, voting.Voting!.OriginalCardCount);
        Assert.Equal(3, voting.Voting.Budget);
        Assert.Equal(3, voting.Voting.Remaining);
        Assert.Equal(0, voting.Voting.BallotVersion);
        Assert.Equal(32, voting.Topics.Count);
        var groupTopic = Assert.Single(voting.Topics, topic => topic.Kind == "group");
        var groupedCards = voting.Cards.Where(card => card.GroupId == groupTopic.SourceId).ToArray();
        Assert.Equal(10, groupedCards.Length);
        var countedCards = voting.Topics.Where(topic => topic.Kind == "card").Select(topic => topic.SourceId)
            .Concat(groupedCards.Select(card => card.Id)).ToArray();
        Assert.Equal(41, countedCards.Distinct().Count());
        Assert.Equal(cards.Order(), countedCards.Order());

        await JoinAsync(facilitator, lateClient, meeting.Id);
        var lateState = await StateAsync(lateClient, meeting.Id);
        Assert.Equal(41, lateState.Voting!.OriginalCardCount);
        Assert.Equal(3, lateState.Voting.Budget);
        Assert.Equal(3, lateState.Voting.Remaining);
        Assert.Equal(0, lateState.Voting.BallotVersion);
        Assert.Equal(32, lateState.Topics.Count);

        var voted = await SetVotesAsync(lateClient, meeting.Id, groupTopic.Id, 3, 0);
        voted.EnsureSuccessStatusCode();
        var voteResult = await voted.Content.ReadFromJsonAsync<MyVotesResponse>();
        Assert.Equal(0, voteResult!.Remaining);
        Assert.Equal(1, voteResult.BallotVersion);
        var afterVote = await StateAsync(lateClient, meeting.Id);
        Assert.Null(afterVote.Topics.Single(topic => topic.Id == groupTopic.Id).TotalVotes);
        Assert.Equal(3, afterVote.Topics.Single(topic => topic.Id == groupTopic.Id).MyVotes);
        Assert.Equal(0, afterVote.Voting!.Remaining);

        var childVote = await SetVotesAsync(lateClient, meeting.Id, groupedCards[0].Id, 1, 1);
        Assert.Equal(HttpStatusCode.NotFound, childVote.StatusCode);
        var negativeVote = await SetVotesAsync(lateClient, meeting.Id, groupTopic.Id, -1, 1);
        Assert.Equal(HttpStatusCode.BadRequest, negativeVote.StatusCode);
        var fractionalVote = await lateClient.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/topics/{groupTopic.Id}/my-votes",
            new { Count = 1.5, ExpectedVersion = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, fractionalVote.StatusCode);
        var otherTopic = voting.Topics.First(topic => topic.Kind == "card");
        var overspend = await SetVotesAsync(lateClient, meeting.Id, otherTopic.Id, 1, 1);
        Assert.Equal(HttpStatusCode.Conflict, overspend.StatusCode);
        Assert.Equal("VoteBudgetExceeded", (await overspend.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        var stale = await SetVotesAsync(lateClient, meeting.Id, groupTopic.Id, 2, 0);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var revoked = await SetVotesAsync(lateClient, meeting.Id, groupTopic.Id, 0, 1);
        revoked.EnsureSuccessStatusCode();
        var afterRevoke = await StateAsync(lateClient, meeting.Id);
        Assert.Equal(3, afterRevoke.Voting!.Remaining);
        Assert.Equal(2, afterRevoke.Voting.BallotVersion);
        Assert.Equal(0, afterRevoke.Topics.Single(topic => topic.Id == groupTopic.Id).MyVotes);

        await using var connection = new SqliteConnection($"Data Source={factory.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT initial_card_count, (SELECT COUNT(*) FROM topics) FROM meetings WHERE id = @Id;";
        command.Parameters.AddWithValue("@Id", meeting.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(41L, reader.GetInt64(0));
        Assert.Equal(32L, reader.GetInt64(1));
    }

    [Fact]
    public async Task VotingHidesTotalsAndOtherBallotsUntilDiscussionEvenForAnOldOpenMeeting()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        var memberUser = await ApiTestHelpers.LoginAsync(member, "privacy-vote-member", "Голосующий");
        await ApiTestHelpers.LoginAsync(facilitator, "privacy-vote-facilitator", "Ведущий");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Приватность M5");
        await using (var connection = new SqliteConnection($"Data Source={factory.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE meetings SET anonymous_votes = 0 WHERE id = $id;";
            command.Parameters.AddWithValue("$id", meeting.Id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await JoinAsync(facilitator, member, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var columnId = (await StateAsync(member, meeting.Id)).Columns[0].Id;
        await CreateCardAsync(member, meeting.Id, columnId, "Тема");
        var collected = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", collected.Version);
        var grouped = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Group", grouped.Version);
        var beforeVote = await StateAsync(member, meeting.Id);
        var topicId = beforeVote.Topics.Single().Id;
        var voted = await SetVotesAsync(member, meeting.Id, topicId, 2, 0);
        voted.EnsureSuccessStatusCode();

        var ownState = await StateAsync(member, meeting.Id);
        Assert.Equal(2, ownState.Topics.Single().MyVotes);
        Assert.Null(ownState.Topics.Single().TotalVotes);
        var facilitatorResponse = await facilitator.GetAsync($"/api/meetings/{meeting.Id}/state");
        facilitatorResponse.EnsureSuccessStatusCode();
        var raw = await facilitatorResponse.Content.ReadAsStringAsync();
        var facilitatorState = JsonSerializer.Deserialize<MeetingStateResponse>(raw, JsonOptions)!;
        Assert.Equal(beforeVote.Version, facilitatorState.Version);
        Assert.Equal(0, facilitatorState.Voting!.BallotVersion);
        Assert.Equal(0, facilitatorState.Topics.Single().MyVotes);
        using var voteJson = JsonDocument.Parse(raw);
        var topicJson = voteJson.RootElement.GetProperty("topics")[0];
        Assert.False(topicJson.TryGetProperty("totalVotes", out _));
        Assert.False(topicJson.TryGetProperty("voters", out _));

        var excluded = await facilitator.PostAsync($"/api/meetings/{meeting.Id}/members/{memberUser.Id}/exclude", null);
        excluded.EnsureSuccessStatusCode();
        var afterExclusion = await StateAsync(facilitator, meeting.Id);
        Assert.Null(afterExclusion.Topics.Single().TotalVotes);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/meetings/{meeting.Id}/state")).StatusCode);
        var tooLow = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/vote-settings",
            new UpdateVoteSettingsRequest
            {
                Mode = "fixed",
                Fixed = 1,
                ExpectedVersion = afterExclusion.Version,
            });
        Assert.Equal(HttpStatusCode.Conflict, tooLow.StatusCode);
        Assert.Equal("VoteBudgetTooLow", (await tooLow.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        await AdvanceAsync(facilitator, meeting.Id, "Vote", afterExclusion.Version);
        using var discussionJson = JsonDocument.Parse(await facilitator.GetStringAsync($"/api/meetings/{meeting.Id}/state"));
        var discussed = discussionJson.RootElement.GetProperty("topics")[0];
        Assert.Equal(2, discussed.GetProperty("totalVotes").GetInt32());
        Assert.False(discussed.TryGetProperty("myVotes", out _));
        Assert.False(discussed.TryGetProperty("voters", out _));
    }

    [Fact]
    public async Task ParallelTabsAndBudgetReductionCannotOverspend()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var firstTab = factory.CreateClient();
        using var secondTab = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "race-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(firstTab, "race-member", "Участник");
        await ApiTestHelpers.LoginAsync(secondTab, "race-member", "Участник");
        var request = ApiTestHelpers.DefaultMeeting("Гонки M5");
        request.VoteSettings = new VoteSettingsRequest { Mode = "fixed", Fixed = 3 };
        var created = await facilitator.PostAsJsonAsync("/api/meetings", request);
        created.EnsureSuccessStatusCode();
        var meeting = (await created.Content.ReadFromJsonAsync<MeetingCreatedResponse>())!;
        await JoinAsync(facilitator, firstTab, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var columnId = (await StateAsync(firstTab, meeting.Id)).Columns[0].Id;
        for (var index = 0; index < 3; index++)
        {
            await CreateCardAsync(firstTab, meeting.Id, columnId, $"Тема {index + 1}");
        }
        var collected = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", collected.Version);
        var grouped = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Group", grouped.Version);
        var initial = await StateAsync(firstTab, meeting.Id);
        var topics = initial.Topics.Select(topic => topic.Id).ToArray();
        var firstVote = await SetVotesAsync(firstTab, meeting.Id, topics[0], 2, 0);
        firstVote.EnsureSuccessStatusCode();

        var parallelVotes = await Task.WhenAll(
            SetVotesAsync(firstTab, meeting.Id, topics[1], 1, 1),
            SetVotesAsync(secondTab, meeting.Id, topics[2], 1, 1));
        Assert.Equal(1, parallelVotes.Count(response => response.IsSuccessStatusCode));
        Assert.Equal(1, parallelVotes.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var afterParallel = await StateAsync(firstTab, meeting.Id);
        Assert.Equal(3, afterParallel.Voting!.Spent);
        Assert.Equal(0, afterParallel.Voting.Remaining);
        Assert.Equal(2, afterParallel.Voting.BallotVersion);

        var facilitatorState = await StateAsync(facilitator, meeting.Id);
        var tooLow = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/vote-settings",
            new UpdateVoteSettingsRequest { Mode = "fixed", Fixed = 2, ExpectedVersion = facilitatorState.Version });
        Assert.Equal(HttpStatusCode.Conflict, tooLow.StatusCode);
        var error = await tooLow.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("VoteBudgetTooLow", error!.Code);
        Assert.DoesNotContain("3", error.Message);

        foreach (var topicId in topics.Skip(1))
        {
            var current = await StateAsync(firstTab, meeting.Id);
            if (current.Topics.Single(topic => topic.Id == topicId).MyVotes > 0)
            {
                var removed = await SetVotesAsync(firstTab, meeting.Id, topicId, 0, current.Voting!.BallotVersion);
                removed.EnsureSuccessStatusCode();
            }
        }
        var beforeRace = await StateAsync(firstTab, meeting.Id);
        Assert.Equal(2, beforeRace.Voting!.Spent);
        var remainingTopic = topics.Skip(1).First(topicId =>
            beforeRace.Topics.Single(topic => topic.Id == topicId).MyVotes == 0);
        var beforeSettingsRace = await StateAsync(facilitator, meeting.Id);
        var race = await Task.WhenAll(
            SetVotesAsync(firstTab, meeting.Id, remainingTopic, 1, beforeRace.Voting.BallotVersion),
            facilitator.PutAsJsonAsync(
                $"/api/meetings/{meeting.Id}/vote-settings",
                new UpdateVoteSettingsRequest { Mode = "fixed", Fixed = 2, ExpectedVersion = beforeSettingsRace.Version }));
        Assert.Equal(1, race.Count(response => response.IsSuccessStatusCode));
        Assert.Equal(1, race.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var final = await StateAsync(firstTab, meeting.Id);
        Assert.True(final.Voting!.Spent <= final.Voting.Budget);
        Assert.True(final.Voting.Budget is 2 or 3);

        var forbidden = await firstTab.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/vote-settings",
            new UpdateVoteSettingsRequest { Mode = "fixed", Fixed = 4, ExpectedVersion = final.Version });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task EmptyVoteHasBudgetAndNoTargets()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "empty-vote", "Ведущий");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Пустая доска");
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", 2);
        await AdvanceAsync(facilitator, meeting.Id, "Group", 3);

        var state = await StateAsync(facilitator, meeting.Id);
        Assert.Empty(state.Topics);
        Assert.Equal(0, state.Voting!.OriginalCardCount);
        Assert.Equal(2, state.Voting.Budget);
        Assert.Equal(2, state.Voting.Remaining);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static Task<HttpResponseMessage> SetVotesAsync(
        HttpClient client,
        string meetingId,
        string topicId,
        int count,
        int expectedVersion) =>
        client.PutAsJsonAsync(
            $"/api/meetings/{meetingId}/topics/{topicId}/my-votes",
            new SetMyVotesRequest { Count = count, ExpectedVersion = expectedVersion });

    private static async Task JoinAsync(HttpClient facilitator, HttpClient member, string meetingId)
    {
        var invite = await facilitator.GetFromJsonAsync<InviteResponse>($"/api/meetings/{meetingId}/invite");
        var response = await member.PostAsJsonAsync(
            "/api/join",
            new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url)));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> CreateCardAsync(HttpClient client, string meetingId, string columnId, string text)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = text, OperationId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardCreatedResponse>())!.Id;
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
