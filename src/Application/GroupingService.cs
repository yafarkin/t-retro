using Domain;

namespace Application;

public sealed class GroupingService(
    IGroupingRepository groups,
    MeetingAccessService access,
    IGroupSuggestionProvider suggestions,
    IMeetingNotifier notifier,
    TimeProvider timeProvider)
{
    public async Task<MeetingCommandResult> MoveAsync(
        Guid userId,
        Guid meetingId,
        string sourceKind,
        Guid sourceId,
        string targetKind,
        Guid targetId,
        int expectedBoardVersion,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        var parsedSource = ParseKind(sourceKind);
        var parsedTarget = ParseKind(targetKind);
        await access.GetAsync(userId, meetingId, false, cancellationToken);
        var result = await groups.MoveAsync(
            meetingId,
            userId,
            parsedSource,
            sourceId,
            parsedTarget,
            targetId,
            expectedBoardVersion,
            operationId,
            CommandFingerprint.Create(parsedSource, sourceId, parsedTarget, targetId, expectedBoardVersion),
            Guid.NewGuid(),
            timeProvider.GetUtcNow(),
            cancellationToken);
        return await CompleteAsync(meetingId, result, true, cancellationToken);
    }

    public async Task<MeetingCommandResult> RenameGroupAsync(
        Guid userId,
        Guid meetingId,
        Guid groupId,
        string? name,
        int expectedBoardVersion,
        CancellationToken cancellationToken)
    {
        var group = BoardGroup.Create(groupId, Guid.Empty, name);
        await access.GetAsync(userId, meetingId, false, cancellationToken);
        var result = await groups.RenameGroupAsync(
            meetingId,
            userId,
            groupId,
            group.Name,
            expectedBoardVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return await CompleteAsync(meetingId, result, true, cancellationToken);
    }

    public async Task<GroupProposal> PreviewAsync(
        Guid userId,
        Guid meetingId,
        CancellationToken cancellationToken)
    {
        if (!suggestions.IsAvailable)
        {
            throw new IntegrationUnavailableException("Предложения группировки отключены в конфигурации.");
        }

        var snapshot = await access.GetAsync(userId, meetingId, true, cancellationToken);
        if (snapshot.Meeting.Phase != MeetingPhase.Group)
        {
            throw new ConflictException("InvalidPhase", "Предложения доступны только во время группировки.");
        }

        var request = new GroupSuggestionRequest(
            snapshot.Columns.Select(column => new GroupSuggestionColumn(column.Id, column.Name)).ToArray(),
            snapshot.Cards
                .Where(card => card.GroupId is null)
                .Select(card => new GroupSuggestionCard(card.Id, card.ColumnId, card.Text!))
                .ToArray());
        var proposedGroups = await suggestions.SuggestGroupsAsync(request, cancellationToken);
        var structure = ToStructure(snapshot);
        try
        {
            structure.ApplySuggestions(proposedGroups, Guid.NewGuid);
        }
        catch (KeyNotFoundException)
        {
            throw new DomainValidationException("InvalidAiResponse", "Предложение содержит неизвестные идентификаторы.");
        }

        var proposal = new GroupProposal(Guid.NewGuid(), snapshot.Meeting.BoardVersion, proposedGroups);
        var result = await groups.SaveProposalAsync(
            meetingId,
            userId,
            proposal,
            timeProvider.GetUtcNow(),
            cancellationToken);
        await CompleteAsync(meetingId, result, false, cancellationToken);
        return proposal;
    }

    public async Task<MeetingCommandResult> ApplyProposalAsync(
        Guid userId,
        Guid meetingId,
        Guid proposalId,
        int expectedBoardVersion,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        await access.GetAsync(userId, meetingId, true, cancellationToken);
        var result = await groups.ApplyProposalAsync(
            meetingId,
            userId,
            proposalId,
            expectedBoardVersion,
            operationId,
            CommandFingerprint.Create(proposalId, expectedBoardVersion),
            timeProvider.GetUtcNow(),
            cancellationToken);
        return await CompleteAsync(meetingId, result, true, cancellationToken);
    }

    private async Task<MeetingCommandResult> CompleteAsync(
        Guid meetingId,
        MeetingCommandResult result,
        bool notify,
        CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case MeetingCommandStatus.NotFound:
                throw new NotFoundException("Ресурс не найден.");
            case MeetingCommandStatus.Forbidden:
                throw new ForbiddenException("Forbidden", "Недостаточно прав для этого действия.");
            case MeetingCommandStatus.InvalidPhase:
                throw new ConflictException("InvalidPhase", "Действие доступно только во время группировки.");
            case MeetingCommandStatus.VersionConflict:
                throw new ConflictException("BoardVersionConflict", "Доска изменилась. Обновите состояние и повторите действие.");
            case MeetingCommandStatus.OperationConflict:
                throw new ConflictException("OperationConflict", "Идентификатор операции уже использован для другой команды.");
        }

        if (notify && !result.IsReplay)
        {
            await notifier.StateChangedAsync(meetingId, result.Version, cancellationToken);
        }

        return result;
    }

    private static BoardItemKind ParseKind(string value) =>
        Enum.TryParse<BoardItemKind>(value, true, out var kind)
            ? kind
            : throw new DomainValidationException("InvalidBoardItemKind", "Неизвестный тип объекта доски.");

    private static BoardStructure ToStructure(MeetingSnapshot snapshot) =>
        new(
            snapshot.Columns.Select(column => column.Id).ToArray(),
            snapshot.Cards.Select(card => new BoardCard(card.Id, card.ColumnId, card.GroupId, card.Text!)).ToArray(),
            snapshot.Groups.Select(group => new BoardGroup(group.Id, group.ColumnId, group.Name)).ToArray());

    private static void ValidateOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new DomainValidationException("OperationIdRequired", "Укажите идентификатор операции.");
        }
    }
}
