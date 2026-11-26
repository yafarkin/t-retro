using System.Data.Common;
using System.Net.Http.Json;
using Api.Contracts;
using Application;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Api.Tests;

public static class ApiTestHelpers
{
    public static void AddId(this DbCommand command, string name, Guid id)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = command is NpgsqlCommand ? id : id.ToString();
        command.Parameters.Add(parameter);
    }

    public static async Task MigrateAsync(TRetroWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>().MigrateAsync(CancellationToken.None);
    }

    public static async Task<EmployeeResponse> LoginAsync(
        HttpClient client,
        string subject,
        string displayName)
    {
        var response = await client.PostAsJsonAsync("/api/dev/login", new DevLoginRequest(subject, displayName));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeeResponse>())!;
    }

    public static async Task<MeetingCreatedResponse> CreateMeetingAsync(
        HttpClient client,
        string name = "Ретро команды")
    {
        var response = await client.PostAsJsonAsync("/api/meetings", DefaultMeeting(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeetingCreatedResponse>())!;
    }

    public static MeetingSettingsRequest DefaultMeeting(string name) =>
        new()
        {
            Name = name,
            Columns =
            [
                new ColumnRequest { Name = "Начать", Color = "#36A269" },
                new ColumnRequest { Name = "Перестать", Color = "#D34E4E" },
                new ColumnRequest { Name = "Продолжать", Color = "#4C86D9" },
            ],
        };

    public static string TokenFromUrl(string url) =>
        Uri.UnescapeDataString(url.Split("token=", 2)[1]);
}
