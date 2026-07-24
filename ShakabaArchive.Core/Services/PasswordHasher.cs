using System.Security.Cryptography;
using System.Text;

namespace ShakabaArchive.Services;

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("shakaba|" + password));
        return Convert.ToHexString(bytes);
    }

    public static bool Verify(string password, string hash) =>
        string.Equals(Hash(password), hash, StringComparison.OrdinalIgnoreCase);
}
