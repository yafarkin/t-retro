using Domain;

namespace Application;

public sealed record CardData(Card Card, Guid AuthorId, string AuthorDisplayName);

public sealed record MeetingCard(
    Guid Id,
    Guid ColumnId,
    string? Text,
    Guid? AuthorId,
    string? AuthorDisplayName,
    bool IsMine,
    bool IsHidden,
    int Version,
    Guid? GroupId = null);

public sealed record MeetingReadiness(int ReadyCount, int TotalCount, bool MyReady);

public enum MeetingCommandStatus
{
    Success,
    NotFound,
    Forbidden,
    InvalidPhase,
    VersionConflict,
    OperationConflict,
    InvalidAssignee,
    InvalidNavigation,
    ActionLocked,
}

public sealed record MeetingCommandResult(
    MeetingCommandStatus Status,
    int Version = 0,
    Guid? EntityId = null,
    bool IsReplay = false,
    int BoardVersion = 0);
