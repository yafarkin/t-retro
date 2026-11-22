using Domain;

namespace Application;

public sealed class DevLoginService(IEmployeeRepository employees)
{
    public async Task<CurrentEmployee> LoginAsync(
        string? subject,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var normalizedSubject = RequiredText.Create(subject, 200);
        var normalizedDisplayName = RequiredText.Create(displayName, 120);
        var employee = await employees.GetOrCreateAsync(
            normalizedSubject,
            normalizedDisplayName,
            cancellationToken);

        return new CurrentEmployee(employee.Id, employee.Subject, employee.DisplayName);
    }
}
