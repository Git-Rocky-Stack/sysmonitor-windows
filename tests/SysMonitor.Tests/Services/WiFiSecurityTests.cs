using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The Wi-Fi page turns the security string into a padlock. The analyser had three places that filled it in
/// with <c>"WPA2" // Assumed</c> when the platform would not say — and the page's test for a padlock was
/// "not empty and not Open", so every one of those networks was shown to the user as encrypted on no
/// evidence at all.
/// <para>
/// A user deciding whether to send something over a network is entitled to know the difference between
/// "encrypted", "not encrypted" and "we could not tell".
/// </para>
/// </summary>
public class WiFiSecurityTests
{
    [Theory]
    [InlineData("WPA2-Personal")]
    [InlineData("WPA3")]
    [InlineData("WPA2")]
    [InlineData("WEP")]   // badly encrypted is still encrypted; the padlock is not a quality rating
    public void AnEncryptedNetwork_IsSecured(string security)
    {
        WiFiSecurity.Describe(security).Should().Be(WiFiSecurityState.Secured);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("open")]
    [InlineData("None")]
    public void AnOpenNetwork_IsOpen(string security)
    {
        WiFiSecurity.Describe(security).Should().Be(WiFiSecurityState.Open);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    public void ANetworkWeCouldNotRead_IsNotClaimedToBeEitherThing(string? security)
    {
        WiFiSecurity.Describe(security).Should().Be(WiFiSecurityState.Unknown,
            "a padlock the app cannot justify is worse than no padlock");
    }

    /// <summary>
    /// The fallback paths in the analyser must not put a security value in that the platform never gave
    /// them. This reads the source because the paths only run on hardware the test machine may not have.
    /// </summary>
    [Fact]
    public void TheAnalyserNeverWritesASecurityValueItWasNotTold()
    {
        var source = File.ReadAllText(Path.Combine(RepoSource.Root,
            "src/SysMonitor.Core/Services/Utilities/WiFiAnalyzer.cs".Replace('/', Path.DirectorySeparatorChar)));

        var assumed = source
            .Split('\n')
            .Select((line, index) => (Line: line.Trim(), Number: index + 1))
            .Where(entry => entry.Line.StartsWith("Security =", StringComparison.Ordinal))
            .Where(entry => entry.Line.Contains("\"WPA", StringComparison.OrdinalIgnoreCase)
                         || entry.Line.Contains("\"WEP", StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"WiFiAnalyzer.cs:{entry.Number}  {entry.Line}")
            .ToList();

        assumed.Should().BeEmpty(
            "a hard-coded encryption type is a claim about a network nobody checked");
    }
}
