using System.Text.RegularExpressions;

namespace Domain;

public static partial class ColumnRules
{
    public static (string Name, string Color) Validate(string? name, string? color)
    {
        var normalizedName = RequiredText.Create(name, 120);
        var normalizedColor = color?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!ColorPattern().IsMatch(normalizedColor))
        {
            throw new DomainValidationException("InvalidColumnColor", "Цвет колонки должен быть в формате #RRGGBB.");
        }

        return (normalizedName, normalizedColor);
    }

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
