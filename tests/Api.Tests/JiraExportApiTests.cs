using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Application;
using Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

public sealed class JiraExportApiTests
{
    [Fact]
    public async Task CreatedExportIsIdempotentAndLocksTask()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        var (meetingId, actionId) = await CreateActionAsync(factory, facilitator, member);

        var forbidden = await member.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var created = await facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        created.EnsureSuccessStatusCode();
        var result = (await created.Content.ReadFromJsonAsync<JiraExportResponse>())!;
        Assert.Equal("created", result.Status);
        Assert.StartsWith("MOCK-", result.IssueKey);
        Assert.StartsWith("https://jira.local.invalid/", result.IssueUrl);
        var externalRequestId = result.ExternalRequestId;
        var version = result.Version;

        var memberState = await StateAsync(member, meetingId);
        var action = memberState.Discussion!.Actions.Single();
        Assert.Equal("created", action.ExportStatus);
        Assert.Equal(externalRequestId, action.ExternalRequestId);
        Assert.Equal(result.IssueKey, action.IssueKey);
        Assert.Equal(result.IssueUrl, action.IssueUrl);
        var repeated = await facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        repeated.EnsureSuccessStatusCode();
        var replay = (await repeated.Content.ReadFromJsonAsync<JiraExportResponse>())!;
        Assert.Equal(result, replay);
        Assert.Equal(version, replay.Version);
        var exporter = (MockJiraIssueExporter)factory.Services.GetRequiredService<IJiraIssueExporter>();
        Assert.Equal(1, exporter.CreateCallCount);

