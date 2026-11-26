using System.Data;
using Application;
using Dapper;
using Infrastructure.Persistence.Migrations;

namespace Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlDatabase(RelationalConnectionFactory connections) : IDatabaseMigrator, ISchemaVersionReader
{
    private static readonly Migration[] Migrations = EmbeddedMigrations.Load("PostgreSQL");
    private const string MigrationTableExists = "SELECT to_regclass('schema_migrations') IS NOT NULL;";

    public int LatestVersion => Migrations[^1].Version;

    public async Task<int> GetCurrentVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            MigrationTableExists, cancellationToken: cancellationToken))
            ? await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;", cancellationToken: cancellationToken))
            : 0;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // Serialize migration jobs in this database until the transaction commits or rolls back.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(6841186061605872681);", transaction: transaction, cancellationToken: cancellationToken));
        var applied = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            MigrationTableExists, transaction: transaction, cancellationToken: cancellationToken))
            ? (await connection.QueryAsync<int>(new CommandDefinition(
                "SELECT version FROM schema_migrations;", transaction: transaction, cancellationToken: cancellationToken))).ToHashSet()
            : [];

        foreach (var migration in Migrations.Where(migration => !applied.Contains(migration.Version)))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                migration.Sql, transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_migrations (version, name, applied_at_utc) VALUES (@Version, @Name, @Now);",
                new { migration.Version, migration.Name, Now = DateTimeOffset.UtcNow }, transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
