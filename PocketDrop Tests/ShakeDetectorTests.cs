using PocketDrop;
using Xunit;
using static PocketDrop.AppHelpers;

public class ShakeDetectorTests
{
    [Fact]
    public void CheckForShake_ShouldReturnTrue_WhenMouseIsShakenRapidly()
    {
        // Arrange
        var detector = new ShakeDetector();
        int minDistance = 50;
        int maxTime = 1000;   // 1 second window
        long time = 100;      // Start at 100ms

        // Act & Assert
        // Start at X=0
        detector.CheckForShake(0, time, minDistance, maxTime);

        // Swing Right to X=100 (Distance: 100)
        time += 50; // 50ms later...
        detector.CheckForShake(100, time, minDistance, maxTime);

        // Swing Left to X=-10 (Distance: 110) - SWING 1 RECORDED!
        time += 50;
        detector.CheckForShake(-10, time, minDistance, maxTime);

        // Swing Right to X=100 (Distance: 110) - SWING 2 RECORDED!
        time += 50;
        detector.CheckForShake(100, time, minDistance, maxTime);

        // Swing Left to X=-10 (Distance: 110) - SWING 3 RECORDED! (Should Trigger)
        time += 50;
        bool result = detector.CheckForShake(-10, time, minDistance, maxTime);

        // It must detect the 3 rapid swings!
        Assert.True(result);
    }

    [Fact]
    public void CheckForShake_ShouldReturnFalse_WhenMovementsAreTooSmall()
    {
        // Arrange
        var detector = new ShakeDetector();
        int minDistance = 100; // Requires huge swings
        long time = 100;

        // Act
        detector.CheckForShake(0, time, minDistance, 1000);

        // Only swinging by 20 pixels (Fails the minDistance check)
        time += 50; detector.CheckForShake(20, time, minDistance, 1000);
        time += 50; detector.CheckForShake(-20, time, minDistance, 1000);
        time += 50; detector.CheckForShake(20, time, minDistance, 1000);
        time += 50; bool result = detector.CheckForShake(-20, time, minDistance, 1000);

        // Assert
        Assert.False(result); // Should not trigger!
    }

    [Fact]
    public void CheckForShake_ShouldReturnFalse_WhenMovementsAreTooSlow()
    {
        // Arrange
        var detector = new ShakeDetector();
        int minDistance = 50;
        int maxTime = 500; // Only allows half a second to finish the shake
        long time = 100;

        // Act
        detector.CheckForShake(0, time, minDistance, maxTime);

        // User swings, but waits 600ms between each swing
        time += 600; detector.CheckForShake(100, time, minDistance, maxTime);
        time += 600; detector.CheckForShake(-10, time, minDistance, maxTime);
        time += 600; detector.CheckForShake(100, time, minDistance, maxTime);
        time += 600; bool result = detector.CheckForShake(-10, time, minDistance, maxTime);

        // Assert
        Assert.False(result); // Should not trigger because the old swings "expired"
    }
}