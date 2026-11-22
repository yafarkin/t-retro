namespace Domain;

public sealed class DomainValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
