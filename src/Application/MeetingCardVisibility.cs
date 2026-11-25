using Domain;

namespace Application;

public static class MeetingCardVisibility
{
    public static MeetingCard Project(Meeting meeting, CardData data, Guid viewerId)
    {
        var isMine = data.AuthorId == viewerId;
        var showText = isMine
            || meeting.Phase != MeetingPhase.Collect
            || !meeting.PrivateCollection;
        var showAuthor = showText && !meeting.AnonymousCards && !data.Card.Anonymous;
        return new MeetingCard(
            data.Card.Id,
            data.Card.ColumnId,
            showText ? data.Card.Text : null,
            showAuthor ? data.AuthorId : null,
            showAuthor ? data.AuthorDisplayName : null,
            isMine,
            !showText,
            data.Card.Version,
            data.Card.GroupId,
            showText ? data.OriginalColumnColor : null);
    }
}
