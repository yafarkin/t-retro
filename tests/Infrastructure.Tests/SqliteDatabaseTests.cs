using Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Tests;

public sealed class SqliteDatabaseTests : IDisposable
{
    private readonly string directory = Path.Combine(Environment.CurrentDirectory, "TestData", Guid.NewGuid().ToString());

    [Fact]
    public async Task MigrationIsRepeatableAndSubjectKeepsStableId()
    {
        var databasePath = Path.Combine(directory, "test.db");
        var connections = new SqliteConnectionFactory($"Data Source={databasePath}", directory);
        var migrator = new SqliteDatabaseMigrator(connections);

        await migrator.MigrateAsync(CancellationToken.None);
        await migrator.MigrateAsync(CancellationToken.None);

        var schemaVersions = new SqliteSchemaVersionReader(connections);
        Assert.Equal(migrator.LatestVersion, await schemaVersions.GetCurrentVersionAsync(CancellationToken.None));

        var repository = new SqliteEmployeeRepository(connections);
        var first = await repository.GetOrCreateAsync("employee-a", "Сотрудник А", CancellationToken.None);
        var second = await repository.GetOrCreateAsync("employee-a", "Новое имя", CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Новое имя", second.DisplayName);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
