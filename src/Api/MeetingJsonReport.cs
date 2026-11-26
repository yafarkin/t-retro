using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Application;
using Domain;

namespace Api;

internal static class MeetingJsonReport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic),
        WriteIndented = true,
    };

    public static byte[] Serialize(MeetingSnapshot snapshot)
    {
        var totals = snapshot.Completion!;
        var discussion = snapshot.Discussion!;
        var columnPositions = snapshot.Columns.ToDictionary(column => column.Id, column => column.Position);
        var topics = snapshot.Topics.ToDictionary(topic => topic.Id);
        var cards = snapshot.Cards.ToDictionary(card => card.Id);
        var cardsByGroup = snapshot.Cards.ToLookup(card => card.GroupId);
        var groups = snapshot.Groups.ToDictionary(group => group.Id);
        var comments = discussion.Comments.ToLookup(comment => comment.TopicId);
        var decisions = discussion.Decisions.ToLookup(decision => decision.TopicId);
        var actions = discussion.Actions.ToLookup(action => action.TopicId);

        var report = new Report(
            snapshot.Meeting.Name,
            snapshot.Meeting.CreatedAtUtc,
            totals.ClosedAtUtc,
            totals.ParticipantCount,
            totals.CardCount,
            totals.TopicCount,
            totals.VoteCount,
            snapshot.Columns.OrderBy(column => column.Position).Select(column => new ReportColumn(
                column.Position, column.Name, column.Color, column.Description)).ToArray(),
            discussion.Queue.OrderBy(item => item.Position).Select(item =>
            {
                var topic = topics[item.TopicId];
                var group = topic.Kind == TopicKind.Group ? groups[topic.SourceId] : null;
                var topicCards = group is null ? [cards[topic.SourceId]] : cardsByGroup[group.Id];
                var columnId = group?.ColumnId ?? cards[topic.SourceId].ColumnId;
                return new ReportTopic(
                    topic.Kind.ToString().ToLowerInvariant(),
                    group?.Name ?? cards[topic.SourceId].Text!,
                    columnPositions[columnId],
                    topic.TotalVotes!.Value,
                    item.Status.ToString().ToLowerInvariant(),
                    topicCards.Select(card => new ReportCard(card.Text!, card.OriginalColumnColor)).ToArray(),
                    comments[topic.Id].Select(comment => comment.Text).ToArray(),
                    decisions[topic.Id].Select(decision => decision.Text).ToArray(),
                    actions[topic.Id].Select(action => new ReportTask(action.Text, action.Description)).ToArray());
            }).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
    }

    private sealed record Report(
        string Name,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ClosedAtUtc,
        int ParticipantCount,
        int CardCount,
        int TopicCount,
        int VoteCount,
        IReadOnlyList<ReportColumn> Columns,
        IReadOnlyList<ReportTopic> Topics);

    private sealed record ReportColumn(int Position, string Name, string Color, string? Description);

    private sealed record ReportTopic(
        string Kind,
        string Title,
        int ColumnPosition,
        int TotalVotes,
        string Status,
        IReadOnlyList<ReportCard> Cards,
        IReadOnlyList<string> Comments,
        IReadOnlyList<string> Decisions,
        IReadOnlyList<ReportTask> Tasks);

    private sealed record ReportCard(string Text, string? OriginalColumnColor);

    private sealed record ReportTask(string Title, string? Description);
}
