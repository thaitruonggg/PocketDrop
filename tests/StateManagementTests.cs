using System.Collections.Generic;
using Xunit;
using PocketDrop;

public class StateManagementTests
{
    [Fact]
    public void IsDuplicate_ShouldReturnTrue_WhenFileAlreadyExists()
    {
        // Arrange: Create a fake memory list
        var mockPocket = new List<PocketItem>
        {
            new PocketItem { FilePath = @"C:\Downloads\report.pdf", FileName = "report.pdf" },
            new PocketItem { FilePath = @"C:\Images\photo.jpg", FileName = "photo.jpg" }
        };

        // Act: Try to drop the exact same file path again using the new AppHelpers class
        bool result = AppHelpers.IsDuplicate(mockPocket, @"C:\Downloads\report.pdf");

        // Assert: It MUST flag as a duplicate
        Assert.True(result);
    }

    [Fact]
    public void IsDuplicate_ShouldReturnFalse_WhenFileIsNew()
    {
        // Arrange
        var mockPocket = new List<PocketItem>
        {
            new PocketItem { FilePath = @"C:\Downloads\report.pdf", FileName = "report.pdf" }
        };

        // Act: Drop a completely different file
        bool result = AppHelpers.IsDuplicate(mockPocket, @"C:\Downloads\invoice.pdf");

        // Assert: It should allow the file
        Assert.False(result);
    }

    [Fact]
    public void IsDuplicate_ShouldIgnoreCapitalizationDifferences()
    {
        // Arrange
        var mockPocket = new List<PocketItem>
        {
            new PocketItem { FilePath = @"C:\Downloads\Report.pdf" } // Capital R
        };

        // Act: Drop the same file but with lowercase letters (Windows treats these as the same file)
        bool result = AppHelpers.IsDuplicate(mockPocket, @"c:\downloads\report.pdf");

        // Assert: It must still flag it as a duplicate
        Assert.True(result);
    }
}