using System.Globalization;

namespace Infrastructure.Persistence.Sqlite;

internal static class SqliteMapping
{
    public static DateTimeOffset Time(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? NullableTime(string? value) =>
        value is null ? null : Time(value);
}
