using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence.Sqlite;

public sealed class SqliteConnectionFactory : RelationalConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRootPath, builder.DataSource));
        }

        builder.ForeignKeys = true;
        builder.DefaultTimeout = 5;
        this.connectionString = builder.ToString();
    }

    public override async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource != ":memory:")
        {
            var directory = Path.GetDirectoryName(builder.DataSource);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public override ValueTask<DbTransaction> BeginSnapshotAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<DbTransaction>(((SqliteConnection)connection).BeginTransaction(deferred: true));
    }

    public override ValueTask<DbTransaction> BeginMeetingTransactionAsync(
        DbConnection connection, Guid meetingId, CancellationToken cancellationToken) =>
        connection.BeginTransactionAsync(cancellationToken);
}
