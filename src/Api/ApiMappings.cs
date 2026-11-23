using Api.Contracts;
using Application;

namespace Api;

public static class ApiMappings
{
    public static MeetingStateResponse ToResponse(MeetingSnapshot snapshot) =>
        new(
            DateTimeOffset.UtcNow,
            snapshot.Meeting.Id.ToString(),
            snapshot.Meeting.Name,
            snapshot.Meeting.Phase.ToString(),
            snapshot.Meeting.Version,
            snapshot.Meeting.BoardVersion,
            snapshot.CanManage,
            snapshot.Meeting.FacilitatorId?.ToString(),
            snapshot.Meeting.AnonymousCards,
            snapshot.Meeting.PrivateCollection,
            snapshot.Meeting.AnonymousVotes,
            new VoteSettingsResponse(
                snapshot.Meeting.VoteSettings.Mode.ToString().ToLowerInvariant(),
                snapshot.Meeting.VoteSettings.Percent,
                snapshot.Meeting.VoteSettings.Minimum,
                snapshot.Meeting.VoteSettings.Fixed),
            snapshot.Columns.Select(column => new ColumnResponse(
                column.Id.ToString(),
                column.Name,
                column.Color,
                column.Position)).ToArray(),
            snapshot.Participants.Select(participant => new ParticipantResponse(
                participant.UserId.ToString(),
                participant.Subject,
                participant.DisplayName,
                participant.Status.ToString(),
                participant.IsOnline)).ToArray(),
            snapshot.Cards.Select(card => new MeetingCardResponse(
                card.Id.ToString(),
                card.ColumnId.ToString(),
                card.Text,
                card.AuthorId?.ToString(),
                card.AuthorDisplayName,
                card.IsMine,
                card.IsHidden,
                card.Version)).ToArray(),
            snapshot.Readiness is null
                ? null
                : new MeetingReadinessResponse(
                    snapshot.Readiness.ReadyCount,
                    snapshot.Readiness.TotalCount,
                    snapshot.Readiness.MyReady));

    public static ColumnInput ToInput(ColumnRequest column) => new(column.Name, column.Color);
}
