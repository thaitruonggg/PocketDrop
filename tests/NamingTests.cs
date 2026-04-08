using System.Collections.Generic;
using Xunit;
using PocketDrop;

public class NamingTests
{
    [Fact]
    public void GetSafeDisplayName_ShouldReturnOriginalName_WhenNoCollision()
    {
        // Arrange
        var mockPocket = new List<PocketItem>(); // Empty pocket
        string inputPath = @"C:\Downloads\report.pdf";

        // Act
        string result = AppHelpers.GetSafeDisplayName(mockPocket, inputPath);

        // Assert
        Assert.Equal("report.pdf", result);
    }

    [Fact]
    public void GetSafeDisplayName_ShouldAppendNumber_WhenCollisionExists()
    {
        // Arrange
        var mockPocket = new List<PocketItem>
        {
            new PocketItem { FileName = "report.pdf" }
        };
        string inputPath = @"C:\Downloads\report.pdf";

        // Act
        string result = AppHelpers.GetSafeDisplayName(mockPocket, inputPath);

        // Assert
        Assert.Equal("report (1).pdf", result); // Must add the (1)
    }

    [Fact]
    public void GetSafeDisplayName_ShouldIncrementNumber_WhenMultipleCollisionsExist()
    {
        // Arrange
        var mockPocket = new List<PocketItem>
        {
            new PocketItem { FileName = "report.pdf" },
            new PocketItem { FileName = "report (1).pdf" }
        };
        string inputPath = @"C:\Downloads\report.pdf";

        // Act
        string result = AppHelpers.GetSafeDisplayName(mockPocket, inputPath);

        // Assert
        Assert.Equal("report (2).pdf", result); // Must skip to (2)
    }

    [Fact]
    public void GetSafeDisplayName_ShouldTreatDomainsAsExtensions()
    {
        // Arrange
        var mockPocket = new List<PocketItem>
        {
            new PocketItem { FileName = "github.com" }
        };

        // Act: Simulating dropping a second github link
        string result = AppHelpers.GetSafeDisplayName(mockPocket, "github.com");

        // Assert: It should correctly identify .com as the extension
        Assert.Equal("github (1).com", result);
    }
}