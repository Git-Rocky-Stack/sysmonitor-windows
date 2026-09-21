namespace SysMonitor.Core.Services.Utilities;

/// <summary>What is known about whether a network is encrypted.</summary>
public enum WiFiSecurityState
{
    /// <summary>Nothing is known. Not the same as open, and it must never be shown as a padlock.</summary>
    Unknown,

    /// <summary>The network carries no encryption: anything sent over it can be read by anyone in range.</summary>
    Open,

    /// <summary>The network is encrypted.</summary>
    Secured,
}

/// <summary>
/// Reads the security of a network from whatever the platform managed to tell us about it.
///
/// <para>
/// This exists because the analyser used to fill the gap with <c>"WPA2" // Assumed</c> when it could not
/// read the real value. The interface turns a non-empty, non-"Open" security string into a green padlock,
/// so a network whose encryption was never established was shown to the user as encrypted. That is a
/// security statement the app had no basis for, and the only safe version of it is "not known".
/// </para>
/// </summary>
public static class WiFiSecurity
{
    /// <summary>The value to record when the platform would not say.</summary>
    public const string UnknownLabel = "Unknown";

    /// <summary>The value the platform uses for a network with no encryption.</summary>
    public const string OpenLabel = "Open";

    public static WiFiSecurityState Describe(string? security)
    {
        if (string.IsNullOrWhiteSpace(security))
            return WiFiSecurityState.Unknown;

        var trimmed = security.Trim();

        if (trimmed.Equals(UnknownLabel, StringComparison.OrdinalIgnoreCase))
            return WiFiSecurityState.Unknown;

        if (trimmed.Equals(OpenLabel, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return WiFiSecurityState.Open;
        }

        return WiFiSecurityState.Secured;
    }
}
