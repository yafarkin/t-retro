namespace Application;

public interface ISchemaVersionReader
{
    Task<int> GetCurrentVersionAsync(CancellationToken cancellationToken);
}
