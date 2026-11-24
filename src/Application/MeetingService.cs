using Domain;

namespace Application;

public sealed class MeetingService(
    IMeetingRepository meetings,
    MeetingAccessService access,
    IMeetingNotifier notifier)
{
    public async Task<MeetingSnapshot> CreateAsync(
        Guid userId,
        string? name,
        IReadOnlyList<ColumnInput> columns,
        PrivacyInput privacy,
        string voteMode,
        int votePercent,
        int voteMinimum,
        int? fixedBudget,
        CancellationToken cancellationToken)
    {
        var meetingId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var meeting = new Meeting(
            meetingId,
            RequiredText.Create(name, 120),
            MeetingPhase.Draft,
            privacy.AnonymousCards,
            privacy.PrivateCollection,
            privacy.AnonymousVotes,
            ParseVoteSettings(voteMode, votePercent, voteMinimum, fixedBudget),
            null,
            userId,
            1,
            1,
            1,
            now,
            now);
        var meetingColumns = CreateColumns(meetingId, columns);
        var creator = new MeetingMember(meetingId, userId, MemberStatus.Active, now, null);
        await meetings.CreateAsync(meeting, meetingColumns, creator, cancellationToken);
        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        await notifier.StateChangedAsync(meetingId, snapshot.Meeting.Version, cancellationToken);
        return snapshot;
    }

    public Task<MeetingSnapshot> GetStateAsync(
        Guid userId,
        Guid meetingId,
        CancellationToken cancellationToken) =>
        access.GetAsync(userId, meetingId, false, cancellationToken);

    public Task<IReadOnlyList<MeetingSummary>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        meetings.ListForUserAsync(userId, cancellationToken);

    public async Task<MeetingSnapshot> UpdateDraftAsync(
        Guid userId,
        Guid meetingId,
        int expectedVersion,
        string? name,
        IReadOnlyList<ColumnInput> columns,
        PrivacyInput privacy,
        string voteMode,
        int votePercent,
        int voteMinimum,
        int? fixedBudget,
        CancellationToken cancellationToken)
    {
        var current = await access.GetAsync(userId, meetingId, true, cancellationToken);
        if (current.Meeting.Phase != MeetingPhase.Draft)
        {
            throw new ConflictException("InvalidPhase", "Настройки можно менять только в подготовке.");
        }

        var updated = current.Meeting with
        {
            Name = RequiredText.Create(name, 120),
            AnonymousCards = privacy.AnonymousCards,
            PrivateCollection = privacy.PrivateCollection,
            AnonymousVotes = privacy.AnonymousVotes,
            VoteSettings = ParseVoteSettings(voteMode, votePercent, voteMinimum, fixedBudget),
            Version = current.Meeting.Version + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var updatedColumns = CreateColumns(meetingId, columns);
        var affected = await meetings.UpdateDraftAsync(
            updated,
            updatedColumns,
            expectedVersion,
            userId,
            cancellationToken);
        if (affected == 0)
        {
            throw new ConflictException("VersionConflict", "Настройки изменились. Обновите состояние и повторите сохранение.");
        }

        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        await notifier.StateChangedAsync(meetingId, snapshot.Meeting.Version, cancellationToken);
        return snapshot;
    }

    private static IReadOnlyList<MeetingColumn> CreateColumns(
        Guid meetingId,
        IReadOnlyList<ColumnInput> inputs)
    {
        if (inputs.Count == 0)
        {
            throw new DomainValidationException("ColumnsRequired", "Добавьте хотя бы одну колонку.");
        }

        return inputs.Select((input, position) =>
        {
            var (name, color) = ColumnRules.Validate(input.Name, input.Color);
            return new MeetingColumn(Guid.NewGuid(), meetingId, name, color, position, ColumnRules.Description(input.Description));
        }).ToArray();
    }

    private static VoteBudgetSettings ParseVoteSettings(
        string mode,
        int percent,
        int minimum,
        int? fixedBudget)
    {
        if (!Enum.TryParse<VoteMode>(mode, true, out var parsedMode))
        {
            throw new DomainValidationException("InvalidVoteMode", "Неизвестный режим бюджета.");
        }

        return VoteBudgetSettings.Create(parsedMode, percent, minimum, fixedBudget);
    }
}
