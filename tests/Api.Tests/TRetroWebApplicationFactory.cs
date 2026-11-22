using Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Api.Tests;

public sealed class TRetroWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Environment.CurrentDirectory, "TestData", Guid.NewGuid().ToString());
    private readonly string environment;

    public TRetroWebApplicationFactory(string environment = "Testing")
    {
        this.environment = environment;
        DatabasePath = Path.Combine(directory, "test.db");
    }

    public string DatabasePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DatabasePath}");
        builder.UseSetting("Database:Provider", "SQLite");
        builder.UseSetting("DevAuthentication:Enabled", environment == "Testing" ? "true" : "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