        var updated = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meetingId}/actions/{actionId}",
            new UpdateActionRequest { Text = "Недопустимое изменение", ExpectedVersion = memberState.Version });
        Assert.Equal(HttpStatusCode.Conflict, updated.StatusCode);
        var deleted = await facilitator.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/meetings/{meetingId}/actions/{actionId}")
        {
            Content = JsonContent.Create(new DeleteDiscussionEntryRequest { ExpectedVersion = memberState.Version }),
        });
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        var reconciled = await exporter.ReconcileAsync(Guid.Parse(externalRequestId), CancellationToken.None);
        Assert.Equal(JiraExportResultStatus.Created, reconciled.Status);
        Assert.Equal(result.IssueKey, reconciled.IssueKey);
    }

    [Fact]
    public async Task PendingExportReturnsCurrentStateWithoutSecondPostAndLocksTask()
    {
        using var factory = new TRetroWebApplicationFactory(jiraDelayMs: 600);
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        var (meetingId, actionId) = await CreateActionAsync(factory, facilitator, member);

        var first = facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        var pendingState = await WaitForActionStatusAsync(facilitator, meetingId, "pending");
        var repeated = await facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        repeated.EnsureSuccessStatusCode();
        var pending = (await repeated.Content.ReadFromJsonAsync<JiraExportResponse>())!;
        Assert.Equal("pending", pending.Status);
        var locked = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meetingId}/actions/{actionId}",
            new UpdateActionRequest { Text = "Изменение во время отправки", ExpectedVersion = pendingState.Version });
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);

        var completed = await first;
        completed.EnsureSuccessStatusCode();
        Assert.Equal("created", (await completed.Content.ReadFromJsonAsync<JiraExportResponse>())!.Status);
        var exporter = (MockJiraIssueExporter)factory.Services.GetRequiredService<IJiraIssueExporter>();
        Assert.Equal(1, exporter.CreateCallCount);
        Assert.Equal("created", (await StateAsync(member, meetingId)).Discussion!.Actions.Single().ExportStatus);
    }

    [Theory]
    [InlineData("Rejected", "rejected")]
    [InlineData("Unavailable", "unavailable")]
    public async Task ConfirmedFailureAllowsEditingButRepeatDoesNotPostAgain(string outcome, string expectedStatus)
    {
        using var factory = new TRetroWebApplicationFactory(jiraOutcome: outcome);
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        var (meetingId, actionId) = await CreateActionAsync(factory, facilitator, member);

        var response = await facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<JiraExportResponse>())!;
        Assert.Equal(expectedStatus, result.Status);
        var repeated = await facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        Assert.Equal(result, await repeated.Content.ReadFromJsonAsync<JiraExportResponse>());
        var state = await StateAsync(facilitator, meetingId);
        var updated = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meetingId}/actions/{actionId}",
            new UpdateActionRequest { Text = "Изменено после отказа", ExpectedVersion = state.Version });
        updated.EnsureSuccessStatusCode();
        Assert.Equal("Изменено после отказа", (await StateAsync(member, meetingId)).Discussion!.Actions.Single().Text);
        var exporter = (MockJiraIssueExporter)factory.Services.GetRequiredService<IJiraIssueExporter>();
        Assert.Equal(1, exporter.CreateCallCount);
    }

    [Fact]
    public async Task UnknownRequiresReconciliationAndBlocksRepeatAndEditing()
    {
        using var factory = new TRetroWebApplicationFactory(jiraOutcome: "Unknown");
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        var (meetingId, actionId) = await CreateActionAsync(factory, facilitator, member);

        var response = await facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<JiraExportResponse>())!;
        Assert.Equal("unknown", result.Status);
        var repeated = await facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        Assert.Equal(result, await repeated.Content.ReadFromJsonAsync<JiraExportResponse>());
        var state = await StateAsync(facilitator, meetingId);
        Assert.Equal("unknown", state.Discussion!.Actions.Single().ExportStatus);
        var updated = await facilitator.PutAsJsonAsync(
            $"/api/meetings/{meetingId}/actions/{actionId}",
            new UpdateActionRequest { Text = "Небезопасное изменение", ExpectedVersion = state.Version });
        Assert.Equal(HttpStatusCode.Conflict, updated.StatusCode);
        var deleted = await facilitator.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/meetings/{meetingId}/actions/{actionId}")
        {
            Content = JsonContent.Create(new DeleteDiscussionEntryRequest { ExpectedVersion = state.Version }),
        });
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        var exporter = (MockJiraIssueExporter)factory.Services.GetRequiredService<IJiraIssueExporter>();
        Assert.Equal(1, exporter.CreateCallCount);
        var reconciliation = await exporter.ReconcileAsync(Guid.Parse(result.ExternalRequestId), CancellationToken.None);
        Assert.Equal(JiraExportResultStatus.Unknown, reconciliation.Status);
    }

    [Fact]
    public async Task DisabledMockDoesNotCreateIntention()
    {
        using var factory = new TRetroWebApplicationFactory(jiraEnabled: false);
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        var (meetingId, actionId) = await CreateActionAsync(factory, facilitator, member);
        Assert.False((await StateAsync(member, meetingId)).Integrations.Jira);

        var response = await facilitator.PostAsync($"/api/meetings/{meetingId}/actions/{actionId}/export", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("IntegrationUnavailable", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        await using var connection = factory.Database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM exports;";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static async Task<(string MeetingId, string ActionId)> CreateActionAsync(
        TRetroWebApplicationFactory factory,
        HttpClient facilitator,
        HttpClient member)
    {
        await ApiTestHelpers.LoginAsync(facilitator, "jira-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "jira-member", "Участник");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Mock Jira M8");
        var invite = await facilitator.GetFromJsonAsync<InviteResponse>($"/api/meetings/{meeting.Id}/invite");
        (await member.PostAsJsonAsync("/api/join", new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url))))
            .EnsureSuccessStatusCode();
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        var columnId = (await StateAsync(facilitator, meeting.Id)).Columns[0].Id;
        var cardResponse = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = "Тема экспорта", OperationId = Guid.NewGuid() });
        cardResponse.EnsureSuccessStatusCode();
        var collected = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", collected.Version);
        var grouped = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Group", grouped.Version);
        var voting = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Vote", voting.Version);
        var discussion = await StateAsync(facilitator, meeting.Id);
        var topicId = discussion.Discussion!.Queue.Single().TopicId;
        var created = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/topics/{topicId}/actions",
            new CreateActionRequest
            {
                Text = "Экспортируемая задача",
                ExpectedVersion = discussion.Version,
                OperationId = Guid.NewGuid(),
            });
        created.EnsureSuccessStatusCode();
        var action = (await created.Content.ReadFromJsonAsync<DiscussionEntryCreatedResponse>())!;
        return (meeting.Id, action.Id);
    }

    private static async Task<MeetingStateResponse> WaitForActionStatusAsync(
        HttpClient client,
        string meetingId,
        string status)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var state = await StateAsync(client, meetingId);
            if (state.Discussion!.Actions.Single().ExportStatus == status)
            {
                return state;
            }
            await Task.Delay(50);
        }

        throw new TimeoutException("Expected export status was not observed.");
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
