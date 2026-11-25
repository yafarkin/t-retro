using System.Net;
using System.Net.Http.Json;
using Api.Contracts;

namespace Api.Tests;

public sealed class GroupingApiTests
{
    [Fact]
    public async Task ParticipantsMoveAndRenameGroupsWithAtomicBoardVersions()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "group-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "group-member", "Участник");
        var meeting = await CreateMeetingInGroupAsync(facilitator, member);
        var initial = await StateAsync(facilitator, meeting.Id);
        var firstCard = await CreateCardAsync(facilitator, meeting.Id, initial.Columns[0].Id, "Первая");
        var secondCard = await CreateCardAsync(member, meeting.Id, initial.Columns[1].Id, "Вторая");
        await CreateCardAsync(facilitator, meeting.Id, initial.Columns[0].Id, "Третья");
        var collected = await StateAsync(member, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", collected.Version);
        var beforeMove = await StateAsync(member, meeting.Id);

        var operationId = Guid.NewGuid();
        var move = await MoveAsync(
            member,
            meeting.Id,
            "card",
            firstCard,
            "card",
            secondCard,
            beforeMove.BoardVersion,
            operationId);
        move.EnsureSuccessStatusCode();
        var moveResult = await move.Content.ReadFromJsonAsync<BoardVersionResponse>();
        var replay = await MoveAsync(
            member,
            meeting.Id,
            "card",
            firstCard,
            "card",
            secondCard,
            beforeMove.BoardVersion,
            operationId);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(moveResult, await replay.Content.ReadFromJsonAsync<BoardVersionResponse>());

        var grouped = await StateAsync(facilitator, meeting.Id);
        var group = Assert.Single(grouped.Groups);
        Assert.Equal(initial.Columns[1].Id, group.ColumnId);
        Assert.Equal("Вторая", group.Name);
        Assert.Equal(2, grouped.Cards.Count(card => card.GroupId == group.Id));

        var rename = await member.PutAsJsonAsync(
            $"/api/meetings/{meeting.Id}/groups/{group.Id}",
            new RenameGroupRequest { Name = "  Общая тема  ", ExpectedBoardVersion = grouped.BoardVersion });
        rename.EnsureSuccessStatusCode();
        var renamed = await StateAsync(member, meeting.Id);
        Assert.Equal("Общая тема", Assert.Single(renamed.Groups).Name);

        var extract = await MoveAsync(
            facilitator,
            meeting.Id,
            "card",
            firstCard,
            "column",
            initial.Columns[0].Id,
            renamed.BoardVersion,
            Guid.NewGuid());
        extract.EnsureSuccessStatusCode();
        var dissolved = await StateAsync(member, meeting.Id);
        Assert.Empty(dissolved.Groups);
        Assert.All(dissolved.Cards, card => Assert.Null(card.GroupId));

        var recreate = await MoveAsync(
            member,
            meeting.Id,
            "card",
            firstCard,
            "card",
            secondCard,
            dissolved.BoardVersion,
            Guid.NewGuid());
        recreate.EnsureSuccessStatusCode();
        var recreated = await StateAsync(facilitator, meeting.Id);
        var recreatedGroup = Assert.Single(recreated.Groups);
        var moveGroup = await MoveAsync(
            member,
            meeting.Id,
            "group",
            recreatedGroup.Id,
            "column",
            initial.Columns[0].Id,
            recreated.BoardVersion,
            Guid.NewGuid());
        moveGroup.EnsureSuccessStatusCode();
        var movedGroup = await StateAsync(facilitator, meeting.Id);
        Assert.Equal(initial.Columns[0].Id, Assert.Single(movedGroup.Groups).ColumnId);
        Assert.Equal(2, movedGroup.Cards.Count(card => card.GroupId == recreatedGroup.Id && card.ColumnId == initial.Columns[0].Id));
        Assert.Equal(initial.Columns[0].Color, movedGroup.Cards.Single(card => card.Id == firstCard).OriginalColumnColor);
        Assert.Equal(initial.Columns[1].Color, movedGroup.Cards.Single(card => card.Id == secondCard).OriginalColumnColor);

        var stale = await MoveAsync(
            facilitator,
            meeting.Id,
            "card",
            firstCard,
            "column",
            initial.Columns[1].Id,
            recreated.BoardVersion,
            Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var invalidGroupTarget = await MoveAsync(
            facilitator,
            meeting.Id,
            "group",
            recreatedGroup.Id,
            "card",
            firstCard,
            movedGroup.BoardVersion,
            Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, invalidGroupTarget.StatusCode);
        var selfMove = await MoveAsync(
            facilitator,
            meeting.Id,
            "card",
            firstCard,
            "card",
            firstCard,
            movedGroup.BoardVersion,
            Guid.NewGuid());
        selfMove.EnsureSuccessStatusCode();
        var afterSelfMove = await StateAsync(facilitator, meeting.Id);
        Assert.Equal(movedGroup.Version, afterSelfMove.Version);
        Assert.Equal(movedGroup.BoardVersion, afterSelfMove.BoardVersion);
    }

    [Fact]
    public async Task StubProposalAppliesOnceAndStaleProposalCannotOverwriteManualMove()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        using var member = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "ai-facilitator", "Ведущий");
        await ApiTestHelpers.LoginAsync(member, "ai-member", "Участник");
        var meeting = await CreateMeetingInGroupAsync(facilitator, member);
        var initial = await StateAsync(facilitator, meeting.Id);
        foreach (var column in initial.Columns.Take(2))
        {
            for (var index = 0; index < 2; index++)
            {
                await CreateCardAsync(facilitator, meeting.Id, column.Id, $"{column.Name} {index + 1}");
            }
        }

