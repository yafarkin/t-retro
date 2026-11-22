namespace Application;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ForbiddenException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
