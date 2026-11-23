using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Contracts;
using Application;
using Microsoft.Data.Sqlite;
using UglyToad.PdfPig;

namespace Api.Tests;

public sealed class CompletionApiTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompletionPersistsTotalsPdfAndIndependentPrivacy(bool anonymousCards, bool anonymousVotes)
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        using var excluded = factory.CreateClient();
        var setup = await CreateDiscussedMeetingAsync(
            facilitator,
            member,
            excluded,
            anonymousCards,
            anonymousVotes,
            "Ретро с итогами");
        var missing = await facilitator.GetAsync($"/api/meetings/{setup.State.Id}/report.pdf");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var operationId = Guid.NewGuid();
        var request = new AdvanceMeetingRequest
        {
            FromPhase = "Discuss",
            ExpectedVersion = setup.State.Version,
            OperationId = operationId,
        };
        var completed = await CloseAsync(facilitator, setup.State.Id, request);
        completed.EnsureSuccessStatusCode();
        var closed = await StateAsync(facilitator, setup.State.Id);
        Assert.Equal("Closed", closed.Phase);
        Assert.Null(closed.Voting);
        Assert.Null(closed.Readiness);
        Assert.Null(closed.Timer);
        Assert.Equal(2, closed.Totals!.ParticipantCount);
        Assert.Equal(3, closed.Totals.CardCount);
        Assert.Equal(2, closed.Totals.TopicCount);
        Assert.Equal(3, closed.Totals.VoteCount);
        Assert.Equal(2, closed.Discussion!.Queue.Count);
        Assert.Equal(3, closed.Topics.Single(topic => topic.Id == setup.GroupTopicId).TotalVotes);
        Assert.Contains(closed.Topics, topic => topic.TotalVotes == 0);
        Assert.All(closed.Cards, card => Assert.False(card.IsHidden));
        Assert.All(closed.Cards, card => Assert.Equal(anonymousCards, card.AuthorId is null));
        var voters = closed.Topics.Single(topic => topic.Id == setup.GroupTopicId).Voters;
        Assert.Equal(anonymousVotes, voters is null);
        if (!anonymousVotes)
        {
            Assert.Equal(3, voters!.Sum(vote => vote.Count));
        }

        Assert.Equal(anonymousCards ? 0 : 3, await CountAsync(factory.DatabasePath, "card_authors"));
        Assert.Equal(anonymousVotes ? 0 : 3, await CountAsync(factory.DatabasePath, "votes"));
        Assert.Equal(anonymousVotes ? 0 : 3, await CountAsync(factory.DatabasePath, "personal_ballots"));
        Assert.Equal(anonymousCards ? 0 : 3, await CountAsync(
            factory.DatabasePath,
            "command_deduplication",
            "operation_type = 'CreateCard'"));
        Assert.Equal(1, await CountAsync(factory.DatabasePath, "meeting_totals"));
        Assert.Equal(2, await CountAsync(factory.DatabasePath, "topic_totals"));
        Assert.Equal(1, await CountAsync(factory.DatabasePath, "reports"));
        Assert.Equal(0, await CountAsync(factory.DatabasePath, "readiness"));
        Assert.Equal(3, await CountAsync(factory.DatabasePath, "members"));
        Assert.Equal(1, await CountAsync(factory.DatabasePath, "actions"));
        var payload = JsonSerializer.Deserialize<MeetingStateData>(await PayloadAsync(factory.DatabasePath))!;
        Assert.All(payload.Cards, card => Assert.Equal(anonymousCards, card.AuthorId is null));
        Assert.Equal(anonymousVotes ? 0 : 3, payload.Votes.Count);
        Assert.Empty(payload.Ballots);
        Assert.Empty(payload.ReadyUserIds);

        var facilitatorReport = await facilitator.GetAsync($"/api/meetings/{setup.State.Id}/report.pdf");
        facilitatorReport.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", facilitatorReport.Content.Headers.ContentType!.MediaType);
        Assert.Contains("private", facilitatorReport.Headers.CacheControl!.ToString());
        Assert.Contains("no-store", facilitatorReport.Headers.CacheControl!.ToString());
        var pdf = await facilitatorReport.Content.ReadAsByteArrayAsync();
        Assert.True(pdf.Length > 1000);
        using var memberSession = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(memberSession, "completion-member", "Участник");
        var memberReport = await memberSession.GetAsync($"/api/meetings/{setup.State.Id}/report.pdf");
        memberReport.EnsureSuccessStatusCode();
        Assert.Equal(pdf, await memberReport.Content.ReadAsByteArrayAsync());
        using (var document = PdfDocument.Open(pdf))
        {
            var text = string.Join('\n', document.GetPages().Select(page => page.Text));
            Assert.Contains("Ретро с итогами", text);
            Assert.Contains("Группа улучшений", text);
            Assert.Contains("Карточка участника", text);
            Assert.Contains("Нулевая тема", text);
            Assert.Contains("Решение по команде", text);
            Assert.Contains("Комментарий к теме", text);
            Assert.Contains("Задача после ретро", text);
            Assert.Contains("обсуждена", text);
            Assert.Equal(anonymousCards, !text.Contains("Автор:"));
            Assert.Equal(anonymousVotes, !text.Contains("Голос:"));
        }

        var replay = await CloseAsync(facilitator, setup.State.Id, request);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(pdf, await facilitator.GetByteArrayAsync($"/api/meetings/{setup.State.Id}/report.pdf"));
        var operationConflict = await CloseAsync(facilitator, setup.State.Id, new AdvanceMeetingRequest
        {
            FromPhase = "Discuss",
            ExpectedVersion = setup.State.Version + 100,
            OperationId = operationId,
        });
        Assert.Equal(HttpStatusCode.Conflict, operationConflict.StatusCode);
        var stale = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{setup.State.Id}/advance",
            new AdvanceMeetingRequest { FromPhase = "Vote", ExpectedVersion = 1, OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var outsider = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(outsider, "completion-outsider", "Посторонний");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/meetings/{setup.State.Id}/report.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await excluded.GetAsync($"/api/meetings/{setup.State.Id}/report.pdf")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/meetings/{setup.State.Id}/report.pdf")).StatusCode);
    }

    [Fact]
    public async Task PdfFailureRollsBackAndSuccessfulCompletionIsSingleAndReplayable()
    {
        using var factory = new TRetroWebApplicationFactory(failPdf: true);
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        using var excluded = factory.CreateClient();
        var setup = await CreateDiscussedMeetingAsync(facilitator, member, excluded, true, true, "Откат PDF");
        var request = new AdvanceMeetingRequest
        {
            FromPhase = "Discuss",
            ExpectedVersion = setup.State.Version,
            OperationId = Guid.NewGuid(),
        };
        var failed = await CloseAsync(facilitator, setup.State.Id, request);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var afterFailure = await StateAsync(facilitator, setup.State.Id);
        Assert.Equal("Discuss", afterFailure.Phase);
        Assert.Equal(3, await CountAsync(factory.DatabasePath, "card_authors"));
        Assert.Equal(3, await CountAsync(factory.DatabasePath, "votes"));
        Assert.Equal(0, await CountAsync(factory.DatabasePath, "meeting_totals"));
        Assert.Equal(0, await CountAsync(factory.DatabasePath, "topic_totals"));
        Assert.Equal(0, await CountAsync(factory.DatabasePath, "reports"));

        factory.FailingReportGenerator!.ShouldFail = false;
        (await CloseAsync(facilitator, setup.State.Id, request)).EnsureSuccessStatusCode();
        var first = await facilitator.GetByteArrayAsync($"/api/meetings/{setup.State.Id}/report.pdf");
        (await CloseAsync(facilitator, setup.State.Id, request)).EnsureSuccessStatusCode();
        Assert.Equal(first, await facilitator.GetByteArrayAsync($"/api/meetings/{setup.State.Id}/report.pdf"));
        Assert.Equal(1, await CountAsync(factory.DatabasePath, "meeting_totals"));
        Assert.Equal(2, await CountAsync(factory.DatabasePath, "topic_totals"));
        Assert.Equal(1, await CountAsync(factory.DatabasePath, "reports"));
    }

    [Fact]
    public async Task EmptyMeetingClosesWithZeroTotals()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "empty-completion", "Ведущий");
        var created = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Пустое ретро");
        await AdvanceAsync(facilitator, created.Id, "Draft");
        await AdvanceAsync(facilitator, created.Id, "Collect");
        await AdvanceAsync(facilitator, created.Id, "Group");
        var voting = await AdvanceAsync(facilitator, created.Id, "Vote");
        (await CloseAsync(facilitator, created.Id, new AdvanceMeetingRequest
        {
            FromPhase = "Discuss",
            ExpectedVersion = voting.Version,
            OperationId = Guid.NewGuid(),
        })).EnsureSuccessStatusCode();
        var closed = await StateAsync(facilitator, created.Id);
        Assert.Equal(0, closed.Totals!.CardCount);
        Assert.Equal(0, closed.Totals.TopicCount);
        Assert.Equal(0, closed.Totals.VoteCount);
        Assert.Empty(closed.Topics);
        Assert.Empty(closed.Discussion!.Queue);
        var pdf = await facilitator.GetByteArrayAsync($"/api/meetings/{created.Id}/report.pdf");
        using var document = PdfDocument.Open(pdf);
        Assert.Contains("Тем для обсуждения нет", string.Join('\n', document.GetPages().Select(page => page.Text)));
    }

    [Fact]
    public async Task LongCyrillicCardProducesReadableMultipagePdf()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        using var excluded = factory.CreateClient();
        var longText = "НАЧАЛО_ДЛИННОЙ_КАРТОЧКИ " + new string('я', 3500) + " КОНЕЦ_ДЛИННОЙ_КАРТОЧКИ";
        var setup = await CreateDiscussedMeetingAsync(
            facilitator,
            member,
            excluded,
            false,
            false,
            "Длинное ретро",
            longText);
        (await CloseAsync(facilitator, setup.State.Id, new AdvanceMeetingRequest
        {
            FromPhase = "Discuss",
            ExpectedVersion = setup.State.Version,
            OperationId = Guid.NewGuid(),
        })).EnsureSuccessStatusCode();
        var pdf = await facilitator.GetByteArrayAsync($"/api/meetings/{setup.State.Id}/report.pdf");
        using var document = PdfDocument.Open(pdf);
        Assert.True(document.NumberOfPages > 1);
        var text = string.Join('\n', document.GetPages().Select(page => page.Text));
        Assert.Contains("НАЧАЛО_ДЛИННОЙ_КАРТОЧКИ", text);
        Assert.Contains("КОНЕЦ_ДЛИННОЙ_КАРТОЧКИ", text);
        Assert.Contains("Решение по команде", text);
    }

    private static async Task<DiscussedMeeting> CreateDiscussedMeetingAsync(
        HttpClient facilitator,
        HttpClient member,
        HttpClient excluded,
        bool anonymousCards,
        bool anonymousVotes,
        string name,
        string? facilitatorCardText = null)
    {
        await ApiTestHelpers.LoginAsync(facilitator, "completion-facilitator", "Ведущий");
        var memberUser = await ApiTestHelpers.LoginAsync(member, "completion-member", "Участник");
        await ApiTestHelpers.LoginAsync(excluded, "completion-excluded", "Исключённый");
        var settings = ApiTestHelpers.DefaultMeeting(name);
        settings.AnonymousCards = anonymousCards;
        settings.AnonymousVotes = anonymousVotes;
        var createdResponse = await facilitator.PostAsJsonAsync("/api/meetings", settings);
        createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<MeetingCreatedResponse>())!;
        var invite = await facilitator.GetFromJsonAsync<InviteResponse>($"/api/meetings/{created.Id}/invite");
        foreach (var client in new[] { member, excluded })
        {
            (await client.PostAsJsonAsync("/api/join", new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url))))
                .EnsureSuccessStatusCode();
        }

        await AdvanceAsync(facilitator, created.Id, "Draft");
        var collected = await StateAsync(facilitator, created.Id);
        var facilitatorCard = await CreateCardAsync(
            facilitator,
            created.Id,
            collected.Columns[0].Id,
            facilitatorCardText ?? "Основная тема");
        var memberCard = await CreateCardAsync(member, created.Id, collected.Columns[0].Id, "Карточка участника");
        var excludedCard = await CreateCardAsync(excluded, created.Id, collected.Columns[1].Id, "Нулевая тема");
        await AdvanceAsync(facilitator, created.Id, "Collect");
        var grouped = await StateAsync(facilitator, created.Id);
        (await facilitator.PostAsJsonAsync($"/api/meetings/{created.Id}/moves", new MoveCardRequest
        {
            SourceKind = "card",
            SourceId = Guid.Parse(memberCard),
            TargetKind = "card",
            TargetId = Guid.Parse(facilitatorCard),
            ExpectedBoardVersion = grouped.BoardVersion,
            OperationId = Guid.NewGuid(),
        })).EnsureSuccessStatusCode();
        grouped = await StateAsync(facilitator, created.Id);
        (await facilitator.PutAsJsonAsync($"/api/meetings/{created.Id}/groups/{grouped.Groups.Single().Id}", new RenameGroupRequest
        {
            Name = "Группа улучшений",
            ExpectedBoardVersion = grouped.BoardVersion,
        })).EnsureSuccessStatusCode();
        await AdvanceAsync(facilitator, created.Id, "Group");
        var voting = await StateAsync(facilitator, created.Id);
        var groupTopic = voting.Topics.Single(topic => topic.Kind == "group");
        Assert.Contains(voting.Topics, topic => topic.SourceId == excludedCard);
        foreach (var client in new[] { facilitator, member, excluded })
        {
            (await client.PutAsJsonAsync($"/api/meetings/{created.Id}/topics/{groupTopic.Id}/my-votes", new SetMyVotesRequest
            {
                Count = 1,
                ExpectedVersion = 0,
            })).EnsureSuccessStatusCode();
        }

        (await facilitator.PostAsync($"/api/meetings/{created.Id}/members/{(await StateAsync(facilitator, created.Id)).Participants.Single(participant => participant.Subject == "completion-excluded").Id}/exclude", null))
            .EnsureSuccessStatusCode();
        await AdvanceAsync(facilitator, created.Id, "Vote");
        var discussed = await StateAsync(facilitator, created.Id);
        (await facilitator.PostAsJsonAsync($"/api/meetings/{created.Id}/topics/{groupTopic.Id}/comments", new CreateCommentRequest
        {
            Text = "Комментарий к теме",
            ExpectedVersion = discussed.Version,
            OperationId = Guid.NewGuid(),
        })).EnsureSuccessStatusCode();
        discussed = await StateAsync(facilitator, created.Id);
        (await facilitator.PostAsJsonAsync($"/api/meetings/{created.Id}/topics/{groupTopic.Id}/decisions", new CreateDecisionRequest
        {
            Text = "Решение по команде",
            ExpectedVersion = discussed.Version,
            OperationId = Guid.NewGuid(),
        })).EnsureSuccessStatusCode();
        discussed = await StateAsync(facilitator, created.Id);
        (await facilitator.PostAsJsonAsync($"/api/meetings/{created.Id}/topics/{groupTopic.Id}/actions", new CreateActionRequest
        {
            Text = "Задача после ретро",
            AssigneeId = Guid.Parse(memberUser.Id),
            ExpectedVersion = discussed.Version,
            OperationId = Guid.NewGuid(),
        })).EnsureSuccessStatusCode();
        discussed = await StateAsync(facilitator, created.Id);
        (await facilitator.PutAsJsonAsync($"/api/meetings/{created.Id}/topics/{groupTopic.Id}/status", new SetTopicStatusRequest
        {
            Status = "discussed",
            Advance = false,
            ExpectedVersion = discussed.Version,
        })).EnsureSuccessStatusCode();
        return new DiscussedMeeting(
            created.Id,
            await StateAsync(facilitator, created.Id),
            groupTopic.Id);
    }

    private static async Task<MeetingStateResponse> AdvanceAsync(HttpClient client, string meetingId, string phase)
    {
        var state = await StateAsync(client, meetingId);
        Assert.Equal(phase, state.Phase);
        (await client.PostAsJsonAsync($"/api/meetings/{meetingId}/advance", new AdvanceMeetingRequest
        {
            FromPhase = phase,
            ExpectedVersion = state.Version,
            OperationId = Guid.NewGuid(),
        })).EnsureSuccessStatusCode();
        return await StateAsync(client, meetingId);
    }

    private static Task<HttpResponseMessage> CloseAsync(
        HttpClient client,
        string meetingId,
        AdvanceMeetingRequest request) =>
        client.PostAsJsonAsync($"/api/meetings/{meetingId}/advance", request);

    private static async Task<string> CreateCardAsync(HttpClient client, string meetingId, string columnId, string text)
    {
        var response = await client.PostAsJsonAsync($"/api/meetings/{meetingId}/cards", new CreateCardRequest
        {
            ColumnId = Guid.Parse(columnId),
            Text = text,
            OperationId = Guid.NewGuid(),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardCreatedResponse>())!.Id;
    }

    private static async Task<MeetingStateResponse> StateAsync(HttpClient client, string meetingId) =>
        (await client.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meetingId}/state"))!;

    private static async Task<long> CountAsync(string databasePath, string table, string? where = null)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}{(where is null ? string.Empty : $" WHERE {where}")};";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> PayloadAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM meeting_totals;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record DiscussedMeeting(
        string MeetingId,
        MeetingStateResponse State,
        string GroupTopicId)
    {
        public string Id => MeetingId;
    }
}
