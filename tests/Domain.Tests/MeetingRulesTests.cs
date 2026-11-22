using Domain;

namespace Domain.Tests;

public sealed class MeetingRulesTests
{
    [Fact]
    public void ColumnColorIsNormalized()
    {
        var (_, color) = ColumnRules.Validate("Начать", "#a1b2c3");

        Assert.Equal("#A1B2C3", color);
    }

    [Fact]
    public void ColumnColorRejectsInvalidFormat()
    {
        Assert.Throws<DomainValidationException>(() => ColumnRules.Validate("Начать", "red"));
    }

    [Fact]
    public void DefaultVoteBudgetIsPercentFiveAndMinimumTwo()
    {
        var settings = VoteBudgetSettings.Default;

        Assert.Equal(VoteMode.Percent, settings.Mode);
        Assert.Equal(5, settings.Percent);
        Assert.Equal(2, settings.Minimum);
    }

    [Fact]
    public void FixedBudgetMustBePositive()
    {
        Assert.Throws<DomainValidationException>(() => VoteBudgetSettings.Create(VoteMode.Fixed, 5, 2, 0));
    }
}
