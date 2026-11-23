using Application;
using Infrastructure.Reporting;

namespace Api.Tests;

public sealed class FailingMeetingReportGenerator : IMeetingReportGenerator
{
    private readonly PdfSharpMeetingReportGenerator fallback = new();

    public bool ShouldFail { get; set; } = true;

    public byte[] Generate(MeetingStateData state) => ShouldFail
        ? throw new InvalidOperationException("Simulated PDF failure.")
        : fallback.Generate(state);
}
