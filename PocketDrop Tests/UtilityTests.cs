using Xunit;
using PocketDrop;

public class UtilityTests
{
    [Fact]
    public void FormatBytes_ShouldReturnZero_WhenInputIsZero()
    {
        // Act
        string result = AppHelpers.FormatBytes(0);

        // Assert
        Assert.Equal("0 B", result);
    }

    [Theory]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1572864, "1.5 MB")] // Added a test for decimals!
    public void FormatBytes_ShouldCalculateScalesCorrectly(long inputBytes, string expectedOutput)
    {
        // Act
        string result = AppHelpers.FormatBytes(inputBytes);

        // Assert
        Assert.Equal(expectedOutput, result);
    }
}