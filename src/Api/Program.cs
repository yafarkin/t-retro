using System.Security.Claims;
using Api;
using Api.Contracts;
using Application;
using Infrastructure.Persistence.Sqlite;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Api;

public sealed partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        var migrate = args.Contains("--migrate", StringComparer.Ordinal);
        var applicationArgs = args.Where(arg => arg != "--migrate").ToArray();
        var builder = WebApplication.CreateBuilder(applicationArgs);
        var isLocalEnvironment = builder.Environment.IsDevelopment()
            || builder.Environment.IsEnvironment("Testing");
        var devAuthenticationEnabled = builder.Configuration.GetValue<bool>("DevAuthentication:Enabled");

        if (devAuthenticationEnabled && !isLocalEnvironment)
        {
            throw new InvalidOperationException("Development authentication is allowed only in Development and Testing.");
        }

        var provider = builder.Configuration["Database:Provider"];
        if (!string.Equals(provider, "SQLite", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only the SQLite storage provider is implemented in the local MVP.");
        }

        if (!isLocalEnvironment)
        {
            throw new InvalidOperationException("SQLite is not allowed outside Development and Testing. Configure PostgreSQL for production.");
        }

        var connectionString = builder.Configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Default is required.");
        }

        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton(new SqliteConnectionFactory(connectionString, builder.Environment.ContentRootPath));
        builder.Services.AddScoped<IEmployeeRepository, SqliteEmployeeRepository>();
        builder.Services.AddScoped<ISchemaVersionReader, SqliteSchemaVersionReader>();
        builder.Services.AddScoped<IDatabaseMigrator, SqliteDatabaseMigrator>();
        builder.Services.AddScoped<IMeetingRepository, SqliteMeetingRepository>();
        builder.Services.AddScoped<ITemplateRepository, SqliteTemplateRepository>();
        builder.Services.AddScoped<DevLoginService>();
        builder.Services.AddScoped<MeetingAccessService>();
        builder.Services.AddScoped<MeetingService>();
        builder.Services.AddScoped<TemplateService>();
        builder.Services.AddScoped<InvitationService>();
        builder.Services.AddScoped<MemberService>();
        builder.Services.AddScoped<FacilitatorElectionService>();
        builder.Services.AddSingleton<IPresenceService, Infrastructure.Presence.InMemoryPresenceService>();
        builder.Services.AddScoped<IMeetingNotifier, SignalRMeetingNotifier>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSignalR();
        builder.Services.AddHostedService<Infrastructure.Background.FacilitatorElectionWorker>();
        builder.Services.AddDataProtection();
        builder.Services.AddSingleton<IInvitationTokenService, Infrastructure.Security.DataProtectionInvitationTokenService>();
        builder.Services.AddSingleton<StartupState>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "t-retro.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                    ApiError.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized", "Требуется вход.");
                options.Events.OnRedirectToAccessDenied = context =>
                    ApiError.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "Forbidden", "Недостаточно прав.");
            });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        if (isLocalEnvironment)
        {
            app.MapOpenApi();
        }

        app.MapMeetingEndpoints();
        app.MapTemplateEndpoints();
        app.MapMemberEndpoints();
        app.MapHub<RetroHub>("/hubs/retro");
        app.MapGet("/", () => Results.Ok(new { Name = "T-Retro API" }));
        app.MapGet("/health/live", () => Results.Ok(new HealthResponse("ok"))).AllowAnonymous();
        app.MapGet("/health/startup", (StartupState state) =>
                state.IsStarted
                    ? Results.Ok(new HealthResponse("ok"))
                    : Results.Json(new HealthResponse("unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous();
        app.MapGet("/health/ready", async (
                ISchemaVersionReader schemaVersions,
                IDatabaseMigrator migrator,
                ILogger<Program> logger,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var currentVersion = await schemaVersions.GetCurrentVersionAsync(cancellationToken);
                    return currentVersion == migrator.LatestVersion
                        ? Results.Ok(new HealthResponse("ok"))
                        : Results.Json(new HealthResponse("unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch
                {
                    logger.LogWarning("Readiness check failed.");
                    return Results.Json(new HealthResponse("unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .AllowAnonymous();

        app.MapGet("/api/me", (ClaimsPrincipal user) =>
            {
                var response = new EmployeeResponse(
                    user.FindFirstValue("employee_id")!,
                    user.FindFirstValue("subject")!,
                    user.FindFirstValue(ClaimTypes.Name)!);
                return Results.Ok(response);
            })
            .RequireAuthorization();

        if (devAuthenticationEnabled)
        {
            app.MapPost("/api/dev/login", async (
                    DevLoginRequest request,
                    HttpContext context,
                    DevLoginService loginService,
                    CancellationToken cancellationToken) =>
                {
                    var employee = await loginService.LoginAsync(request.Subject, request.DisplayName, cancellationToken);
                    var claims = new[]
                    {
                        new Claim("employee_id", employee.Id.ToString()),
                        new Claim("subject", employee.Subject),
                        new Claim(ClaimTypes.Name, employee.DisplayName),
                    };
                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await context.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(identity));
                    return Results.Ok(new EmployeeResponse(
                        employee.Id.ToString(),
                        employee.Subject,
                        employee.DisplayName));
                })
                .AllowAnonymous();

            app.MapPost("/api/dev/logout", async (HttpContext context) =>
                {
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return Results.NoContent();
                })
                .RequireAuthorization();
        }

        if (migrate)
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>().MigrateAsync(CancellationToken.None);
            return 0;
        }

        var startupState = app.Services.GetRequiredService<StartupState>();
        app.Lifetime.ApplicationStarted.Register(() => startupState.IsStarted = true);
        await app.RunAsync();
        return 0;
    }
}
