namespace Domain;

public enum BoardItemKind
{
    Column,
    Card,
    Group,
}

public sealed record BoardCard(Guid Id, Guid ColumnId, Guid? GroupId, string Text);

public sealed record BoardGroup(Guid Id, Guid ColumnId, string Name)
{
    public const int MaxNameLength = 120;

    public static BoardGroup Create(Guid id, Guid columnId, string? name) =>
        new(id, columnId, RequiredText.Create(name, MaxNameLength));
}

public sealed record SuggestedGroup(string Name, Guid ColumnId, IReadOnlyList<Guid> CardIds);

public sealed record BoardStructure(
    IReadOnlyList<Guid> ColumnIds,
    IReadOnlyList<BoardCard> Cards,
    IReadOnlyList<BoardGroup> Groups)
{
    public BoardStructure Move(
        BoardItemKind sourceKind,
        Guid sourceId,
        BoardItemKind targetKind,
        Guid targetId,
        Guid newGroupId)
    {
        if (sourceKind == BoardItemKind.Group)
        {
            return MoveGroup(sourceId, targetKind, targetId);
        }

        if (sourceKind != BoardItemKind.Card)
        {
            throw new DomainValidationException("InvalidMoveSource", "Перемещать можно карточку или группу.");
        }

        var source = FindCard(sourceId);
        if (targetKind == BoardItemKind.Card && sourceId == targetId)
        {
            return this;
        }

        var destination = FindDestination(source, targetKind, targetId, newGroupId);
        if (source.ColumnId == destination.ColumnId && source.GroupId == destination.GroupId)
        {
            return this;
        }

        var groups = Groups.ToList();
        var cards = Cards.ToList();
        if (destination.NewGroup is not null)
        {
            groups.Add(destination.NewGroup);
            var target = FindCard(targetId);
            cards[FindCardIndex(targetId)] = target with { GroupId = destination.NewGroup.Id };
        }

        cards[FindCardIndex(sourceId)] = source with
        {
            ColumnId = destination.ColumnId,
            GroupId = destination.GroupId,
        };
        DissolveSourceGroup(source.GroupId, destination.GroupId, cards, groups);
        return new BoardStructure(ColumnIds, cards, groups);
    }

    public BoardStructure ApplySuggestions(
        IReadOnlyList<SuggestedGroup> suggestions,
        Func<Guid> idFactory)
    {
        if (suggestions.Count == 0)
        {
            return this;
        }

        var usedCardIds = new HashSet<Guid>();
        foreach (var suggestion in suggestions)
        {
            FindColumn(suggestion.ColumnId);
            BoardGroup.Create(Guid.NewGuid(), suggestion.ColumnId, suggestion.Name);
            if (suggestion.CardIds.Count < 2)
            {
                throw new DomainValidationException("InvalidAiResponse", "Группа предложения должна содержать хотя бы две карточки.");
            }

            foreach (var cardId in suggestion.CardIds)
            {
                if (!usedCardIds.Add(cardId))
                {
                    throw new DomainValidationException("InvalidAiResponse", "Карточка повторяется в предложении.");
                }

                var card = FindCard(cardId);
                if (card.GroupId is not null)
                {
                    throw new DomainValidationException("InvalidAiResponse", "Предложение содержит карточку из существующей группы.");
                }
            }
        }

        var groups = Groups.ToList();
        var cards = Cards.ToList();
        foreach (var suggestion in suggestions)
        {
            var group = BoardGroup.Create(idFactory(), suggestion.ColumnId, suggestion.Name);
            groups.Add(group);
            foreach (var cardId in suggestion.CardIds)
            {
                cards[FindCardIndex(cardId)] = FindCard(cardId) with
                {
                    ColumnId = group.ColumnId,
                    GroupId = group.Id,
                };
            }
        }

        return new BoardStructure(ColumnIds, cards, groups);
    }

    private BoardStructure MoveGroup(Guid sourceId, BoardItemKind targetKind, Guid targetId)
    {
        if (targetKind != BoardItemKind.Column)
        {
            throw new DomainValidationException("InvalidGroupMove", "Группу можно перенести только в колонку.");
        }

        FindColumn(targetId);
        var group = Groups.SingleOrDefault(item => item.Id == sourceId)
            ?? throw new KeyNotFoundException();
        if (group.ColumnId == targetId)
        {
            return this;
        }

        var cards = Cards
            .Select(card => card.GroupId == sourceId ? card with { ColumnId = targetId } : card)
            .ToArray();
        var groups = Groups
            .Select(item => item.Id == sourceId ? item with { ColumnId = targetId } : item)
            .ToArray();
        return new BoardStructure(ColumnIds, cards, groups);
    }

    private (Guid ColumnId, Guid? GroupId, BoardGroup? NewGroup) FindDestination(
        BoardCard source,
        BoardItemKind targetKind,
        Guid targetId,
        Guid newGroupId)
    {
        switch (targetKind)
        {
            case BoardItemKind.Column:
                return (FindColumn(targetId), null, null);
            case BoardItemKind.Group:
                var group = Groups.SingleOrDefault(item => item.Id == targetId)
                    ?? throw new KeyNotFoundException();
                return (group.ColumnId, group.Id, null);
            case BoardItemKind.Card:
                var target = FindCard(targetId);
                if (target.GroupId is not null)
                {
                    return (target.ColumnId, target.GroupId, null);
                }

                var name = target.Text.Length <= BoardGroup.MaxNameLength
                    ? target.Text
                    : target.Text[..BoardGroup.MaxNameLength];
                var newGroup = BoardGroup.Create(newGroupId, target.ColumnId, name);
                return (target.ColumnId, newGroup.Id, newGroup);
            default:
                throw new DomainValidationException("InvalidMoveTarget", "Неизвестное назначение переноса.");
        }
    }

    private void DissolveSourceGroup(
        Guid? sourceGroupId,
        Guid? destinationGroupId,
        List<BoardCard> cards,
        List<BoardGroup> groups)
    {
        if (sourceGroupId is null || sourceGroupId == destinationGroupId)
        {
            return;
        }

        var remaining = cards.Where(card => card.GroupId == sourceGroupId).ToArray();
        if (remaining.Length > 1)
        {
            return;
        }

        foreach (var card in remaining)
        {
            cards[FindCardIndex(card.Id)] = card with { GroupId = null };
        }

        groups.RemoveAll(group => group.Id == sourceGroupId);
    }

    private BoardCard FindCard(Guid cardId) =>
        Cards.SingleOrDefault(card => card.Id == cardId) ?? throw new KeyNotFoundException();

    private Guid FindColumn(Guid columnId) =>
        ColumnIds.Contains(columnId) ? columnId : throw new KeyNotFoundException();

    private int FindCardIndex(Guid cardId)
    {
        var index = 0;
        foreach (var card in Cards)
        {
            if (card.Id == cardId)
            {
                return index;
            }

            index++;
        }

        throw new KeyNotFoundException();
    }
}
