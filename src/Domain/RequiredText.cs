namespace Domain;

public static class RequiredText
{
    public static string Create(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new DomainValidationException("TextRequired", "Введите текст.");
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException(
                "TextTooLong",
                $"Текст должен содержать не более {maxLength} символов.");
        }

        return normalized;
    }
}
