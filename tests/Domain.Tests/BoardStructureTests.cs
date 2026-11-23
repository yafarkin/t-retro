using Domain;

namespace Domain.Tests;

public sealed class BoardStructureTests
{
    private static readonly Guid FirstColumn = Guid.NewGuid();
    private static readonly Guid SecondColumn = Guid.NewGuid();
    private static readonly Guid FirstCard = Guid.NewGuid();
    private static readonly Guid SecondCard = Guid.NewGuid();
    private static readonly Guid ThirdCard = Guid.NewGuid();

    [Fact]
    public void MovingCardOntoSingleCardCreatesGroupInTargetColumn()
    {
        var board = Board();

        var moved = board.Move(BoardItemKind.Card, FirstCard, BoardItemKind.Card, SecondCard, Guid.NewGuid());

        var group = Assert.Single(moved.Groups);
        Assert.Equal(SecondColumn, group.ColumnId);
        Assert.Equal("Вторая", group.Name);
        Assert.Equal(2, moved.Cards.Count(card => card.GroupId == group.Id && card.ColumnId == SecondColumn));
    }

    [Fact]
    public void RemovingOneOfTwoCardsDissolvesGroup()
    {
        var board = Board().Move(BoardItemKind.Card, FirstCard, BoardItemKind.Card, SecondCard, Guid.NewGuid());

        var moved = board.Move(BoardItemKind.Card, FirstCard, BoardItemKind.Column, FirstColumn, Guid.NewGuid());

        Assert.Empty(moved.Groups);
        Assert.All(moved.Cards, card => Assert.Null(card.GroupId));
        Assert.Equal(FirstColumn, moved.Cards.Single(card => card.Id == FirstCard).ColumnId);
        Assert.Equal(SecondColumn, moved.Cards.Single(card => card.Id == SecondCard).ColumnId);
    }

    [Fact]
    public void MovingWholeGroupUpdatesAllCardsAndRejectsNesting()
    {
        var board = Board().Move(BoardItemKind.Card, FirstCard, BoardItemKind.Card, SecondCard, Guid.NewGuid());
        var groupId = board.Groups.Single().Id;

        var moved = board.Move(BoardItemKind.Group, groupId, BoardItemKind.Column, FirstColumn, Guid.NewGuid());

        Assert.Equal(FirstColumn, moved.Groups.Single().ColumnId);
        Assert.Equal(2, moved.Cards.Count(card => card.GroupId == groupId && card.ColumnId == FirstColumn));
        Assert.Throws<DomainValidationException>(() =>
            moved.Move(BoardItemKind.Group, groupId, BoardItemKind.Card, ThirdCard, Guid.NewGuid()));
    }

    [Fact]
    public void MovingInsideSameGroupAndOntoSelfAreNoOps()
    {
        var board = Board().Move(BoardItemKind.Card, FirstCard, BoardItemKind.Card, SecondCard, Guid.NewGuid());
        var groupId = board.Groups.Single().Id;

        var sameCard = board.Move(BoardItemKind.Card, FirstCard, BoardItemKind.Card, FirstCard, Guid.NewGuid());
        var sameGroup = board.Move(BoardItemKind.Card, FirstCard, BoardItemKind.Group, groupId, Guid.NewGuid());

        Assert.Same(board, sameCard);
        Assert.Same(board, sameGroup);
    }

    [Fact]
    public void ApplyingSuggestionsValidatesCardsAndPreservesManualGroups()
    {
        var manual = Board().Move(BoardItemKind.Card, FirstCard, BoardItemKind.Card, SecondCard, Guid.NewGuid());
        var fourthCard = Guid.NewGuid();
        var fifthCard = Guid.NewGuid();
        var withFreeCards = new BoardStructure(
            [FirstColumn, SecondColumn],
            [.. manual.Cards, new BoardCard(fourthCard, FirstColumn, null, "Четвёртая"), new BoardCard(fifthCard, FirstColumn, null, "Пятая")],
            manual.Groups);

        var updated = withFreeCards.ApplySuggestions(
            [new SuggestedGroup("Предложение", FirstColumn, [fourthCard, fifthCard])],
            Guid.NewGuid);

        Assert.Equal(2, updated.Groups.Count);
        Assert.Equal(2, updated.Cards.Count(card => card.GroupId == manual.Groups.Single().Id));
        Assert.Throws<DomainValidationException>(() => withFreeCards.ApplySuggestions(
            [new SuggestedGroup("Повтор", FirstColumn, [ThirdCard, ThirdCard])],
            Guid.NewGuid));
        Assert.Throws<DomainValidationException>(() => withFreeCards.ApplySuggestions(
            [new SuggestedGroup("Одна", FirstColumn, [ThirdCard])],
            Guid.NewGuid));
    }

    private static BoardStructure Board() =>
        new(
            [FirstColumn, SecondColumn],
            [
                new BoardCard(FirstCard, FirstColumn, null, "Первая"),
                new BoardCard(SecondCard, SecondColumn, null, "Вторая"),
                new BoardCard(ThirdCard, FirstColumn, null, "Третья"),
            ],
            []);
}
