using System.Data;
using System.Data.Common;
using Dapper;
using Npgsql;

namespace Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlConnectionFactory : RelationalConnectionFactory, IDisposable, IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;

    public PostgreSqlConnectionFactory(string connectionString)
    {
        NpgsqlConnectionStringBuilder settings;
        try
        {
            settings = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("ConnectionStrings:Default is not a valid PostgreSQL connection string.");
        }

        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Database)
            || string.IsNullOrWhiteSpace(settings.Username))
        {
            throw new InvalidOperationException("The PostgreSQL connection string requires Host, Database and Username.");
        }

        dataSource = NpgsqlDataSource.Create(settings.ConnectionString);
    }

    public override async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken) =>
        await dataSource.OpenConnectionAsync(cancellationToken);

    public override ValueTask<DbTransaction> BeginSnapshotAsync(
        DbConnection connection, CancellationToken cancellationToken) =>
        connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

    public override async ValueTask<DbTransaction> BeginMeetingTransactionAsync(
        DbConnection connection, Guid meetingId, CancellationToken cancellationToken)
    {
        var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT id FROM meetings WHERE id = @MeetingId FOR UPDATE;",
                new { MeetingId = meetingId }, transaction, cancellationToken: cancellationToken));
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public void Dispose() => dataSource.Dispose();

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
