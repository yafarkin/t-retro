using System.Text.RegularExpressions;
using Application;
using Dapper;

namespace Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteDatabaseMigrator(SqliteConnectionFactory connections) : IDatabaseMigrator
{
    private static readonly IReadOnlyList<Migration> Migrations = LoadMigrations();

    public int LatestVersion => Migrations.Max(migration => migration.Version);

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var tableExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_migrations';",
            transaction);
        var applied = tableExists == 0
            ? []
            : (await connection.QueryAsync<int>(
                "SELECT version FROM schema_migrations;",
                transaction)).ToHashSet();

        foreach (var migration in Migrations.Where(migration => !applied.Contains(migration.Version)))
        {
            await connection.ExecuteAsync(migration.Sql, transaction);
            await connection.ExecuteAsync(
                "INSERT INTO schema_migrations (version, name, applied_at_utc) VALUES (@Version, @Name, @Now);",
                new
                {
                    migration.Version,
                    migration.Name,
                    Now = DateTimeOffset.UtcNow.ToString("O"),
                },
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static IReadOnlyList<Migration> LoadMigrations()
    {
        var assembly = typeof(SqliteDatabaseMigrator).Assembly;
        return assembly.GetManifestResourceNames()
            .Select(resourceName => (ResourceName: resourceName, Match: MigrationPattern().Match(resourceName)))
            .Where(item => item.Match.Success)
            .Select(item =>
            {
                using var stream = assembly.GetManifestResourceStream(item.ResourceName)
                    ?? throw new InvalidOperationException("Migration resource is unavailable.");
                using var reader = new StreamReader(stream);
                return new Migration(
                    int.Parse(item.Match.Groups["version"].Value),
                    item.Match.Groups["name"].Value,
                    reader.ReadToEnd());
            })
            .OrderBy(migration => migration.Version)
            .ToArray();
    }

    [GeneratedRegex(@"(?<version>\d+)_(?<name>[^.]+)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationPattern();

    private sealed record Migration(int Version, string Name, string Sql);
}
