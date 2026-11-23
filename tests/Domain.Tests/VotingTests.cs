using Domain;

namespace Domain.Tests;

public sealed class VotingTests
{
    [Theory]
    [InlineData(0, 5, 2, 2)]
    [InlineData(40, 5, 2, 2)]
    [InlineData(41, 5, 2, 3)]
    [InlineData(41, 0, 2, 2)]
    [InlineData(10, 50, 1, 5)]
    public void PercentBudgetUsesCeilingAndMinimum(int cards, int percent, int minimum, int expected)
    {
        var settings = VoteBudgetSettings.Create(VoteMode.Percent, percent, minimum, null);

        Assert.Equal(expected, settings.BudgetFor(cards));
    }

    [Fact]
    public void FixedBudgetDoesNotDependOnCardCount()
    {
        var settings = VoteBudgetSettings.Create(VoteMode.Fixed, 5, 2, 7);

        Assert.Equal(7, settings.BudgetFor(0));
        Assert.Equal(7, settings.BudgetFor(100));
    }
}
