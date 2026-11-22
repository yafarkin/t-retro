using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteEmployeeRepository(SqliteConnectionFactory connections) : IEmployeeRepository
{
    public async Task<Employee> GetOrCreateAsync(
        string subject,
        string displayName,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var row = await connection.QuerySingleAsync<UserRow>(
            """
            INSERT INTO users (id, subject, display_name, created_at_utc, updated_at_utc)
            VALUES (@Id, @Subject, @DisplayName, @Now, @Now)
            ON CONFLICT(subject) DO UPDATE SET
                display_name = excluded.display_name,
                updated_at_utc = excluded.updated_at_utc
            RETURNING id AS Id, subject AS Subject, display_name AS DisplayName;
            """,
            new
            {
                Id = Guid.NewGuid().ToString(),
                Subject = subject,
                DisplayName = displayName,
                Now = now,
            });

        return new Employee(Guid.Parse(row.Id), row.Subject, row.DisplayName);
    }

    private sealed class UserRow
    {
        public required string Id { get; init; }

        public required string Subject { get; init; }

        public required string DisplayName { get; init; }
    }
}
