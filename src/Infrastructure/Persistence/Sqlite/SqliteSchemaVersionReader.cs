using Application;
using Dapper;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteSchemaVersionReader(SqliteConnectionFactory connections) : ISchemaVersionReader
{
    public async Task<int> GetCurrentVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var tableExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_migrations';");
        if (tableExists == 0)
        {
            return 0;
        }

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
    }
}
