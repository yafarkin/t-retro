using Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

public sealed class TRetroWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string directory;
    private readonly string environment;
    private readonly bool deleteOnDispose;
    private readonly bool aiEnabled;

    public TRetroWebApplicationFactory(
        string environment = "Testing",
        string? databasePath = null,
        bool aiEnabled = true)
    {
        this.environment = environment;
        this.aiEnabled = aiEnabled;
        deleteOnDispose = databasePath is null;
        DatabasePath = databasePath ?? Path.Combine(
            Environment.CurrentDirectory,
            "TestData",
            Guid.NewGuid().ToString(),
            "test.db");
        directory = Path.GetDirectoryName(DatabasePath)!;
    }

    public string DatabasePath { get; }

    public TestTimeProvider Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DatabasePath}");
        builder.UseSetting("Database:Provider", "SQLite");
        builder.UseSetting("DevAuthentication:Enabled", environment == "Testing" ? "true" : "false");
        builder.UseSetting("Integrations:Ai:Enabled", aiEnabled ? "true" : "false");
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Time));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (deleteOnDispose && Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
