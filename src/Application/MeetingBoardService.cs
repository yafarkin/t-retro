using Domain;

namespace Application;

public sealed class MeetingBoardService(
    IMeetingRepository meetings,
    MeetingAccessService access,
    IMeetingNotifier notifier,
    IMeetingTimer timer,
    TimeProvider timeProvider)
{
    public async Task<MeetingCommandResult> CreateCardAsync(
        Guid userId,
        Guid meetingId,
        Guid columnId,
        string? text,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        if (snapshot.Columns.All(column => column.Id != columnId))
        {
            throw new NotFoundException("Колонка не найдена.");
        }

        var card = Card.Create(Guid.NewGuid(), meetingId, columnId, text, timeProvider.GetUtcNow());
        var result = await meetings.CreateCardAsync(
            card,
            userId,
            operationId,
            CommandFingerprint.Create(columnId, card.Text),
            cancellationToken);
        return await CompleteAsync(meetingId, result, cancellationToken);
    }

    public async Task<MeetingCommandResult> UpdateCardAsync(
        Guid userId,
        Guid meetingId,
        Guid cardId,
        string? text,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        var current = FindOwnCard(snapshot, cardId);
        var now = timeProvider.GetUtcNow();
        var card = new Card(
            current.Id,
            meetingId,
            current.ColumnId,
            current.Text!,
            current.Version,
            now,
            now).UpdateText(text, now);
        var result = await meetings.UpdateCardAsync(card, userId, expectedVersion, cancellationToken);
        return await CompleteAsync(meetingId, result, cancellationToken);
    }

    public async Task<MeetingCommandResult> DeleteCardAsync(
        Guid userId,
        Guid meetingId,
        Guid cardId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        FindOwnCard(snapshot, cardId);
        var result = await meetings.DeleteCardAsync(
            meetingId,
            cardId,
            userId,
            expectedVersion,
            cancellationToken);
        return await CompleteAsync(meetingId, result, cancellationToken);
    }

    public async Task<MeetingCommandResult> SetReadinessAsync(
        Guid userId,
        Guid meetingId,
        string phase,
        bool ready,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MeetingPhase>(phase, true, out var parsedPhase)
            || parsedPhase is not (MeetingPhase.Collect or MeetingPhase.Vote))
        {
            throw new DomainValidationException(
                "InvalidReadinessPhase",
                "Готовность доступна только во время сбора и голосования.");
        }

        await access.GetAsync(userId, meetingId, false, cancellationToken);
        var result = await meetings.SetReadinessAsync(
            meetingId,
            userId,
            parsedPhase,
            ready,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return await CompleteAsync(meetingId, result, cancellationToken);
    }

    public async Task<MeetingCommandResult> AdvanceAsync(
        Guid userId,
        Guid meetingId,
        string fromPhase,
        int expectedVersion,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        if (!Enum.TryParse<MeetingPhase>(fromPhase, true, out var parsedPhase))
        {
            throw new DomainValidationException("InvalidPhase", "Неизвестная фаза встречи.");
        }

        if (parsedPhase == MeetingPhase.Discuss)
        {
            throw new ConflictException(
                "CompletionUnavailable",
                "Завершение встречи станет доступно вместе с итогами и PDF.");
        }

        var nextPhase = MeetingPhaseRules.Next(parsedPhase);
        await access.GetAsync(userId, meetingId, true, cancellationToken);
        var result = await meetings.AdvanceAsync(
            meetingId,
            userId,
            parsedPhase,
            nextPhase,
            expectedVersion,
            operationId,
            CommandFingerprint.Create(parsedPhase, expectedVersion),
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result.Status == MeetingCommandStatus.Success && !result.IsReplay)
        {
            timer.Cancel(meetingId);
        }

        return await CompleteAsync(meetingId, result, cancellationToken);
    }

    private async Task<MeetingCommandResult> CompleteAsync(
        Guid meetingId,
        MeetingCommandResult result,
        CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case MeetingCommandStatus.NotFound:
                throw new NotFoundException("Ресурс не найден.");
            case MeetingCommandStatus.Forbidden:
                throw new ForbiddenException("Forbidden", "Недостаточно прав для этого действия.");
            case MeetingCommandStatus.InvalidPhase:
                throw new ConflictException("InvalidPhase", "Действие недоступно в текущей фазе.");
            case MeetingCommandStatus.VersionConflict:
                throw new ConflictException("VersionConflict", "Состояние изменилось. Обновите данные и повторите действие.");
            case MeetingCommandStatus.OperationConflict:
                throw new ConflictException("OperationConflict", "Идентификатор операции уже использован для другой команды.");
        }

        if (!result.IsReplay)
        {
            await notifier.StateChangedAsync(meetingId, result.Version, cancellationToken);
        }

        return result;
    }

    private static MeetingCard FindOwnCard(MeetingSnapshot snapshot, Guid cardId)
    {
        var card = snapshot.Cards.SingleOrDefault(item => item.Id == cardId)
            ?? throw new NotFoundException("Карточка не найдена.");
        if (!card.IsMine)
        {
            throw new ForbiddenException("CardAuthorRequired", "Изменять и удалять можно только свои карточки.");
        }

        return card;
    }

    private static void ValidateOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new DomainValidationException("OperationIdRequired", "Укажите идентификатор операции.");
        }
    }
}
