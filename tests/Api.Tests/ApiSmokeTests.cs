using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Application;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

public sealed class ApiSmokeTests
{
    [Fact]
    public async Task ReadinessDependsOnMigrations()
    {
        using var factory = new TRetroWebApplicationFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/startup")).StatusCode);

        await Task.WhenAll(ApiTestHelpers.MigrateAsync(factory), ApiTestHelpers.MigrateAsync(factory));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        await using var connection = factory.Database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(factory.Database.IsPostgreSql ? 1L : 15L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task DevLoginSupportsIndependentEmployeesAndStableSubject()
    {
        using var factory = new TRetroWebApplicationFactory();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>().MigrateAsync(CancellationToken.None);
        }

        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var firstLogin = await firstClient.PostAsJsonAsync(
            "/api/dev/login",
            new DevLoginRequest("employee-a", "Сотрудник А"));
        var secondLogin = await secondClient.PostAsJsonAsync(
            "/api/dev/login",
            new DevLoginRequest("employee-b", "Сотрудник Б"));

        firstLogin.EnsureSuccessStatusCode();
        secondLogin.EnsureSuccessStatusCode();
        var first = await firstLogin.Content.ReadFromJsonAsync<EmployeeResponse>();
        var second = await secondLogin.Content.ReadFromJsonAsync<EmployeeResponse>();
        Assert.NotEqual(first!.Id, second!.Id);

        var repeatedLogin = await firstClient.PostAsJsonAsync(
            "/api/dev/login",
            new DevLoginRequest("employee-a", "Новое имя"));
        var repeated = await repeatedLogin.Content.ReadFromJsonAsync<EmployeeResponse>();
        Assert.Equal(first.Id, repeated!.Id);
        Assert.Equal("Новое имя", repeated.DisplayName);

        var me = await firstClient.GetFromJsonAsync<EmployeeResponse>("/api/me");
        Assert.Equal(repeated, me);
    }

    [Fact]
    public async Task AnonymousMeReturnsStructuredUnauthorizedError()
    {
        using var factory = new TRetroWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("Unauthorized", error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
    }

    [Fact]
    public void ProductionRejectsSqlite()
    {
        using var factory = new TRetroWebApplicationFactory("Production");

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }
}
