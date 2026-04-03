using System.Collections.Generic;
using System.IO;
using Xunit;
using PocketDrop;

public class FileValidationTests
{
    [Fact]
    public void RemoveDeadFiles_ShouldRemoveGhostFiles_AndReturnTrue()
    {
        // Arrange
        // 1. Ask Windows to create a real temporary file on the hard drive
        string tempFilePath = Path.GetTempFileName();

        // 2. Put it in our fake PocketDrop memory
        var mockPocket = new List<PocketItem>
        {
            new PocketItem { FilePath = tempFilePath, FileName = "ghost_file.txt" }
        };

        // 3. Delete the physical file behind the app's back!
        File.Delete(tempFilePath);

        // Act
        // Run the JIT Validation
        bool result = AppHelpers.RemoveDeadFiles(mockPocket);

        // Assert
        Assert.True(result); // It must report that it found and fixed an error
        Assert.Empty(mockPocket); // The list must now be completely empty
    }

    [Fact]
    public void RemoveDeadFiles_ShouldKeepValidFiles_AndReturnFalse()
    {
        // Arrange
        string tempFilePath = Path.GetTempFileName();
        var mockPocket = new List<PocketItem>
        {
            new PocketItem { FilePath = tempFilePath, FileName = "real_file.txt" }
        };

        // Act
        // Run the JIT Validation WITHOUT deleting the file first
        bool result = AppHelpers.RemoveDeadFiles(mockPocket);

        // Assert
        Assert.False(result); // It should report no errors were found
        Assert.Single(mockPocket); // The file should still be safely inside the list

        // Cleanup: Delete the real test file so we don't clutter the developer's hard drive
        File.Delete(tempFilePath);
    }
}