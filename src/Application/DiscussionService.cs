using Domain;

namespace Application;

public sealed class DiscussionService(
    IDiscussionRepository discussion,
    MeetingAccessService access,
    IMeetingNotifier notifier,
    TimeProvider timeProvider)
{
    public Task<MeetingCommandResult> SelectCurrentAsync(
        Guid userId,
        Guid meetingId,
        Guid topicId,
        int expectedVersion,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            userId,
            meetingId,
            now => discussion.SelectCurrentAsync(
                meetingId,
                userId,
                topicId,
                expectedVersion,
                now,
                cancellationToken),
            cancellationToken);

    public Task<MeetingCommandResult> SetTopicStatusAsync(
        Guid userId,
        Guid meetingId,
        Guid topicId,
        string status,
        bool advance,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DiscussionStatus>(status, true, out var parsedStatus))
        {
            throw new DomainValidationException("InvalidDiscussionStatus", "Неизвестный статус обсуждения.");
        }

        return ExecuteAsync(
            userId,
            meetingId,
            now => discussion.SetTopicStatusAsync(
                meetingId,
                userId,
                topicId,
                parsedStatus,
                advance,
                expectedVersion,
                now,
                cancellationToken),
            cancellationToken);
    }

    public Task<MeetingCommandResult> CreateCommentAsync(
        Guid userId,
        Guid meetingId,
        Guid topicId,
        string? text,
        int expectedVersion,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        var comment = new TopicComment(Guid.NewGuid(), meetingId, topicId, ValidateText(text));
        return ExecuteAsync(
            userId,
            meetingId,
            now => discussion.CreateCommentAsync(
                comment,
                userId,
                expectedVersion,
                operationId,
                CommandFingerprint.Create("Comment", topicId, comment.Text),
                now,
                cancellationToken),
            cancellationToken,
            requireFacilitator: false);
    }

    public Task<MeetingCommandResult> UpdateCommentAsync(
        Guid userId,
        Guid meetingId,
        Guid commentId,
        string? text,
        int expectedVersion,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            userId,
            meetingId,
            now => discussion.UpdateCommentAsync(
                meetingId,
                commentId,
                userId,
                ValidateText(text),
                expectedVersion,
                now,
                cancellationToken),
            cancellationToken);

    public Task<MeetingCommandResult> DeleteCommentAsync(
        Guid userId,
        Guid meetingId,
        Guid commentId,
        int expectedVersion,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            userId,
            meetingId,
            now => discussion.DeleteCommentAsync(
                meetingId,
                commentId,
                userId,
                expectedVersion,
                now,
                cancellationToken),
            cancellationToken);

    public Task<MeetingCommandResult> CreateDecisionAsync(
        Guid userId,
        Guid meetingId,
        Guid topicId,
        string? text,
        int expectedVersion,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        var decision = new TopicDecision(Guid.NewGuid(), meetingId, topicId, ValidateText(text));
        return ExecuteAsync(
            userId,
            meetingId,
            now => discussion.CreateDecisionAsync(
                decision,
                userId,
                expectedVersion,
                operationId,
                CommandFingerprint.Create("Decision", topicId, decision.Text),
                now,
                cancellationToken),
            cancellationToken);
    }

    public Task<MeetingCommandResult> UpdateDecisionAsync(
        Guid userId,
        Guid meetingId,
        Guid decisionId,
        string? text,
        int expectedVersion,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            userId,
            meetingId,
            now => discussion.UpdateDecisionAsync(
                meetingId,
                decisionId,
                userId,
                ValidateText(text),
                expectedVersion,
                now,
                cancellationToken),
            cancellationToken);

    public Task<MeetingCommandResult> DeleteDecisionAsync(
        Guid userId,
        Guid meetingId,
        Guid decisionId,
        int expectedVersion,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            userId,
            meetingId,
            now => discussion.DeleteDecisionAsync(
                meetingId,
                decisionId,
                userId,
                expectedVersion,
                now,
                cancellationToken),
            cancellationToken);

    public Task<MeetingCommandResult> CreateActionAsync(
        Guid userId,
        Guid meetingId,
        Guid topicId,
        string? text,
        string? description,
        Guid? assigneeId,
        int expectedVersion,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperationId(operationId);
        var action = new ActionItem(
            Guid.NewGuid(), meetingId, topicId, ValidateText(text), assigneeId, ValidateDescription(description));
        var fingerprint = action.Description is null
            ? CommandFingerprint.Create("Action", topicId, action.Text, assigneeId)
            : CommandFingerprint.Create("Action", topicId, action.Text, assigneeId, action.Description);
        return ExecuteAsync(
            userId,
            meetingId,
            now => discussion.CreateActionAsync(
                action,
                userId,
                expectedVersion,
                operationId,
                fingerprint,
                now,
                cancellationToken),
            cancellationToken,
            requireFacilitator: false);
    }

    public Task<MeetingCommandResult> UpdateActionAsync(
        Guid userId,
        Guid meetingId,
        Guid actionId,
        string? text,
        string? description,
        Guid? assigneeId,
        int expectedVersion,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            userId,
            meetingId,
            now => discussion.UpdateActionAsync(
                meetingId,
                actionId,
                userId,
                ValidateText(text),
                ValidateDescription(description),
                assigneeId,
                expectedVersion,
                now,
                cancellationToken),
            cancellationToken);

    public Task<MeetingCommandResult> DeleteActionAsync(
        Guid userId,
        Guid meetingId,
        Guid actionId,
        int expectedVersion,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            userId,
            meetingId,
            now => discussion.DeleteActionAsync(
                meetingId,
                actionId,
                userId,
                expectedVersion,
                now,
                cancellationToken),
            cancellationToken);

    private async Task<MeetingCommandResult> ExecuteAsync(
        Guid userId,
        Guid meetingId,
        Func<DateTimeOffset, Task<MeetingCommandResult>> command,
        CancellationToken cancellationToken,
        bool requireFacilitator = true)
    {
        await access.GetAsync(userId, meetingId, requireFacilitator, cancellationToken);
        var result = await command(timeProvider.GetUtcNow());
        switch (result.Status)
        {
            case MeetingCommandStatus.NotFound:
                throw new NotFoundException("Ресурс обсуждения не найден.");
            case MeetingCommandStatus.Forbidden:
                throw new ForbiddenException("Forbidden", "Действие доступно только фасилитатору.");
            case MeetingCommandStatus.InvalidPhase:
                throw new ConflictException("InvalidPhase", "Действие доступно только во время обсуждения.");
            case MeetingCommandStatus.VersionConflict:
                throw new ConflictException("VersionConflict", "Состояние изменилось. Обновите данные и повторите действие.");
            case MeetingCommandStatus.OperationConflict:
                throw new ConflictException("OperationConflict", "Идентификатор операции уже использован для другой команды.");
            case MeetingCommandStatus.InvalidAssignee:
                throw new DomainValidationException("InvalidAssignee", "Исполнитель должен быть действующим участником встречи.");
            case MeetingCommandStatus.InvalidNavigation:
                throw new ConflictException("InvalidDiscussionNavigation", "Команду «Далее» можно выполнить только для текущей темы.");
            case MeetingCommandStatus.ActionLocked:
                throw new ConflictException("ActionLocked", "Задачу нельзя изменить или удалить во время экспорта, после создания в Jira или до сверки неизвестного исхода.");
        }

        if (!result.IsReplay)
        {
            await notifier.StateChangedAsync(meetingId, result.Version, cancellationToken);
        }

        return result;
    }

    private static string? ValidateDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : ValidateText(description);

    private static string ValidateText(string? text) => RequiredText.Create(text, 4000);

    private static void ValidateOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new DomainValidationException("OperationIdRequired", "Укажите идентификатор операции.");
        }
    }
}
