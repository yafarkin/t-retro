using Domain;

namespace Application;

public sealed class VotingService(
    IVotingRepository voting,
    MeetingAccessService access,
    IMeetingNotifier notifier,
    TimeProvider timeProvider)
{
    public async Task<VotingCommandResult> SetMyVotesAsync(
        Guid userId,
        Guid meetingId,
        Guid topicId,
        int count,
        int expectedBallotVersion,
        CancellationToken cancellationToken)
    {
        if (count < 0)
        {
            throw new DomainValidationException("InvalidVoteCount", "Число голосов не может быть отрицательным.");
        }

        if (expectedBallotVersion < 0)
        {
            throw new DomainValidationException("InvalidBallotVersion", "Версия бюллетеня не может быть отрицательной.");
        }

        await access.GetAsync(userId, meetingId, false, cancellationToken);
        var result = await voting.SetMyVotesAsync(
            meetingId,
            userId,
            topicId,
            count,
            expectedBallotVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return await CompleteAsync(meetingId, result, cancellationToken);
    }

    public async Task<VotingCommandResult> UpdateSettingsAsync(
        Guid userId,
        Guid meetingId,
        string mode,
        int percent,
        int minimum,
        int? fixedBudget,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<VoteMode>(mode, true, out var parsedMode))
        {
            throw new DomainValidationException("InvalidVoteMode", "Неизвестный режим бюджета.");
        }

        var settings = VoteBudgetSettings.Create(parsedMode, percent, minimum, fixedBudget);
        await access.GetAsync(userId, meetingId, true, cancellationToken);
        var result = await voting.UpdateSettingsAsync(
            meetingId,
            userId,
            settings,
            expectedVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return await CompleteAsync(meetingId, result, cancellationToken);
    }

    private async Task<VotingCommandResult> CompleteAsync(
        Guid meetingId,
        VotingCommandResult result,
        CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case VotingCommandStatus.NotFound:
                throw new NotFoundException("Тема голосования не найдена.");
            case VotingCommandStatus.Forbidden:
                throw new ForbiddenException("Forbidden", "Недостаточно прав для этого действия.");
            case VotingCommandStatus.InvalidPhase:
                throw new ConflictException("InvalidPhase", "Действие доступно только во время голосования.");
            case VotingCommandStatus.VersionConflict:
                throw new ConflictException("VersionConflict", "Состояние изменилось. Обновите данные и повторите действие.");
            case VotingCommandStatus.BudgetExceeded:
                throw new ConflictException("VoteBudgetExceeded", "Недостаточно голосов в личном бюджете.");
            case VotingCommandStatus.BudgetTooLow:
                throw new ConflictException(
                    "VoteBudgetTooLow",
                    $"Бюджет нельзя установить меньше {result.MinimumBudget}: участники уже потратили больше голосов.");
        }

        if (!result.IsReplay)
        {
            await notifier.StateChangedAsync(meetingId, result.Version, cancellationToken);
        }

        return result;
    }
}