        var collected = await StateAsync(facilitator, meeting.Id);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", collected.Version);
        var forbidden = await member.PostAsync($"/api/meetings/{meeting.Id}/ai/preview", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var preview = await facilitator.PostAsync($"/api/meetings/{meeting.Id}/ai/preview", null);
        preview.EnsureSuccessStatusCode();
        var proposal = await preview.Content.ReadFromJsonAsync<GroupProposalResponse>();
        Assert.NotNull(proposal);
        Assert.Equal(2, proposal.Groups.Count);
        Assert.All(proposal.Groups, group => Assert.Equal(2, group.CardIds.Count));
        Assert.All(proposal.Groups, group => Assert.StartsWith("Тестовая группа", group.Name));
        Assert.Empty((await StateAsync(facilitator, meeting.Id)).Groups);

        var operationId = Guid.NewGuid();
        var apply = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/ai/apply",
            new ApplyGroupProposalRequest
            {
                ProposalId = Guid.Parse(proposal.Id),
                ExpectedBoardVersion = proposal.BoardVersion,
                OperationId = operationId,
            });
        apply.EnsureSuccessStatusCode();
        var applyResult = await apply.Content.ReadFromJsonAsync<BoardVersionResponse>();
        var replay = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/ai/apply",
            new ApplyGroupProposalRequest
            {
                ProposalId = Guid.Parse(proposal.Id),
                ExpectedBoardVersion = proposal.BoardVersion,
                OperationId = operationId,
            });
        replay.EnsureSuccessStatusCode();
        Assert.Equal(applyResult, await replay.Content.ReadFromJsonAsync<BoardVersionResponse>());
        var applied = await StateAsync(facilitator, meeting.Id);
        Assert.Equal(2, applied.Groups.Count);
        Assert.Equal(4, applied.Cards.Count(card => card.GroupId is not null));

        var firstGroup = applied.Groups.Single(group => group.ColumnId == initial.Columns[0].Id);
        var manualMove = await MoveAsync(
            facilitator,
            meeting.Id,
            "group",
            firstGroup.Id,
            "column",
            initial.Columns[1].Id,
            applied.BoardVersion,
            Guid.NewGuid());
        manualMove.EnsureSuccessStatusCode();
        var manuallyMoved = await StateAsync(facilitator, meeting.Id);
        var staleApply = await facilitator.PostAsJsonAsync(
            $"/api/meetings/{meeting.Id}/ai/apply",
            new ApplyGroupProposalRequest
            {
                ProposalId = Guid.Parse(proposal.Id),
                ExpectedBoardVersion = manuallyMoved.BoardVersion,
                OperationId = Guid.NewGuid(),
            });
        Assert.Equal(HttpStatusCode.Conflict, staleApply.StatusCode);
        var afterStaleApply = await StateAsync(facilitator, meeting.Id);
        Assert.Equal(initial.Columns[1].Id, afterStaleApply.Groups.Single(group => group.Id == firstGroup.Id).ColumnId);
        Assert.Equal(manuallyMoved.Version, afterStaleApply.Version);
    }

    [Fact]
    public async Task DisabledStubIsVisibleAndReturnsIntegrationUnavailable()
    {
        using var factory = new TRetroWebApplicationFactory(aiEnabled: false);
        await ApiTestHelpers.MigrateAsync(factory);
        using var facilitator = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(facilitator, "disabled-ai", "Ведущий");
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator);
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        await AdvanceAsync(facilitator, meeting.Id, "Collect", 2);

        var state = await StateAsync(facilitator, meeting.Id);
        Assert.False(state.Integrations.Ai);
        var response = await facilitator.PostAsync($"/api/meetings/{meeting.Id}/ai/preview", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("IntegrationUnavailable", error!.Code);
    }

    private static async Task<MeetingCreatedResponse> CreateMeetingInGroupAsync(
        HttpClient facilitator,
        HttpClient member)
    {
        var meeting = await ApiTestHelpers.CreateMeetingAsync(facilitator, "Ретро M4");
        var invite = await facilitator.GetFromJsonAsync<InviteResponse>($"/api/meetings/{meeting.Id}/invite");
        var join = await member.PostAsJsonAsync("/api/join", new JoinRequest(ApiTestHelpers.TokenFromUrl(invite!.Url)));
        join.EnsureSuccessStatusCode();
        await AdvanceAsync(facilitator, meeting.Id, "Draft", 1);
        return meeting;
    }

    private static async Task<string> CreateCardAsync(HttpClient client, string meetingId, string columnId, string text)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/cards",
            new CreateCardRequest { ColumnId = Guid.Parse(columnId), Text = text, OperationId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardCreatedResponse>())!.Id;
    }

    private static Task<HttpResponseMessage> MoveAsync(
        HttpClient client,
        string meetingId,
        string sourceKind,
        string sourceId,
        string targetKind,
        string targetId,
        int expectedBoardVersion,
        Guid operationId) =>
        client.PostAsJsonAsync(
            $"/api/meetings/{meetingId}/moves",
            new MoveCardRequest
            {
                SourceKind = sourceKind,
                SourceId = Guid.Parse(sourceId),
                TargetKind = targetKind,
                TargetId = Guid.Parse(targetId),
                ExpectedBoardVersion = expectedBoardVersion,
                OperationId = operationId,
            });

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
