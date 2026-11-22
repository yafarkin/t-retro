using Domain;

namespace Application;

public interface IEmployeeRepository
{
    Task<Employee> GetOrCreateAsync(
        string subject,
        string displayName,
        CancellationToken cancellationToken);
}
