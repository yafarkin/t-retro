using Application;
using Domain;
using Infrastructure.Reporting;

namespace Infrastructure.Tests;

public sealed class PdfSharpMeetingReportGeneratorTests
{
    [Fact]
    public void GeneratesPdfForEmptyMeeting()
    {
        var meetingId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var meeting = new Meeting(
            meetingId,
            "Пустое ретро",
            MeetingPhase.Discuss,
            true,
            true,
            VoteBudgetSettings.Default,
            0,
            null,
            1,
            1,
            1,
            now,
            now);
        var state = new MeetingStateData(
            meeting,
            [new MeetingColumn(Guid.NewGuid(), meetingId, "Начать", "#36A269", 0)],
            [],
            [],
            new HashSet<Guid>(),
            [],
            [],
            [],
            [],
            [],
            null,
            [],
            [],
            [],
            [],
            new MeetingCompletionTotals(now, 0, 0, 0, 0),
            []);

        var pdf = new PdfSharpMeetingReportGenerator().Generate(state);
        var restored = System.Text.Json.JsonSerializer.Deserialize<MeetingStateData>(
            System.Text.Json.JsonSerializer.Serialize(state));

        Assert.NotNull(restored);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
    }
}
