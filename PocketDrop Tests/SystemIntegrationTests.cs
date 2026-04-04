using System;
using Xunit;
using PocketDrop;

public class SystemIntegrationTests
{
    // ══════════════════════════════════════════════════════
    // THEME CHECK TESTS
    // ══════════════════════════════════════════════════════

    [Fact]
    public void IsWindowsInDarkMode_ShouldExecuteWithoutCrashing()
    {
        // Act
        // We cannot test for 'true' or 'false' because we don't know what 
        // theme the developer running the test is currently using!
        // Instead, we just test that the registry call succeeds without throwing an exception.
        var exception = Record.Exception(() => AppHelpers.IsWindowsInDarkMode());

        // Assert
        Assert.Null(exception); // Proves the Registry access is safe
    }

    // ══════════════════════════════════════════════════════
    // STARTUP REGISTRY TESTS
    // ══════════════════════════════════════════════════════

    [Fact]
    public void SetRunAtStartup_ShouldAddAndRemoveRegistryKey()
    {
        // Arrange
        // 1. Save the developer's CURRENT startup state so we don't ruin their actual app preferences
        bool originalState = AppHelpers.IsRunAtStartupEnabled();
        string fakeExePath = @"C:\TestPath\FakePocketDrop.exe";

        try
        {
            // Act 1: Turn Startup ON
            bool enableSuccess = AppHelpers.SetRunAtStartup(true, fakeExePath);
            bool isEnabledNow = AppHelpers.IsRunAtStartupEnabled();

            // Assert 1
            Assert.True(enableSuccess);
            Assert.True(isEnabledNow);

            // Act 2: Turn Startup OFF
            bool disableSuccess = AppHelpers.SetRunAtStartup(false, fakeExePath);
            bool isOffNow = AppHelpers.IsRunAtStartupEnabled();

            // Assert 2
            Assert.True(disableSuccess);
            Assert.False(isOffNow);
        }
        finally
        {
            // Cleanup: No matter what happens (even if an Assert fails and crashes the test),
            // this block will ALWAYS run to restore the developer's computer to how it was!
            string realPath = Environment.ProcessPath ?? "";
            AppHelpers.SetRunAtStartup(originalState, realPath);
        }
    }
}