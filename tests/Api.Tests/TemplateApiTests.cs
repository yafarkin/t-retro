using System.Net;
using System.Net.Http.Json;
using Api.Contracts;

namespace Api.Tests;

public sealed class TemplateApiTests
{
    [Fact]
    public async Task SharedTemplateCopiesColumnsAndPersonalTemplateStaysPrivate()
    {
        using var factory = new TRetroWebApplicationFactory();
        await ApiTestHelpers.MigrateAsync(factory);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await ApiTestHelpers.LoginAsync(firstClient, "employee-a", "Сотрудник А");
        await ApiTestHelpers.LoginAsync(secondClient, "employee-b", "Сотрудник Б");

        var personal = await CreateTemplateAsync(firstClient, "Личный", "personal");
        var shared = await CreateTemplateAsync(firstClient, "Общий", "shared");

        var secondCatalog = await secondClient.GetFromJsonAsync<TemplateResponse[]>("/api/templates");
        Assert.DoesNotContain(secondCatalog!, template => template.Id == personal.Id);
        var sharedTemplate = Assert.Single(secondCatalog!, template => template.Id == shared.Id);

        var createMeeting = ApiTestHelpers.DefaultMeeting("Встреча Б");
        createMeeting.Columns = sharedTemplate.Columns
            .Select(column => new ColumnRequest { Name = column.Name, Color = column.Color })
            .ToArray();
        var createResponse = await secondClient.PostAsJsonAsync("/api/meetings", createMeeting);
        createResponse.EnsureSuccessStatusCode();
        var meeting = await createResponse.Content.ReadFromJsonAsync<MeetingCreatedResponse>();

        var replacement = ApiTestHelpers.DefaultMeeting("Изменённый шаблон").Columns.Reverse().ToArray();
        replacement[0].Color = "#000000";
        var updateResponse = await firstClient.PutAsJsonAsync(
            $"/api/templates/{shared.Id}",
            new TemplateRequest { Name = "Изменённый", Visibility = "shared", Columns = replacement });
        updateResponse.EnsureSuccessStatusCode();

        var state = await secondClient.GetFromJsonAsync<MeetingStateResponse>(
            $"/api/meetings/{meeting!.Id}/state");
        Assert.Equal(sharedTemplate.Columns.Select(column => column.Name), state!.Columns.Select(column => column.Name));
        Assert.Equal(sharedTemplate.Columns.Select(column => column.Color), state.Columns.Select(column => column.Color));
        Assert.Equal(sharedTemplate.Columns.Select(column => column.Position), state.Columns.Select(column => column.Position));

        var forbiddenUpdate = await secondClient.PutAsJsonAsync(
            $"/api/templates/{shared.Id}",
            new TemplateRequest { Name = "Чужой шаблон", Visibility = "personal", Columns = replacement });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpdate.StatusCode);

        var forbiddenDelete = await secondClient.DeleteAsync($"/api/templates/{shared.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);
        var deleteResponse = await firstClient.DeleteAsync($"/api/templates/{shared.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var firstCatalog = await firstClient.GetFromJsonAsync<TemplateResponse[]>("/api/templates");
        Assert.Equal(personal.Id, Assert.Single(firstCatalog!).Id);
    }

    private static async Task<TemplateCreatedResponse> CreateTemplateAsync(
        HttpClient client,
        string name,
        string visibility)
    {
        var response = await client.PostAsJsonAsync(
            "/api/templates",
            new TemplateRequest
            {
                Name = name,
                Visibility = visibility,
                Columns = ApiTestHelpers.DefaultMeeting(name).Columns,
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TemplateCreatedResponse>())!;
    }
}
