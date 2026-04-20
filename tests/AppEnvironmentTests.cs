using Xunit;
using PocketDrop;

namespace PocketDrop_Tests
{
    public class AppEnvironmentTests
    {
        [Fact]
        public void IsGameModeActive_ShouldExecuteWithoutCrashing()
        {
            // Act
            // Verify the shell32 DLL import doesn't throw a catastrophic exception
            // during the test execution in standard environments.
            var exception = Record.Exception(() => AppHelpers.IsGameModeActive());

            // Assert
            Assert.Null(exception); // Proves the user32 API interaction is safe
        }

        [Fact]
        public void OpenUrl_ShouldHandleNullOrEmptyStringsGracefully()
        {
            // Act
            var exceptionNull = Record.Exception(() => AppHelpers.OpenUrl(null));
            var exceptionEmpty = Record.Exception(() => AppHelpers.OpenUrl(""));

            // Assert
            // It should capture the process start exception silently via try/catch inside OpenUrl
            // and not propagate it to the main application
            Assert.Null(exceptionNull);
            Assert.Null(exceptionEmpty);
        }

        [Fact]
        public void IsForegroundAppExcluded_ShouldNotCrash_WhenExcludedAppsIsNull()
        {
            // Arrange
            // Backup current configuration (mocking logic)
            var orgConfig = App.ExcludedApps;

            try
            {
                // Force an empty or null setting
                App.ExcludedApps = null;

                // Act
                var exception = Record.Exception(() => AppHelpers.IsForegroundAppExcluded());

                // Assert
                Assert.Null(exception);
            }
            finally
            {
                // Restore configuration
                App.ExcludedApps = orgConfig;
            }
        }
    }
}
