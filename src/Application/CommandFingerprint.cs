using System.Security.Cryptography;
using System.Text;

namespace Application;

internal static class CommandFingerprint
{
    public static string Create(params object?[] values)
    {
        var input = string.Join('\u001f', values.Select(value => value?.ToString() ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
