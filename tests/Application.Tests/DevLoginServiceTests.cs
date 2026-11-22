using Domain;

namespace Application.Tests;

public sealed class DevLoginServiceTests
{
    [Fact]
    public async Task LoginNormalizesIdentityBeforeStorage()
    {
        var repository = new FakeEmployeeRepository();
        var service = new DevLoginService(repository);

        await service.LoginAsync("  employee-a  ", "  Сотрудник А  ", CancellationToken.None);

        Assert.Equal("employee-a", repository.Subject);
        Assert.Equal("Сотрудник А", repository.DisplayName);
    }

    private sealed class FakeEmployeeRepository : IEmployeeRepository
    {
        public string? Subject { get; private set; }

        public string? DisplayName { get; private set; }

        public Task<Employee> GetOrCreateAsync(
            string subject,
            string displayName,
            CancellationToken cancellationToken)
        {
            Subject = subject;
            DisplayName = displayName;
            return Task.FromResult(new Employee(Guid.NewGuid(), subject, displayName));
        }
    }
}
