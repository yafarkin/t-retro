namespace Application;

public interface IDatabaseMigrator
{
    int LatestVersion { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}
