using System.Data;
using System.Data.Common;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public abstract class RelationalConnectionFactory
{
    static RelationalConnectionFactory()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.RemoveTypeMap(typeof(Guid?));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
        SqlMapper.AddTypeHandler(new GuidMapping());
        SqlMapper.AddTypeHandler(new TimestampMapping());
    }

    public abstract Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);

    public abstract ValueTask<DbTransaction> BeginSnapshotAsync(
        DbConnection connection, CancellationToken cancellationToken);

    public abstract ValueTask<DbTransaction> BeginMeetingTransactionAsync(
        DbConnection connection, Guid meetingId, CancellationToken cancellationToken);

    private sealed class GuidMapping : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value) => value is string text ? Guid.Parse(text) : (Guid)value;

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            if (parameter is SqliteParameter)
            {
                parameter.DbType = DbType.String;
                parameter.Value = value.ToString();
                return;
            }

            parameter.DbType = DbType.Guid;
            parameter.Value = value;
        }
    }

    private sealed class TimestampMapping : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTime timestamp => new DateTimeOffset(timestamp),
            _ => (DateTimeOffset)value,
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            if (parameter is SqliteParameter)
            {
                parameter.DbType = DbType.String;
                parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);
                return;
            }

            parameter.DbType = DbType.DateTimeOffset;
            parameter.Value = value.ToUniversalTime();
        }
    }
}
