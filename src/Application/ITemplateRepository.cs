using Domain;

namespace Application;

public interface ITemplateRepository
{
    Task<IReadOnlyList<TemplateDetails>> ListAccessibleAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<TemplateDetails?> FindAsync(Guid templateId, CancellationToken cancellationToken);

    Task CreateAsync(
        MeetingTemplate template,
        IReadOnlyList<TemplateColumn> columns,
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        MeetingTemplate template,
        IReadOnlyList<TemplateColumn> columns,
        Guid ownerId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid templateId, Guid ownerId, CancellationToken cancellationToken);
}
