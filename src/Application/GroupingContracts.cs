using Domain;

namespace Application;

public sealed record GroupSuggestionColumn(Guid Id, string Name);

public sealed record GroupSuggestionCard(Guid Id, Guid ColumnId, string Text);

public sealed record GroupSuggestionRequest(
    IReadOnlyList<GroupSuggestionColumn> Columns,
    IReadOnlyList<GroupSuggestionCard> FreeCards);

public interface IGroupSuggestionProvider
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<SuggestedGroup>> SuggestGroupsAsync(
        GroupSuggestionRequest request,
        CancellationToken cancellationToken);
}

public sealed record GroupProposal(
    Guid Id,
    int BoardVersion,
    IReadOnlyList<SuggestedGroup> Groups);

public interface IGroupingRepository
{
    Task<MeetingCommandResult> MoveAsync(
        Guid meetingId,
        Guid userId,
        BoardItemKind sourceKind,
        Guid sourceId,
        BoardItemKind targetKind,
        Guid targetId,
        int expectedBoardVersion,
        Guid operationId,
        string fingerprint,
        Guid newGroupId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> RenameGroupAsync(
        Guid meetingId,
        Guid userId,
        Guid groupId,
        string name,
        int expectedBoardVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> SaveProposalAsync(
        Guid meetingId,
        Guid facilitatorId,
        GroupProposal proposal,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<MeetingCommandResult> ApplyProposalAsync(
        Guid meetingId,
        Guid facilitatorId,
        Guid proposalId,
        int expectedBoardVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
