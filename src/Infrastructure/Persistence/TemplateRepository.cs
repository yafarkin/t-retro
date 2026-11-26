using System.Data;
using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence;

public sealed class TemplateRepository(RelationalConnectionFactory connections) : ITemplateRepository
{
    public async Task<IReadOnlyList<TemplateDetails>> ListAccessibleAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connections.BeginSnapshotAsync(connection, cancellationToken);
        var rows = (await connection.QueryAsync<TemplateRow>(
            """
            SELECT * FROM templates
            WHERE owner_id = @UserId OR visibility = 'Shared'
            ORDER BY name, id;
            """,
            new { UserId = userId }, transaction)).ToArray();
        if (rows.Length == 0)
        {
            return [];
        }

        var columns = (await connection.QueryAsync<TemplateColumnRow>(
            """
            SELECT c.* FROM template_columns c
            JOIN templates t ON t.id = c.template_id
            WHERE t.owner_id = @UserId OR t.visibility = 'Shared'
            ORDER BY c.position;
            """,
            new { UserId = userId }, transaction)).ToLookup(column => column.TemplateId);
        return rows.Select(row => new TemplateDetails(
            MapTemplate(row),
            columns[row.Id].Select(MapColumn).ToArray())).ToArray();
    }

    public async Task<TemplateDetails?> FindAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connections.BeginSnapshotAsync(connection, cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TemplateRow>(
            "SELECT * FROM templates WHERE id = @TemplateId;",
            new { TemplateId = templateId }, transaction);
        if (row is null)
        {
            return null;
        }

        var columns = await connection.QueryAsync<TemplateColumnRow>(
            "SELECT * FROM template_columns WHERE template_id = @TemplateId ORDER BY position;",
            new { TemplateId = templateId }, transaction);
        return new TemplateDetails(MapTemplate(row), columns.Select(MapColumn).ToArray());
    }

    public async Task CreateAsync(
        MeetingTemplate template,
        IReadOnlyList<TemplateColumn> columns,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertTemplateAsync(connection, transaction, template);
        await InsertColumnsAsync(connection, transaction, columns);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(
        MeetingTemplate template,
        IReadOnlyList<TemplateColumn> columns,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            """
            UPDATE templates
            SET name = @Name, visibility = @Visibility, updated_at_utc = @UpdatedAtUtc
            WHERE id = @Id AND owner_id = @OwnerId;
            """,
            new
            {
                template.Name,
                Visibility = template.Visibility.ToString(),
                UpdatedAtUtc = template.UpdatedAtUtc,
                Id = template.Id,
                OwnerId = ownerId,
            },
            transaction);
        if (affected == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(
            "DELETE FROM template_columns WHERE template_id = @TemplateId;",
            new { TemplateId = template.Id },
            transaction);
        await InsertColumnsAsync(connection, transaction, columns);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(
        Guid templateId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            "DELETE FROM templates WHERE id = @TemplateId AND owner_id = @OwnerId;",
            new { TemplateId = templateId, OwnerId = ownerId });
        return affected > 0;
    }

    private static async Task InsertTemplateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        MeetingTemplate template)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO templates (id, owner_id, name, visibility, created_at_utc, updated_at_utc)
            VALUES (@Id, @OwnerId, @Name, @Visibility, @CreatedAtUtc, @UpdatedAtUtc);
            """,
            new
            {
                Id = template.Id,
                OwnerId = template.OwnerId,
                template.Name,
                Visibility = template.Visibility.ToString(),
                CreatedAtUtc = template.CreatedAtUtc,
                UpdatedAtUtc = template.UpdatedAtUtc,
            },
            transaction);
    }

    private static async Task InsertColumnsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyList<TemplateColumn> columns)
    {
        foreach (var column in columns)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO template_columns (template_id, id, name, color, position, description)
                VALUES (@TemplateId, @Id, @Name, @Color, @Position, @Description);
                """,
                new
                {
                    TemplateId = column.TemplateId,
                    Id = column.Id,
                    column.Name,
                    column.Color,
                    column.Position,
                    column.Description,
                },
                transaction);
        }
    }

    private static MeetingTemplate MapTemplate(TemplateRow row) =>
        new(
            row.Id,
            row.OwnerId,
            row.Name,
            Enum.Parse<TemplateVisibility>(row.Visibility),
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static TemplateColumn MapColumn(TemplateColumnRow row) =>
        new(
            row.Id,
            row.TemplateId,
            row.Name,
            row.Color,
            row.Position,
            row.Description);

    private sealed class TemplateRow
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        public required string Name { get; init; }
        public required string Visibility { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
    }

    private sealed class TemplateColumnRow
    {
        public Guid TemplateId { get; init; }
        public Guid Id { get; init; }
        public required string Name { get; init; }
        public required string Color { get; init; }
        public string? Description { get; init; }
        public int Position { get; init; }
    }
}
