namespace Domain;

public sealed record Card(
    Guid Id,
    Guid MeetingId,
    Guid ColumnId,
    string Text,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? GroupId = null)
{
    public const int MaxTextLength = 4000;

    public static Card Create(
        Guid id,
        Guid meetingId,
        Guid columnId,
        string? text,
        DateTimeOffset nowUtc) =>
        new(
            id,
            meetingId,
            columnId,
            RequiredText.Create(text, MaxTextLength),
            1,
            nowUtc,
            nowUtc);

    public Card UpdateText(string? text, DateTimeOffset nowUtc) =>
        this with
        {
            Text = RequiredText.Create(text, MaxTextLength),
            Version = Version + 1,
            UpdatedAtUtc = nowUtc,
        };
}
