using System.Security.Claims;
using Api.Contracts;
using Application;

namespace Api;

public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this WebApplication app)
    {
        var templates = app.MapGroup("/api/templates").RequireAuthorization();
        templates.MapGet("/", async (
            ClaimsPrincipal user,
            TemplateService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(CurrentUser.Id(user), cancellationToken);
            return Results.Ok(result.Select(ToResponse));
        });
        templates.MapPost("/", async (
            TemplateRequest request,
            ClaimsPrincipal user,
            TemplateService service,
            CancellationToken cancellationToken) =>
        {
            var id = await service.CreateAsync(
                CurrentUser.Id(user),
                request.Name,
                request.Visibility,
                request.Columns.Select(ApiMappings.ToInput).ToArray(),
                cancellationToken);
            return Results.Created($"/api/templates/{id}", new TemplateCreatedResponse(id.ToString()));
        });
        templates.MapPut("/{templateId:guid}", async (
            Guid templateId,
            TemplateRequest request,
            ClaimsPrincipal user,
            TemplateService service,
            CancellationToken cancellationToken) =>
        {
            await service.UpdateAsync(
                CurrentUser.Id(user),
                templateId,
                request.Name,
                request.Visibility,
                request.Columns.Select(ApiMappings.ToInput).ToArray(),
                cancellationToken);
            return Results.NoContent();
        });
        templates.MapDelete("/{templateId:guid}", async (
            Guid templateId,
            ClaimsPrincipal user,
            TemplateService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(CurrentUser.Id(user), templateId, cancellationToken);
            return Results.NoContent();
        });
    }

    private static TemplateResponse ToResponse(TemplateDetails details) =>
        new(
            details.Template.Id.ToString(),
            details.Template.OwnerId.ToString(),
            details.Template.Name,
            details.Template.Visibility.ToString().ToLowerInvariant(),
            details.Columns.Select(column => new ColumnResponse(
                column.Id.ToString(),
                column.Name,
                column.Color,
                column.Position)).ToArray());
}
