using Domain;

namespace Application;

public sealed class JiraExportService(
    IJiraExportRepository exports,
    IJiraIssueExporter exporter,
    MeetingAccessService access,
    IMeetingNotifier notifier,
    TimeProvider timeProvider)
{
    public async Task<JiraExportCommandView> ExportAsync(
        Guid userId,
        Guid meetingId,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        if (!exporter.IsAvailable)
        {
            throw new IntegrationUnavailableException("Mock Jira отключён в конфигурации.");
        }

        await access.GetAsync(userId, meetingId, true, cancellationToken);
        var begun = await exports.BeginAsync(
            meetingId,
            actionId,
            userId,
            Guid.NewGuid(),
            timeProvider.GetUtcNow(),
            cancellationToken);
        switch (begun.Status)
        {
            case MeetingCommandStatus.NotFound:
                throw new NotFoundException("Задача не найдена.");
            case MeetingCommandStatus.Forbidden:
                throw new ForbiddenException("FacilitatorRequired", "Экспорт доступен только фасилитатору.");
            case MeetingCommandStatus.InvalidPhase:
                throw new ConflictException("InvalidPhase", "Экспорт доступен во время обсуждения или после завершения.");
            case MeetingCommandStatus.VersionConflict:
                throw new ConflictException("VersionConflict", "Состояние изменилось. Обновите данные и повторите действие.");
        }

        var export = begun.Export
            ?? throw new InvalidOperationException("Export intention is missing.");
        if (!begun.ShouldExport)
        {
            return new JiraExportCommandView(export, begun.Version);
        }

        await notifier.StateChangedAsync(meetingId, begun.Version, cancellationToken);
        JiraExportResult result;
        try
        {
            result = await exporter.CreateAsync(begun.Request!, cancellationToken);
        }
        catch
        {
            result = new JiraExportResult(JiraExportResultStatus.Unknown);
        }

        var completed = await exports.CompleteAsync(
            export,
            result,
            timeProvider.GetUtcNow(),
            cancellationToken);
        var snapshot = await access.GetAsync(userId, meetingId, false, cancellationToken);
        await notifier.StateChangedAsync(meetingId, snapshot.Meeting.Version, cancellationToken);
        return new JiraExportCommandView(completed, snapshot.Meeting.Version);
    }
}
