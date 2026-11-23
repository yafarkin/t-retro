namespace Domain;

public enum MeetingPhase
{
    Draft,
    Collect,
    Group,
    Vote,
    Discuss,
    Closed,
}

public enum MemberStatus
{
    Active,
    Excluded,
}

public enum VoteMode
{
    Percent,
    Fixed,
}

public sealed record VoteBudgetSettings(VoteMode Mode, int Percent, int Minimum, int? Fixed)
{
    public static VoteBudgetSettings Default => new(VoteMode.Percent, 5, 2, null);

    public static VoteBudgetSettings Create(VoteMode mode, int percent, int minimum, int? fixedBudget)
    {
        if (mode == VoteMode.Percent)
        {
            if (percent is < 0 or > 100)
            {
                throw new DomainValidationException("InvalidVotePercent", "Процент голосов должен быть от 0 до 100.");
            }

            if (minimum < 1)
            {
                throw new DomainValidationException("InvalidVoteMinimum", "Минимум голосов должен быть не меньше 1.");
            }

            return new VoteBudgetSettings(mode, percent, minimum, null);
        }

        if (fixedBudget is null or < 1)
        {
            throw new DomainValidationException("InvalidFixedBudget", "Фиксированный бюджет должен быть не меньше 1.");
        }

        return new VoteBudgetSettings(mode, 5, 2, fixedBudget);
    }

    public int BudgetFor(int originalCardCount)
    {
        if (originalCardCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalCardCount));
        }

        return Mode == VoteMode.Fixed
            ? Fixed!.Value
            : Math.Max(Minimum, (int)Math.Ceiling(originalCardCount * (double)Percent / 100));
    }
}

public sealed record Meeting(
    Guid Id,
    string Name,
    MeetingPhase Phase,
    bool AnonymousCards,
    bool PrivateCollection,
    bool AnonymousVotes,
    VoteBudgetSettings VoteSettings,
    int? InitialCardCount,
    Guid? FacilitatorId,
    int InvitationVersion,
    int Version,
    int BoardVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record MeetingColumn(Guid Id, Guid MeetingId, string Name, string Color, int Position);

public sealed record MeetingGroup(Guid Id, Guid MeetingId, Guid ColumnId, string Name);

public sealed record MeetingMember(
    Guid MeetingId,
    Guid UserId,
    MemberStatus Status,
    DateTimeOffset JoinedAtUtc,
    DateTimeOffset? ExcludedAtUtc);

public enum TemplateVisibility
{
    Personal,
    Shared,
}

public sealed record MeetingTemplate(
    Guid Id,
    Guid OwnerId,
    string Name,
    TemplateVisibility Visibility,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TemplateColumn(Guid Id, Guid TemplateId, string Name, string Color, int Position);
