namespace Api.Contracts;

public sealed record ErrorResponse(string Code, string Message, string TraceId);

public sealed record DevLoginRequest(string? Subject, string? DisplayName);

public sealed record EmployeeResponse(string Id, string Subject, string DisplayName);

public sealed record HealthResponse(string Status);
