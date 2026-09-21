using FluentAssertions;
using SysMonitor.Core.Services.GameMode;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The overlay's position dropdown offers Top Left, Top Right, Bottom Left and Bottom Right. It used to do
/// nothing at all: <c>FpsOverlayWindow.Position</c> was an auto-property that was written and never read,
/// and the window was hard-placed at (100, 100) whatever the label said.
/// <para>
/// The arithmetic lives here, away from WinUI, so it can be checked: a display whose work area does not start
/// at the origin (a second monitor, or a taskbar on the left) is the case a hard-coded corner gets wrong.
/// </para>
/// </summary>
public class OverlayPlacementTests
{
    private static readonly OverlayBounds PrimaryDisplay = new(0, 0, 1920, 1040);

    private const int Width = 220;
    private const int Height = 260;

    [Fact]
    public void TopLeft_SitsAgainstTheTopLeftOfTheWorkArea()
    {
        var placed = OverlayPlacement.Place(OverlayPosition.TopLeft, PrimaryDisplay, Width, Height);

        placed.X.Should().Be(OverlayPlacement.Margin);
        placed.Y.Should().Be(OverlayPlacement.Margin);
    }

    [Fact]
    public void TopRight_SitsAgainstTheTopRightOfTheWorkArea()
    {
        var placed = OverlayPlacement.Place(OverlayPosition.TopRight, PrimaryDisplay, Width, Height);

        placed.X.Should().Be(1920 - Width - OverlayPlacement.Margin);
        placed.Y.Should().Be(OverlayPlacement.Margin);
    }

    [Fact]
    public void BottomLeft_SitsAgainstTheBottomLeftOfTheWorkArea()
    {
        var placed = OverlayPlacement.Place(OverlayPosition.BottomLeft, PrimaryDisplay, Width, Height);

        placed.X.Should().Be(OverlayPlacement.Margin);
        placed.Y.Should().Be(1040 - Height - OverlayPlacement.Margin);
    }

    [Fact]
    public void BottomRight_SitsAgainstTheBottomRightOfTheWorkArea()
    {
        var placed = OverlayPlacement.Place(OverlayPosition.BottomRight, PrimaryDisplay, Width, Height);

        placed.X.Should().Be(1920 - Width - OverlayPlacement.Margin);
        placed.Y.Should().Be(1040 - Height - OverlayPlacement.Margin);
    }

    [Fact]
    public void EveryCornerKeepsTheOverlayInsideTheWorkArea()
    {
        foreach (var position in Enum.GetValues<OverlayPosition>())
        {
            var placed = OverlayPlacement.Place(position, PrimaryDisplay, Width, Height);

            placed.X.Should().BeGreaterThanOrEqualTo(PrimaryDisplay.X, $"{position} must stay on screen");
            placed.Y.Should().BeGreaterThanOrEqualTo(PrimaryDisplay.Y, $"{position} must stay on screen");
            (placed.X + Width).Should().BeLessThanOrEqualTo(PrimaryDisplay.X + PrimaryDisplay.Width);
            (placed.Y + Height).Should().BeLessThanOrEqualTo(PrimaryDisplay.Y + PrimaryDisplay.Height);
        }
    }

    [Fact]
    public void ASecondMonitorLeftOfThePrimaryOne_GetsNegativeCoordinates()
    {
        // Windows gives a display to the left of the primary a negative X. A corner worked out from the
        // size alone lands on the wrong screen.
        var secondary = new OverlayBounds(-1920, 0, 1920, 1080);

        var placed = OverlayPlacement.Place(OverlayPosition.TopLeft, secondary, Width, Height);

        placed.X.Should().Be(-1920 + OverlayPlacement.Margin);
        placed.Y.Should().Be(OverlayPlacement.Margin);
    }

    [Fact]
    public void ATaskbarDownTheLeftHandSide_ShiftsTheWorkAreaAndTheOverlayWithIt()
    {
        var withSideTaskbar = new OverlayBounds(80, 0, 1840, 1080);

        var placed = OverlayPlacement.Place(OverlayPosition.BottomLeft, withSideTaskbar, Width, Height);

        placed.X.Should().Be(80 + OverlayPlacement.Margin);
        placed.Y.Should().Be(1080 - Height - OverlayPlacement.Margin);
    }

    [Fact]
    public void AWorkAreaSmallerThanTheOverlay_PutsItAtTheTopLeftRatherThanOffScreen()
    {
        var tiny = new OverlayBounds(0, 0, 100, 100);

        var placed = OverlayPlacement.Place(OverlayPosition.BottomRight, tiny, Width, Height);

        placed.X.Should().Be(0, "an overlay wider than the screen starts at its edge, not past it");
        placed.Y.Should().Be(0);
    }

    [Fact]
    public void ThePlacementKeepsTheSizeItWasGiven()
    {
        var placed = OverlayPlacement.Place(OverlayPosition.TopRight, PrimaryDisplay, Width, Height);

        placed.Width.Should().Be(Width);
        placed.Height.Should().Be(Height);
    }
}
