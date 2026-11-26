using Domain;

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

    public async Task<MeetingSnapshot> GetCompletedSnapshotAsync(
        Guid userId,
        Guid meetingId,
        CancellationToken cancellationToken)
    {
        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        if (snapshot.Meeting.Phase != MeetingPhase.Closed || snapshot.Completion is null)
        {
            throw new NotFoundException("Отчёт будет доступен после завершения встречи.");
        }

        return snapshot;
    }
}
