using System.Globalization;

namespace Infrastructure.Persistence.Migrations;

internal sealed record Migration(int Version, string Name, string Sql);

internal static class EmbeddedMigrations
{
    public static Migration[] Load(string provider)
    {
        var assembly = typeof(EmbeddedMigrations).Assembly;
        var prefix = $"{typeof(EmbeddedMigrations).Namespace}.{provider}.";
        return assembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(prefix, StringComparison.Ordinal) && resource.EndsWith(".sql", StringComparison.Ordinal))
            .Select(resource =>
            {
                var file = resource[prefix.Length..^4];
                var separator = file.IndexOf('_');
                using var stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException("Migration resource is unavailable.");
                using var reader = new StreamReader(stream);
                return new Migration(
                    int.Parse(file.AsSpan(0, separator), CultureInfo.InvariantCulture),
                    file[(separator + 1)..],
                    reader.ReadToEnd());
            })
            .OrderBy(migration => migration.Version)
            .ToArray();
    }
}
