using Application;
using Domain;

namespace Application.Tests;

public sealed class MeetingCardVisibilityTests
{
    private static readonly Guid ViewerId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, true)]
    public void OtherCardFollowsPrivacyMatrix(
        bool anonymousCards,
        bool privateCollection,
        bool expectText,
        bool expectAuthor)
    {
        var projected = MeetingCardVisibility.Project(
            Meeting(MeetingPhase.Collect, anonymousCards, privateCollection),
            Data(AuthorId),
            ViewerId);

        Assert.Equal(expectText, projected.Text is not null);
        Assert.Equal(expectAuthor, projected.AuthorId is not null);
        Assert.Equal(expectAuthor, projected.AuthorDisplayName is not null);
        Assert.Equal(!expectText, projected.IsHidden);
        Assert.False(projected.IsMine);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OwnCardAlwaysKeepsTextAndOwnership(bool anonymousCards)
    {
        var projected = MeetingCardVisibility.Project(
            Meeting(MeetingPhase.Collect, anonymousCards, true),
            Data(ViewerId),
            ViewerId);

        Assert.Equal("Текст карточки", projected.Text);
        Assert.True(projected.IsMine);
        Assert.False(projected.IsHidden);
        Assert.Equal(!anonymousCards, projected.AuthorId is not null);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void PrivateCollectionRevealsTextAndOnlyPermittedAuthorsAfterCollect(
        bool anonymousCards, bool anonymous, bool expectAuthor)
    {
        var projected = MeetingCardVisibility.Project(
            Meeting(MeetingPhase.Group, anonymousCards, true),
            Data(AuthorId, anonymous),
            ViewerId);

        Assert.Equal("Текст карточки", projected.Text);
        Assert.Equal(expectAuthor ? AuthorId : null, projected.AuthorId);
        Assert.Equal(expectAuthor ? "Автор" : null, projected.AuthorDisplayName);
    }

    private static Meeting Meeting(MeetingPhase phase, bool anonymousCards, bool privateCollection) =>
        new(
            Guid.NewGuid(),
            "Ретро",
            phase,
            anonymousCards,
            privateCollection,
            VoteBudgetSettings.Default,
            null,
            null,
            1,
            1,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static CardData Data(Guid authorId, bool anonymous = false)
    {
        var now = DateTimeOffset.UtcNow;
        var meetingId = Guid.NewGuid();
        return new CardData(
            Card.Create(Guid.NewGuid(), meetingId, Guid.NewGuid(), "Текст карточки", now, anonymous),
            authorId,
            "Автор");
    }
}
