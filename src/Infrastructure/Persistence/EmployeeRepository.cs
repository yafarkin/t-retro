using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence;

public sealed class EmployeeRepository(RelationalConnectionFactory connections) : IEmployeeRepository
{
    public async Task<Employee> GetOrCreateAsync(
        string subject,
        string displayName,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
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
                Id = Guid.NewGuid(),
                Subject = subject,
                DisplayName = displayName,
                Now = now,
            });

        return new Employee(row.Id, row.Subject, row.DisplayName);
    }

    private sealed class UserRow
    {
        public Guid Id { get; init; }

        public required string Subject { get; init; }

        public required string DisplayName { get; init; }
    }
}
