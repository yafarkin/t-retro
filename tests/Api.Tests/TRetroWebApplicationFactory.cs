using Api;
using Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

public sealed class TRetroWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string environment;
    private readonly bool ownsDatabase;
    private readonly bool aiEnabled;
    private readonly bool jiraEnabled;
    private readonly string jiraOutcome;
    private readonly int jiraDelayMs;

    public TRetroWebApplicationFactory(
        string environment = "Testing",
        ApiTestDatabase? database = null,
        bool aiEnabled = true,
        bool jiraEnabled = true,
        string jiraOutcome = "Created",
        int jiraDelayMs = 0,
        bool failPdf = false)
    {
        this.environment = environment;
        this.aiEnabled = aiEnabled;
        this.jiraEnabled = jiraEnabled;
        this.jiraOutcome = jiraOutcome;
        this.jiraDelayMs = jiraDelayMs;
        ownsDatabase = database is null;
        Database = database ?? new ApiTestDatabase(allowPostgreSql: environment == "Testing");
        if (failPdf)
        {
            FailingReportGenerator = new FailingMeetingReportGenerator();
        }
    }

    public ApiTestDatabase Database { get; }

    public TestTimeProvider Time { get; } = new();

    public FailingMeetingReportGenerator? FailingReportGenerator { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", Database.ConnectionString);
        builder.UseSetting("Database:Provider", Database.IsPostgreSql ? "PostgreSQL" : "SQLite");
        builder.UseSetting("DevAuthentication:Enabled", environment == "Testing" ? "true" : "false");
        builder.UseSetting("Integrations:Ai:Enabled", aiEnabled ? "true" : "false");
        builder.UseSetting("Integrations:Jira:Enabled", jiraEnabled ? "true" : "false");
        builder.UseSetting("Integrations:Jira:Outcome", jiraOutcome);
        builder.UseSetting("Integrations:Jira:DelayMs", jiraDelayMs.ToString());
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            if (FailingReportGenerator is not null)
            {
                services.AddSingleton<IMeetingReportGenerator>(FailingReportGenerator);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && ownsDatabase)
        {
            Database.Dispose();
        }
    }
}
