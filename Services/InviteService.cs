using System.Security.Cryptography;
using System.Text;

namespace TruthOrDare.Api.Services;

public static class InviteService
{
    public static string CreatePublicCode()
    {
        var number = RandomNumberGenerator.GetInt32(100000, 999999);
        return number.ToString();
    }

    public static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
