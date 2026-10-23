using System.Security.Cryptography;

namespace SysMonitor.Core.Helpers;

/// <summary>
/// The check an elevated helper makes before acting: the list it was sent is the one whose SHA-256 the app put
/// on its command line, which nothing else can rewrite once Windows has asked the person.
/// </summary>
public static class ElevatedFingerprint
{
    public static string Of(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload));

    public static bool Matches(byte[] payload, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return false;

        try
        {
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), Convert.FromHexString(fingerprint.Trim()));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
