using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Api.Tests;

public sealed class ApiTestDatabase : IDisposable
{
    private readonly string? directory;
    private readonly string? schema;
    private bool disposed;

    public ApiTestDatabase(bool allowPostgreSql = true)
    {
        var postgres = Environment.GetEnvironmentVariable("TRETRO_TEST_POSTGRES");
        if (allowPostgreSql && !string.IsNullOrWhiteSpace(postgres))
        {
            schema = $"t_retro_test_{Guid.NewGuid():N}";
            var settings = new NpgsqlConnectionStringBuilder(postgres) { SearchPath = schema, Pooling = false };
            ConnectionString = settings.ConnectionString;
            ExecuteSchemaCommand($"CREATE SCHEMA {schema};");
        }
        else
        {
            directory = Path.Combine(Environment.CurrentDirectory, "TestData", Guid.NewGuid().ToString());
            ConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "test.db"),
                ForeignKeys = true,
            }.ConnectionString;
        }
    }

    public bool IsPostgreSql => schema is not null;

    public string ConnectionString { get; }

    public DbConnection CreateConnection() =>
        IsPostgreSql ? new NpgsqlConnection(ConnectionString) : new SqliteConnection(ConnectionString);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (schema is not null)
        {
            ExecuteSchemaCommand($"DROP SCHEMA {schema} CASCADE;");
        }
        else if (Directory.Exists(directory))
        {
            using var connection = new SqliteConnection(ConnectionString);
            SqliteConnection.ClearPool(connection);
            Directory.Delete(directory, true);
        }
    }

    private void ExecuteSchemaCommand(string sql)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
