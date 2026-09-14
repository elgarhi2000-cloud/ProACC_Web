using System.Security.Cryptography;
using System.Text;

namespace BlazorApp1.Services;

// Compatibility for existing USER records. New password storage needs a versioned,
// salted format and a coordinated schema migration before retiring legacy values.
public static class LegacyPasswordVerifier
{
    public static bool Verify(string stored, string supplied)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(supplied)) return false;
        Span<byte> decoded = stackalloc byte[32];
        var isHash = stored.Length == 44
            && Convert.TryFromBase64String(stored, decoded, out var length) && length == 32;
        var expected = isHash ? decoded.ToArray() : Encoding.UTF8.GetBytes(stored);
        var actual = isHash ? SHA256.HashData(Encoding.UTF8.GetBytes(supplied)) : Encoding.UTF8.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
