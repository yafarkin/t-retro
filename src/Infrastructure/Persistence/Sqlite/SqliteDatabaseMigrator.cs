using Application;
using Dapper;
using Infrastructure.Persistence.Migrations;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteDatabaseMigrator(SqliteConnectionFactory connections) : IDatabaseMigrator
{
    private static readonly IReadOnlyList<Migration> Migrations = EmbeddedMigrations.Load("SQLite");

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
}
