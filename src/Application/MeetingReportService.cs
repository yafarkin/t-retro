namespace Application;

public sealed class MeetingReportService(
    MeetingAccessService access,
    IMeetingCompletionRepository completion)
{
    public async Task<byte[]> GetAsync(
        Guid userId,
        Guid meetingId,
        CancellationToken cancellationToken)
    {
        await access.GetAsync(userId, meetingId, false, cancellationToken);
        return await completion.GetReportAsync(meetingId, cancellationToken)
            ?? throw new NotFoundException("Отчёт будет доступен после завершения встречи.");
    }
}
