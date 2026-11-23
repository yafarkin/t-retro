using Application;
using Domain;

namespace Infrastructure.Integrations;

public sealed class StubGroupSuggestionProvider(bool enabled) : IGroupSuggestionProvider
{
    public bool IsAvailable => enabled;

    public Task<IReadOnlyList<SuggestedGroup>> SuggestGroupsAsync(
        GroupSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            throw new IntegrationUnavailableException("Предложения группировки отключены в конфигурации.");
        }

        var groups = request.FreeCards
            .GroupBy(card => card.ColumnId)
            .SelectMany(columnCards => columnCards
                .Chunk(2)
                .Where(chunk => chunk.Length == 2)
                .Select((chunk, index) => new SuggestedGroup(
                    $"Тестовая группа {index + 1}",
                    columnCards.Key,
                    chunk.Select(card => card.Id).ToArray())))
            .ToArray();
        return Task.FromResult<IReadOnlyList<SuggestedGroup>>(groups);
    }
}
