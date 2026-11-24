using System.Data;
using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteTemplateRepository(SqliteConnectionFactory connections) : ITemplateRepository
{
    public async Task<IReadOnlyList<TemplateDetails>> ListAccessibleAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<TemplateRow>(
            """
            SELECT * FROM templates
            WHERE owner_id = @UserId OR visibility = 'Shared'
            ORDER BY name, id;
            """,
            new { UserId = userId.ToString() })).ToArray();
        if (rows.Length == 0)
        {
            return [];
        }

        var columns = await GetColumnsAsync(
            connection,
            null,
            rows.Select(row => row.Id).ToArray());
        return rows.Select(row => new TemplateDetails(
            MapTemplate(row),
            columns.Where(column => column.TemplateId == row.Id)
                .OrderBy(column => column.Position)
                .Select(MapColumn)
                .ToArray())).ToArray();
    }

    public async Task<TemplateDetails?> FindAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TemplateRow>(
            "SELECT * FROM templates WHERE id = @TemplateId;",
            new { TemplateId = templateId.ToString() });
        if (row is null)
        {
            return null;
        }

        var columns = await GetColumnsAsync(connection, null, [row.Id]);
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
                UpdatedAtUtc = template.UpdatedAtUtc.ToString("O"),
                Id = template.Id.ToString(),
                OwnerId = ownerId.ToString(),
            },
            transaction);
        if (affected == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(
            "DELETE FROM template_columns WHERE template_id = @TemplateId;",
            new { TemplateId = template.Id.ToString() },
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
            new { TemplateId = templateId.ToString(), OwnerId = ownerId.ToString() });
        return affected > 0;
    }

    private static async Task<TemplateColumnRow[]> GetColumnsAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        IReadOnlyCollection<string> templateIds)
    {
        var rows = await connection.QueryAsync<TemplateColumnRow>(
            "SELECT * FROM template_columns WHERE template_id IN @TemplateIds ORDER BY position;",
            new { TemplateIds = templateIds },
            transaction);
        return rows.ToArray();
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
                Id = template.Id.ToString(),
                OwnerId = template.OwnerId.ToString(),
                template.Name,
                Visibility = template.Visibility.ToString(),
                CreatedAtUtc = template.CreatedAtUtc.ToString("O"),
                UpdatedAtUtc = template.UpdatedAtUtc.ToString("O"),
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
                    TemplateId = column.TemplateId.ToString(),
                    Id = column.Id.ToString(),
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
            Guid.Parse(row.Id),
            Guid.Parse(row.OwnerId),
            row.Name,
            Enum.Parse<TemplateVisibility>(row.Visibility),
            SqliteMapping.Time(row.CreatedAtUtc),
            SqliteMapping.Time(row.UpdatedAtUtc));

    private static TemplateColumn MapColumn(TemplateColumnRow row) =>
        new(
            Guid.Parse(row.Id),
            Guid.Parse(row.TemplateId),
            row.Name,
            row.Color,
            row.Position,
            row.Description);

    private sealed class TemplateRow
    {
        public required string Id { get; init; }
        public required string OwnerId { get; init; }
        public required string Name { get; init; }
        public required string Visibility { get; init; }
        public required string CreatedAtUtc { get; init; }
        public required string UpdatedAtUtc { get; init; }
    }

    private sealed class TemplateColumnRow
    {
        public required string TemplateId { get; init; }
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Color { get; init; }
        public string? Description { get; init; }
        public int Position { get; init; }
    }
}
