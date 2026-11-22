using Domain;

namespace Application;

public sealed class TemplateService(ITemplateRepository templates)
{
    public Task<IReadOnlyList<TemplateDetails>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        templates.ListAccessibleAsync(userId, cancellationToken);

    public async Task<Guid> CreateAsync(
        Guid userId,
        string? name,
        string visibility,
        IReadOnlyList<ColumnInput> columns,
        CancellationToken cancellationToken)
    {
        var templateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var template = new MeetingTemplate(
            templateId,
            userId,
            RequiredText.Create(name, 120),
            ParseVisibility(visibility),
            now,
            now);
        await templates.CreateAsync(template, CreateColumns(templateId, columns), cancellationToken);
        return templateId;
    }

    public async Task UpdateAsync(
        Guid userId,
        Guid templateId,
        string? name,
        string visibility,
        IReadOnlyList<ColumnInput> columns,
        CancellationToken cancellationToken)
    {
        var current = await FindAsync(templateId, cancellationToken);
        if (current.Template.OwnerId != userId)
        {
            throw new ForbiddenException("TemplateOwnerRequired", "Изменять шаблон может только владелец.");
        }

        var updated = current.Template with
        {
            Name = RequiredText.Create(name, 120),
            Visibility = ParseVisibility(visibility),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        if (!await templates.UpdateAsync(updated, CreateColumns(templateId, columns), userId, cancellationToken))
        {
            throw new ConflictException("VersionConflict", "Шаблон изменился. Обновите каталог и повторите сохранение.");
        }
    }

    public async Task DeleteAsync(
        Guid userId,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var current = await FindAsync(templateId, cancellationToken);
        if (current.Template.OwnerId != userId)
        {
            throw new ForbiddenException("TemplateOwnerRequired", "Удалять шаблон может только владелец.");
        }

        if (!await templates.DeleteAsync(templateId, userId, cancellationToken))
        {
            throw new NotFoundException("Шаблон не найден.");
        }
    }

    private async Task<TemplateDetails> FindAsync(Guid templateId, CancellationToken cancellationToken) =>
        await templates.FindAsync(templateId, cancellationToken)
        ?? throw new NotFoundException("Шаблон не найден.");

    private static IReadOnlyList<TemplateColumn> CreateColumns(
        Guid templateId,
        IReadOnlyList<ColumnInput> inputs)
    {
        if (inputs.Count == 0)
        {
            throw new DomainValidationException("ColumnsRequired", "Добавьте хотя бы одну колонку.");
        }

        return inputs.Select((input, position) =>
        {
            var (name, color) = ColumnRules.Validate(input.Name, input.Color);
            return new TemplateColumn(Guid.NewGuid(), templateId, name, color, position);
        }).ToArray();
    }

    private static TemplateVisibility ParseVisibility(string visibility)
    {
        if (!Enum.TryParse<TemplateVisibility>(visibility, true, out var parsed))
        {
            throw new DomainValidationException("InvalidTemplateVisibility", "Неизвестная видимость шаблона.");
        }

        return parsed;
    }
}
