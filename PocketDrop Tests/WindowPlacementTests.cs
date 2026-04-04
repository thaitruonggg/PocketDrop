using System.Windows;
using Xunit;
using PocketDrop;

public class WindowPlacementTests
{
    // A mock 1080p monitor and standard window size for our tests
    private const double WindowWidth = 380;
    private const double WindowHeight = 500;
    private const double ScreenLeft = 0;
    private const double ScreenTop = 0;
    private const double ScreenRight = 1920;
    private const double ScreenBottom = 1080;

    [Fact]
    public void CalculateWindowPosition_ShouldPlaceTopLeft_WhenMode5()
    {
        // Act: Mode 5 is "Top Left Corner"
        Point result = AppHelpers.CalculateWindowPosition(
            placementMode: 5,
            cursorX: 1000, cursorY: 500, // Cursor position shouldn't matter for corner snaps
            WindowWidth, WindowHeight,
            ScreenLeft, ScreenTop, ScreenRight, ScreenBottom);

        // Assert: Should lock exactly 8 pixels off the top left edge
        Assert.Equal(8, result.X);
        Assert.Equal(8, result.Y);
    }

    [Fact]
    public void CalculateWindowPosition_ShouldPlaceBottomRight_WhenMode8()
    {
        // Act: Mode 8 is "Bottom Right Corner"
        Point result = AppHelpers.CalculateWindowPosition(
            placementMode: 8,
            cursorX: 1000, cursorY: 500,
            WindowWidth, WindowHeight,
            ScreenLeft, ScreenTop, ScreenRight, ScreenBottom);

        // Assert: Screen edge (1920) - Window Width (380) - padding (8) = 1532
        Assert.Equal(1532, result.X);
        // Screen bottom (1080) - Window Height (500) - padding (8) = 572
        Assert.Equal(572, result.Y);
    }

    [Fact]
    public void CalculateWindowPosition_ShouldPlaceNearMouse_WhenMode0()
    {
        // Arrange: Mouse is floating comfortably in the middle of the screen
        double mouseX = 1000;
        double mouseY = 1000;

        // Act: Mode 0 is the default "Near Mouse" setting
        Point result = AppHelpers.CalculateWindowPosition(
            0, mouseX, mouseY,
            WindowWidth, WindowHeight,
            ScreenLeft, ScreenTop, ScreenRight, ScreenBottom);

        // Assert: Math says -> X: Cursor (1000) - HalfWidth (190) + Nudge (40) = 850
        Assert.Equal(850, result.X);

        // Assert: Math says -> Y: Cursor (1000) - Height (500) - Nudge (80) = 420
        Assert.Equal(420, result.Y);
    }

    [Fact]
    public void CalculateWindowPosition_ShouldClampToScreen_WhenMouseIsNearTopLeftEdge()
    {
        // Arrange: Mouse is jammed into the absolute top-left pixel of the monitor!
        double mouseX = 0;
        double mouseY = 0;

        // Act: Run the "Near Mouse" calculation
        Point result = AppHelpers.CalculateWindowPosition(
            0, mouseX, mouseY,
            WindowWidth, WindowHeight,
            ScreenLeft, ScreenTop, ScreenRight, ScreenBottom);

        // Assert: The raw math would normally push it to X=-150 and Y=-580 (completely off screen).
        // Our test proves the Math.Max clamp catches it and forces it back onto the screen!
        Assert.Equal(8, result.X);
        Assert.Equal(8, result.Y);
    }

    // ══════════════════════════════════════════════════════
    // TASKBAR SNAP TESTS (SavedPocketsWindow)
    // ══════════════════════════════════════════════════════

    [Fact]
    public void CalculateTaskbarSnapPosition_ShouldOffsetShadow_On1080pMonitor()
    {
        // Arrange: A standard 1080p monitor and a 300x400 window with a 15px shadow
        double workAreaW = 1920;
        double workAreaH = 1080;
        double windowW = 300;
        double windowH = 400;
        double shadowMargin = 15;

        // Act
        Point result = AppHelpers.CalculateTaskbarSnapPosition(
            windowW, windowH, workAreaW, workAreaH, shadowMargin);

        // Assert: Math says -> Left: Screen (1920) - Window (300) + Shadow (15) = 1635
        Assert.Equal(1635, result.X);

        // Assert: Math says -> Top: Screen (1080) - Window (400) + Shadow (15) = 695
        Assert.Equal(695, result.Y);
    }

    [Fact]
    public void CalculateTaskbarSnapPosition_ShouldWorkCorrectly_WithZeroShadow()
    {
        // Arrange: Testing the edge case where you remove the shadow entirely in the future
        double workAreaW = 2560; // 1440p Monitor
        double workAreaH = 1440;
        double windowW = 500;
        double windowH = 600;
        double shadowMargin = 0; // No shadow!

        // Act
        Point result = AppHelpers.CalculateTaskbarSnapPosition(
            windowW, windowH, workAreaW, workAreaH, shadowMargin);

        // Assert: Left -> 2560 - 500 + 0 = 2060
        Assert.Equal(2060, result.X);

        // Assert: Top -> 1440 - 600 + 0 = 840
        Assert.Equal(840, result.Y);
    }
}