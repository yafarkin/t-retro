using Domain;

namespace Domain.Tests;

public sealed class CardAndPhaseRulesTests
{
    [Fact]
    public void CardTextIsTrimmedAndLimited()
    {
        var now = DateTimeOffset.UtcNow;
        var card = Card.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  Идея  ", now);

        Assert.Equal("Идея", card.Text);
        Assert.Throws<DomainValidationException>(() =>
            Card.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   ", now));
        Assert.Throws<DomainValidationException>(() =>
            Card.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('x', 4001), now));
    }

    [Fact]
    public void UpdatedCardKeepsIdentityAndIncrementsVersion()
    {
        var now = DateTimeOffset.UtcNow;
        var card = Card.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "До", now);

        var updated = card.UpdateText(" После ", now.AddMinutes(1));

        Assert.Equal(card.Id, updated.Id);
        Assert.Equal("После", updated.Text);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public void PhasesAdvanceExactlyOneStepAndStopAtClosed()
    {
        Assert.Equal(MeetingPhase.Collect, MeetingPhaseRules.Next(MeetingPhase.Draft));
        Assert.Equal(MeetingPhase.Group, MeetingPhaseRules.Next(MeetingPhase.Collect));
        Assert.Equal(MeetingPhase.Vote, MeetingPhaseRules.Next(MeetingPhase.Group));
        Assert.Equal(MeetingPhase.Discuss, MeetingPhaseRules.Next(MeetingPhase.Group, votingEnabled: false));
        Assert.Equal(MeetingPhase.Discuss, MeetingPhaseRules.Next(MeetingPhase.Vote));
        Assert.Equal(MeetingPhase.Closed, MeetingPhaseRules.Next(MeetingPhase.Discuss));
        Assert.Throws<DomainValidationException>(() => MeetingPhaseRules.Next(MeetingPhase.Closed));
    }
}
