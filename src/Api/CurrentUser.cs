using System.Security.Claims;

namespace Api;

public static class CurrentUser
{
    public static Guid Id(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue("employee_id")!);
}
