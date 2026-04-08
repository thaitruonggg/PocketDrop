using Xunit;
using PocketDrop;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1")] // Minor patch update
    [InlineData("1.0.0", "1.1.0")] // Feature update
    [InlineData("1.0.0", "2.0.0")] // Major update
    [InlineData("1.0.9", "1.0.10")] // Proves it treats it as numbers, not decimals (10 is bigger than 9)
    public void IsUpdateAvailable_ShouldReturnTrue_WhenOnlineIsNewer(string currentVer, string onlineVer)
    {
        // Act
        bool result = AppHelpers.IsUpdateAvailable(currentVer, onlineVer);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")] // Exactly the same
    [InlineData("1.1.0", "1.0.0")] // User is actually on a NEWER beta version than the server
    [InlineData("2.0.0", "1.9.9")]
    public void IsUpdateAvailable_ShouldReturnFalse_WhenOnlineIsOlderOrEqual(string currentVer, string onlineVer)
    {
        // Act
        bool result = AppHelpers.IsUpdateAvailable(currentVer, onlineVer);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsUpdateAvailable_ShouldReturnFalse_WhenGitHubReturnsGarbage()
    {
        // Arrange
        string currentVer = "1.0.0";
        // Imagine the user is on a hotel Wi-Fi that intercepts the request and returns an HTML login page instead of the version number
        string fakeGitHubResponse = "<!DOCTYPE html><html>Please login to Wi-Fi</html>";

        // Act
        bool result = AppHelpers.IsUpdateAvailable(currentVer, fakeGitHubResponse);

        // Assert
        // It must NOT crash the app, and it must safely decline the update.
        Assert.False(result);
    }
}