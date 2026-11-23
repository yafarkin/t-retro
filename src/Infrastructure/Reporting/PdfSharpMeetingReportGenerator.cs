using Application;
using Domain;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Infrastructure.Reporting;

public sealed class PdfSharpMeetingReportGenerator : IMeetingReportGenerator
{
    static PdfSharpMeetingReportGenerator()
    {
        GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
    }

    public byte[] Generate(MeetingStateData state)
    {
        var totals = state.Completion
            ?? throw new InvalidOperationException("Meeting completion totals are missing.");
        using var document = new PdfDocument();
        document.Info.Title = state.Meeting.Name;
        document.Info.Subject = "Итоги ретроспективы";
        document.Info.Creator = "T-Retro";
        document.Info.Author = string.Empty;
        var writer = new ReportWriter(document);
        writer.Title(state.Meeting.Name);
        writer.Paragraph($"Дата завершения: {totals.ClosedAtUtc:dd.MM.yyyy HH:mm} UTC");
        writer.Paragraph($"Участников: {totals.ParticipantCount}. Карточек: {totals.CardCount}. Тем: {totals.TopicCount}. Голосов: {totals.VoteCount}.");

        writer.Section("Участники");
        foreach (var participant in state.Participants)
        {
            var status = participant.Status == MemberStatus.Excluded ? ", исключён" : string.Empty;
            writer.Bullet(participant.DisplayName + status);
        }

        writer.Section("Колонки и карточки");
        foreach (var column in state.Columns)
        {
            writer.Subheading(column.Name);
            var cards = state.Cards.Where(card => card.Card.ColumnId == column.Id).ToArray();
            foreach (var card in cards.Where(card => card.Card.GroupId is null))
            {
                WriteCard(writer, state.Meeting, card);
            }

            foreach (var group in state.Groups.Where(group => group.ColumnId == column.Id))
            {
                writer.Paragraph($"Группа: {group.Name}", bold: true, indent: 10);
                foreach (var card in cards.Where(card => card.Card.GroupId == group.Id))
                {
                    WriteCard(writer, state.Meeting, card, 20);
                }
            }
        }

        writer.Section("Рейтинг и итоги обсуждения");
        var queue = state.DiscussionQueue.OrderBy(topic => topic.Position).ToArray();
        foreach (var queueItem in queue)
        {
            var topic = state.Topics.Single(item => item.Id == queueItem.TopicId);
            var total = state.TopicTotals!.Single(item => item.TopicId == topic.Id).TotalVotes;
            writer.Subheading($"{queueItem.Position + 1}. {TopicTitle(state, topic)}");
            writer.Paragraph($"Голосов: {total}. Статус: {StatusText(queueItem.Status)}.");
            if (!state.Meeting.AnonymousVotes)
            {
                foreach (var vote in state.Votes.Where(vote => vote.TopicId == topic.Id))
                {
                    var voter = state.Participants.Single(participant => participant.UserId == vote.UserId);
                    writer.Bullet($"Голос: {voter.DisplayName} — {vote.Count}", 10);
                }
            }

            foreach (var decision in state.Decisions.Where(item => item.TopicId == topic.Id))
            {
                writer.Paragraph($"Решение: {decision.Text}", indent: 10);
            }

            foreach (var comment in state.Comments.Where(item => item.TopicId == topic.Id))
            {
                writer.Paragraph($"Комментарий: {comment.Text}", indent: 10);
            }

            foreach (var action in state.Actions.Where(item => item.TopicId == topic.Id))
            {
                var assignee = action.AssigneeId is null
                    ? "без исполнителя"
                    : state.Participants.Single(participant => participant.UserId == action.AssigneeId).DisplayName;
                writer.Paragraph($"Задача: {action.Text}", indent: 10);
                writer.Paragraph($"Исполнитель: {assignee}", indent: 20);
            }
        }

        if (queue.Length == 0)
        {
            writer.Paragraph("Тем для обсуждения нет.");
        }

        writer.AddPageNumbers();
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static void WriteCard(ReportWriter writer, Meeting meeting, CardData card, double indent = 10)
    {
        writer.Bullet(card.Card.Text, indent);
        if (!meeting.AnonymousCards)
        {
            writer.Paragraph($"Автор: {card.AuthorDisplayName}", indent: indent + 10);
        }
    }

    private static string TopicTitle(MeetingStateData state, MeetingTopic topic) => topic.Kind == TopicKind.Group
        ? state.Groups.Single(group => group.Id == topic.SourceId).Name
        : state.Cards.Single(card => card.Card.Id == topic.SourceId).Card.Text;

    private static string StatusText(DiscussionStatus status) => status switch
    {
        DiscussionStatus.Pending => "ожидает",
        DiscussionStatus.Discussed => "обсуждена",
        DiscussionStatus.Skipped => "пропущена",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private sealed class ReportWriter(PdfDocument document)
    {
        private const double Margin = 42;
        private const double BottomMargin = 48;
        private readonly XFont titleFont = new(EmbeddedFontResolver.FamilyName, 18, XFontStyleEx.Bold);
        private readonly XFont sectionFont = new(EmbeddedFontResolver.FamilyName, 14, XFontStyleEx.Bold);
        private readonly XFont subheadingFont = new(EmbeddedFontResolver.FamilyName, 11, XFontStyleEx.Bold);
        private readonly XFont bodyFont = new(EmbeddedFontResolver.FamilyName, 10, XFontStyleEx.Regular);
        private readonly XFont boldBodyFont = new(EmbeddedFontResolver.FamilyName, 10, XFontStyleEx.Bold);
        private XGraphics? graphics;
        private double y;

        public void Title(string text) => Paragraph(text, titleFont, 0, 16);

        public void Section(string text)
        {
            EnsureSpace(sectionFont.Size * 3);
            y += 8;
            Paragraph(text, sectionFont, 0, 8);
        }

        public void Subheading(string text) => Paragraph(text, subheadingFont, 0, 5);

        public void Bullet(string text, double indent = 0) => Paragraph("• " + text, bodyFont, indent, 3);

        public void Paragraph(string text, bool bold = false, double indent = 0) =>
            Paragraph(text, bold ? boldBodyFont : bodyFont, indent, 3);

        public void AddPageNumbers()
        {
            graphics?.Dispose();
            graphics = null;
            for (var index = 0; index < document.Pages.Count; index++)
            {
                var page = document.Pages[index];
                using var pageGraphics = XGraphics.FromPdfPage(page);
                pageGraphics.DrawString(
                    $"Страница {index + 1} / {document.Pages.Count}",
                    bodyFont,
                    XBrushes.Black,
                    new XRect(
                        XUnit.FromPoint(Margin).Point,
                        XUnit.FromPoint(page.Height.Point - 32).Point,
                        XUnit.FromPoint(page.Width.Point - Margin * 2).Point,
                        XUnit.FromPoint(16).Point),
                    XStringFormats.Center);
            }
        }

        private void Paragraph(string text, XFont font, double indent, double spacingAfter)
        {
            var width = PageWidth - indent;
            foreach (var logicalLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var remaining = logicalLine;
                if (remaining.Length == 0)
                {
                    EnsureSpace(font.Size * 1.35);
                    y += font.Size * 1.35;
                    continue;
                }

                while (remaining.Length > 0)
                {
                    EnsureSpace(font.Size * 1.35);
                    var line = TakeLine(remaining, font, width);
                    graphics!.DrawString(
                        line,
                        font,
                        XBrushes.Black,
                        new XRect(
                            XUnit.FromPoint(Margin + indent).Point,
                            XUnit.FromPoint(y).Point,
                            XUnit.FromPoint(width).Point,
                            XUnit.FromPoint(font.Size * 1.35).Point),
                        XStringFormats.TopLeft);
                    y += font.Size * 1.35;
                    remaining = remaining[line.Length..].TrimStart();
                }
            }

            y += spacingAfter;
        }

        private string TakeLine(string text, XFont font, double width)
        {
            if (graphics!.MeasureString(text, font).Width <= width)
            {
                return text;
            }

            var low = 1;
            var high = text.Length;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (graphics.MeasureString(text[..middle], font).Width <= width)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            var breakAt = text.LastIndexOf(' ', low - 1);
            return breakAt > 0 ? text[..breakAt] : text[..low];
        }

        private double PageWidth => document.Pages.Count == 0
            ? XUnit.FromMillimeter(210).Point - Margin * 2
            : document.Pages[document.Pages.Count - 1].Width.Point - Margin * 2;

        private void EnsureSpace(double height)
        {
            if (graphics is null || y + height > document.Pages[document.Pages.Count - 1].Height.Point - BottomMargin)
            {
                AddPage();
            }
        }

        private void AddPage()
        {
            graphics?.Dispose();
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            y = Margin;
        }
    }
}
