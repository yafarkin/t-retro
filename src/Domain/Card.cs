namespace Domain;

public sealed record Card(
    Guid Id,
    Guid MeetingId,
    Guid ColumnId,
    string Text,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? GroupId = null,
    bool Anonymous = false)
{
    public const int MaxTextLength = 4000;

    public static Card Create(
        Guid id,
        Guid meetingId,
        Guid columnId,
        string? text,
        DateTimeOffset nowUtc,
        bool anonymous = false) =>
        new(
            id,
            meetingId,
            columnId,
            RequiredText.Create(text, MaxTextLength),
            1,
            nowUtc,
            nowUtc,
            Anonymous: anonymous);

    public Card UpdateText(string? text, DateTimeOffset nowUtc) =>
        this with
        {
            Text = RequiredText.Create(text, MaxTextLength),
            Version = Version + 1,
            UpdatedAtUtc = nowUtc,
        };
}
