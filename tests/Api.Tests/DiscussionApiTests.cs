using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Microsoft.Data.Sqlite;

namespace Api.Tests;

public sealed class DiscussionApiTests
{
    [Fact]
    public async Task QueueIncludesZeroVotesAndNavigationIsShared()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "discussion-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "discussion-member", "Участник");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Ретро M6");
        await JoinAsync(facilitator, member, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var columns = (await StateAsync(facilitator, meeting.Id)).Columns;
        var first = await CreateCardAsync(facilitator, meeting.Id, columns[0].Id, "Два голоса в первой колонке");
        var second = await CreateCardAsync(facilitator, meeting.Id, columns[1].Id, "Два голоса во второй колонке");
        var zero = await CreateCardAsync(facilitator, meeting.Id, columns[0].Id, "Нулевая тема");
        await AdvanceToVoteAsync(facilitator, meeting.Id);
        var voting = await StateAsync(facilitator, meeting.Id);
        var firstTopic = voting.Topics.Single(topic => topic.SourceId == first);
        var secondTopic = voting.Topics.Single(topic => topic.SourceId == second);
        var zeroTopic = voting.Topics.Single(topic => topic.SourceId == zero);
        (await SetVotesAsync(member, meeting.Id, firstTopic.Id, 2, 0)).EnsureSuccessStatusCode();
        (await SetVotesAsync(facilitator, meeting.Id, secondTopic.Id, 2, 0)).EnsureSuccessStatusCode();
        voting = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Vote", voting.Version);

        var initial = await StateAsync(member, meeting.Id);
        Assert.Equal("Discuss", initial.Phase);
        Assert.Equal(
            [firstTopic.Id, secondTopic.Id, zeroTopic.Id],
            initial.Discussion!.Queue.Select(topic => topic.TopicId).ToArray());
        Assert.All(initial.Discussion.Queue, topic => Assert.Equal("pending", topic.Status));
        Assert.Equal(firstTopic.Id, initial.Discussion.CurrentTopicId);
        Assert.Equal(0, initial.Topics.Single(topic => topic.Id == zeroTopic.Id).TotalVotes);

        var forbidden = await member.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/discussion/current",
            new SelectCurrentTopicRequest { TopicId = Guid.Parse(zeroTopic.Id), ExpectedVersion = initial.Version });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        (await SelectCurrentAsync(facilitator, meeting.Id, zeroTopic.Id, initial.Version)).EnsureSuccessStatusCode();
        var selected = await StateAsync(member, meeting.Id);
        Assert.Equal(zeroTopic.Id, selected.Discussion!.CurrentTopicId);
        (await SetStatusAsync(facilitator, meeting.Id, zeroTopic.Id, "skipped", false, selected.Version))
            .EnsureSuccessStatusCode();
        selected = await StateAsync(member, meeting.Id);
        Assert.Equal("skipped", selected.Discussion!.Queue.Single(topic => topic.TopicId == zeroTopic.Id).Status);
        Assert.Equal(zeroTopic.Id, selected.Discussion.CurrentTopicId);

        (await SelectCurrentAsync(facilitator, meeting.Id, firstTopic.Id, selected.Version)).EnsureSuccessStatusCode();
        selected = await StateAsync(facilitator, meeting.Id);
        var invalidNext = await SetStatusAsync(facilitator, meeting.Id, secondTopic.Id, "discussed", true, selected.Version);
        Assert.Equal(HttpStatusCode.Conflict, invalidNext.StatusCode);
        (await SetStatusAsync(facilitator, meeting.Id, firstTopic.Id, "discussed", true, selected.Version))
            .EnsureSuccessStatusCode();
        selected = await StateAsync(member, meeting.Id);
        Assert.Equal(secondTopic.Id, selected.Discussion!.CurrentTopicId);
        (await SetStatusAsync(facilitator, meeting.Id, secondTopic.Id, "skipped", true, selected.Version))
            .EnsureSuccessStatusCode();
        selected = await StateAsync(member, meeting.Id);
        Assert.Equal(zeroTopic.Id, selected.Discussion!.CurrentTopicId);
        (await SetStatusAsync(facilitator, meeting.Id, zeroTopic.Id, "discussed", true, selected.Version))
            .EnsureSuccessStatusCode();
        selected = await StateAsync(member, meeting.Id);
        Assert.Null(selected.Discussion!.CurrentTopicId);

        (await SelectCurrentAsync(facilitator, meeting.Id, firstTopic.Id, selected.Version)).EnsureSuccessStatusCode();
        var returned = await StateAsync(member, meeting.Id);
        Assert.Equal(firstTopic.Id, returned.Discussion!.CurrentTopicId);
        (await SetStatusAsync(facilitator, meeting.Id, firstTopic.Id, "pending", false, returned.Version))
            .EnsureSuccessStatusCode();
        var reopened = await StateAsync(member, meeting.Id);
        Assert.Equal("pending", reopened.Discussion!.Queue.Single(topic => topic.TopicId == firstTopic.Id).Status);
    }

    [Fact]
    public async Task CommentsDecisionsAndActionsAreSeparateAndKeepHistoricalAssignee()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "entries-facilitator", "Ведущий");
        var memberUser = await ApiTestHelpers.LoginAsync(member, "entries-member", "Исполнитель");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Сущности M6");
        await JoinAsync(facilitator, member, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var columnId = (await StateAsync(facilitator, meeting.Id)).Columns[0].Id;
        await CreateCardAsync(facilitator, meeting.Id, columnId, "Тема");
        await AdvanceToVoteAsync(facilitator, meeting.Id);
        var voting = await StateAsync(facilitator, meeting.Id);
        var topicId = voting.Topics.Single().Id;
        await AdvanceAsync(facilitator, meeting.Id, "Vote", voting.Version);
        var initial = await StateAsync(facilitator, meeting.Id);

        var forbidden = await member.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/topics/{topicId}/decisions",
            new CreateDecisionRequest
            {
                Text = "Решение участника",
                ExpectedVersion = initial.Version,
                OperationId = Guid.NewGuid(),
            });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var empty = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/topics/{topicId}/comments",
            new CreateCommentRequest
            {
                Text = " ",
                ExpectedVersion = initial.Version,
                OperationId = Guid.NewGuid(),
            });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var operationId = Guid.NewGuid();
        var commentRequest = new CreateCommentRequest
        {
            Text = "  Комментарий  ",
            ExpectedVersion = initial.Version,
            OperationId = operationId,
        };
        var createdComment = await member.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/topics/{topicId}/comments", commentRequest);
        createdComment.EnsureSuccessStatusCode();
        var comment = (await createdComment.Content.ReadFromJsonAsync<DiscussionEntryCreatedResponse>())!;
        var replay = await member.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/topics/{topicId}/comments", commentRequest);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(comment, await replay.Content.ReadFromJsonAsync<DiscussionEntryCreatedResponse>());
        var conflicting = await member.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/topics/{topicId}/comments",
            new CreateCommentRequest
            {
                Text = "Другой текст",
                ExpectedVersion = initial.Version,
                OperationId = operationId,
            });
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        var afterComment = await StateAsync(facilitator, meeting.Id);
        var decision = await CreateDecisionAsync(facilitator, meeting.Id, topicId, "Решение", afterComment.Version);
        var afterDecision = await StateAsync(member, meeting.Id);
        Assert.Equal("Комментарий", Assert.Single(afterDecision.Discussion!.Comments).Text);
        Assert.Equal("Решение", Assert.Single(afterDecision.Discussion.Decisions).Text);
        Assert.DoesNotContain(afterDecision.Discussion.Comments, item => item.Text == "Решение");
        Assert.DoesNotContain(afterDecision.Discussion.Decisions, item => item.Text == "Комментарий");

        var staleUpdate = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/comments/{comment.Id}",
            new UpdateCommentRequest { Text = "Устаревшее", ExpectedVersion = initial.Version });
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        (await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/comments/{comment.Id}",
            new UpdateCommentRequest { Text = "  Изменённый комментарий  ", ExpectedVersion = afterDecision.Version }))
            .EnsureSuccessStatusCode();
        var afterUpdate = await StateAsync(facilitator, meeting.Id);
        Assert.Equal("Изменённый комментарий", afterUpdate.Discussion!.Comments.Single().Text);

        var actionWithoutAssignee = await CreateActionAsync(
            facilitator, meeting.Id, topicId, "Задача без исполнителя", null, afterUpdate.Version);
        var afterFirstAction = await StateAsync(facilitator, meeting.Id);
        var actionWithAssignee = await CreateActionAsync(
            member, meeting.Id, topicId, "Задача с исполнителем", memberUser.Id, afterFirstAction.Version, "  Детали задачи  ");
        var afterActions = await StateAsync(member, meeting.Id);
        Assert.Equal(2, afterActions.Discussion!.Actions.Count);
        var assigned = afterActions.Discussion.Actions.Single(action => action.Id == actionWithAssignee.Id);
        Assert.Equal("Детали задачи", assigned.Description);
        Assert.Equal(actionWithAssignee.Version, assigned.CreatedVersion);
        Assert.Equal(comment.Version, Assert.Single(afterActions.Discussion.Comments).CreatedVersion);
        Assert.Null(afterActions.Discussion.Actions.Single(action => action.Id == actionWithoutAssignee.Id).Description);
        Assert.All(afterActions.Discussion.Actions, action => Assert.Equal("notExported", action.ExportStatus));
        var invalidAssignee = await CreateActionResponseAsync(
            facilitator, meeting.Id, topicId, "Неверный исполнитель", Guid.NewGuid().ToString(), afterActions.Version);
        Assert.Equal(HttpStatusCode.BadRequest, invalidAssignee.StatusCode);

        var excluded = await facilitator.PostAsync($"/api/meetings/{meeting.Id}/members/{memberUser.Id}/exclude", null);
        excluded.EnsureSuccessStatusCode();
        var afterExclusion = await StateAsync(facilitator, meeting.Id);
        Assert.Equal(
            memberUser.Id,
            afterExclusion.Discussion!.Actions.Single(action => action.Id == actionWithAssignee.Id).AssigneeId);
        (await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/actions/{actionWithAssignee.Id}",
            new UpdateActionRequest
            {
                Text = "Изменено после исключения",
                Description = "Обновлённые детали",
                AssigneeId = Guid.Parse(memberUser.Id),
                ExpectedVersion = afterExclusion.Version,
            })).EnsureSuccessStatusCode();
        var afterHistoricalEdit = await StateAsync(facilitator, meeting.Id);
        var edited = afterHistoricalEdit.Discussion!.Actions.Single(action => action.Id == actionWithAssignee.Id);
        Assert.Equal("Обновлённые детали", edited.Description);
        Assert.Equal(assigned.CreatedVersion, edited.CreatedVersion);
        var excludedComment = await member.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/topics/{topicId}/comments",
            new CreateCommentRequest
            {
                Text = "Комментарий после исключения",
                ExpectedVersion = afterHistoricalEdit.Version,
                OperationId = Guid.NewGuid(),
            });
        Assert.Equal(HttpStatusCode.Forbidden, excludedComment.StatusCode);
        var excludedTask = await CreateActionResponseAsync(
            member, meeting.Id, topicId, "Задача после исключения", null, afterHistoricalEdit.Version);
        Assert.Equal(HttpStatusCode.Forbidden, excludedTask.StatusCode);
        var newExcludedAssignment = await CreateActionResponseAsync(
            facilitator,
            meeting.Id,
            topicId,
            "Новое назначение исключённому",
            memberUser.Id,
            afterHistoricalEdit.Version);
        Assert.Equal(HttpStatusCode.BadRequest, newExcludedAssignment.StatusCode);
        var reassignment = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/actions/{actionWithoutAssignee.Id}",
            new UpdateActionRequest
            {
                Text = "Задача без исполнителя",
                AssigneeId = Guid.Parse(memberUser.Id),
                ExpectedVersion = afterHistoricalEdit.Version,
            });
        Assert.Equal(HttpStatusCode.BadRequest, reassignment.StatusCode);

        var current = await StateAsync(facilitator, meeting.Id);
        (await facilitator.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/meetings/{meeting.Id}/decisions/{decision.Id}")
        {
            Content = JsonContent.Create(new DeleteDiscussionEntryRequest { ExpectedVersion = current.Version }),
        })).EnsureSuccessStatusCode();
        current = await StateAsync(facilitator, meeting.Id);
        (await facilitator.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/meetings/{meeting.Id}/comments/{comment.Id}")
        {
            Content = JsonContent.Create(new DeleteDiscussionEntryRequest { ExpectedVersion = current.Version }),
        })).EnsureSuccessStatusCode();
        current = await StateAsync(facilitator, meeting.Id);
        Assert.Empty(current.Discussion!.Comments);
        Assert.Empty(current.Discussion.Decisions);
        Assert.Equal(2, current.Discussion.Actions.Count);

        await using var connection = new SqliteConnection($"Data Source={factory.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM comments), (SELECT COUNT(*) FROM decisions), (SELECT COUNT(*) FROM actions);";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
        Assert.Equal(2L, reader.GetInt64(2));
    }

    [Fact]
    public async Task EmptyDiscussionHasNoCurrentTopic()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "empty-discussion", "Ведущий");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Пустое обсуждение");
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", 2);
        await AdvanceAsync(facilitator, meeting.Id, "Group", 3);
        await AdvanceAsync(facilitator, meeting.Id, "Vote", 4);

        var state = await StateAsync(facilitator, meeting.Id);
        Assert.Empty(state.Discussion!.Queue);
        Assert.Null(state.Discussion.CurrentTopicId);
        Assert.Empty(state.Discussion.Comments);
        Assert.Empty(state.Discussion.Decisions);
        Assert.Empty(state.Discussion.Actions);
    }

    private static async Task AdvanceToVoteAsync(HttpClient facilitator, string meetingId)
    {
        var collected = await StateAsync(facilitator, meetingId);
        await AdvanceAsync(facilitator, meetingId, "Collect", collected.Version);
        var grouped = await StateAsync(facilitator, meetingId);
        await AdvanceAsync(facilitator, meetingId, "Group", grouped.Version);
    }

    private static Task<HttpResponseMessage> SelectCurrentAsync(
        HttpClient client,
        string meetingId,
        string topicId,
        int expectedVersion) =>
        client.PutAsJsonAsync(
            $"/api/meetings/{meetingId}/discussion/current",
            new SelectCurrentTopicRequest { TopicId = Guid.Parse(topicId), ExpectedVersion = expectedVersion });

    private static Task<HttpResponseMessage> SetStatusAsync(
        HttpClient client,
        string meetingId,
        string topicId,
        string status,
        bool advance,
        int expectedVersion) =>
        client.PutAsJsonAsync(
            $"/api/meetings/{meetingId}/topics/{topicId}/status",
            new SetTopicStatusRequest { Status = status, Advance = advance, ExpectedVersion = expectedVersion });

    private static async Task<DiscussionEntryCreatedResponse> CreateDecisionAsync(
        HttpClient client,
        string meetingId,
        string topicId,
        string text,
        int expectedVersion)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/topics/{topicId}/decisions",
            new CreateDecisionRequest { Text = text, ExpectedVersion = expectedVersion, OperationId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DiscussionEntryCreatedResponse>())!;
    }

    private static async Task<DiscussionEntryCreatedResponse> CreateActionAsync(
        HttpClient client,
        string meetingId,
        string topicId,
        string text,
        string? assigneeId,
        int expectedVersion,
        string? description = null)
    {
        var response = await CreateActionResponseAsync(client, meetingId, topicId, text, assigneeId, expectedVersion, description);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DiscussionEntryCreatedResponse>())!;
    }

    private static Task<HttpResponseMessage> CreateActionResponseAsync(
        HttpClient client,
        string meetingId,
        string topicId,
        string text,
        string? assigneeId,
        int expectedVersion,
        string? description = null) =>
        client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/topics/{topicId}/actions",
            new CreateActionRequest
            {
                Text = text,
                Description = description,
                AssigneeId = assigneeId is null ? null : Guid.Parse(assigneeId),
                ExpectedVersion = expectedVersion,
                OperationId = Guid.NewGuid(),
            });

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
