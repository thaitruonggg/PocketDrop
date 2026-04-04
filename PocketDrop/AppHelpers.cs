using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PocketDrop
{
    public static class AppHelpers
    {
        // ══════════════════════════════════════════════════════
        // FILE MATH
        // ══════════════════════════════════════════════════════
        public static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return $"{num.ToString("0.0", CultureInfo.InvariantCulture)} {suffixes[place]}";
        }

        // ══════════════════════════════════════════════════════
        // LIST MANAGEMENT
        // ══════════════════════════════════════════════════════
        public static bool IsDuplicate(IEnumerable<PocketItem> currentItems, string newFilePath)
        {
            if (string.IsNullOrEmpty(newFilePath)) return false;
            return currentItems.Any(item =>
                item.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase));
        }

        // ══════════════════════════════════════════════════════
        // JIT VALIDATION (GHOST FILE CHECK)
        // ══════════════════════════════════════════════════════
        public static bool RemoveDeadFiles(IList<PocketItem> currentItems)
        {
            bool removedAny = false;

            // Iterate backward so we can safely delete items while looping
            for (int i = currentItems.Count - 1; i >= 0; i--)
            {
                string path = currentItems[i].FilePath;

                // If the file AND directory no longer exist on the hard drive
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    currentItems.RemoveAt(i);
                    removedAny = true;
                }
            }

            return removedAny;
        }

        // ══════════════════════════════════════════════════════
        // SHAKE DETECTOR ALGORITHM
        // ══════════════════════════════════════════════════════
        public class ShakeDetector
        {
            private Queue<long> _swingTimestamps = new Queue<long>();
            private int _lastX = 0;
            private int _swingOriginX = 0;
            private int _currentDir = 0; // 1 for right, -1 for left
            private bool _isFirstMove = true;

            public bool CheckForShake(int currentMouseX, long currentTimestampMs, int minDistancePx, int maxTimeMs, int requiredSwings = 3)
            {
                if (_isFirstMove)
                {
                    _lastX = currentMouseX;
                    _swingOriginX = currentMouseX;
                    _isFirstMove = false;
                    return false;
                }

                int deltaX = currentMouseX - _lastX;
                if (deltaX == 0) return false;

                int newDir = deltaX > 0 ? 1 : -1;

                // Did we change direction? (A "Swing")
                if (_currentDir != 0 && newDir != _currentDir)
                {
                    // How far did we travel before changing direction?
                    int swingDistance = Math.Abs(_lastX - _swingOriginX);

                    if (swingDistance >= minDistancePx)
                    {
                        // Valid swing! Record the time.
                        _swingTimestamps.Enqueue(currentTimestampMs);
                    }

                    // Reset origin for the new swing direction
                    _swingOriginX = _lastX;
                }

                _currentDir = newDir;
                _lastX = currentMouseX;

                // Clean up old swings that fell outside the time window
                while (_swingTimestamps.Count > 0 && (currentTimestampMs - _swingTimestamps.Peek()) > maxTimeMs)
                {
                    _swingTimestamps.Dequeue();
                }

                // Did we hit the required number of swings?
                if (_swingTimestamps.Count >= requiredSwings)
                {
                    _swingTimestamps.Clear(); // Reset so it doesn't instantly trigger again
                    _isFirstMove = true;
                    return true;
                }

                return false;
            }
        }

        // ══════════════════════════════════════════════════════
        // Collision Renamer ALGORITHM
        // ══════════════════════════════════════════════════════
        public static string GetSafeDisplayName(IEnumerable<PocketItem> currentItems, string originalPath)
        {
            string originalName = Path.GetFileName(originalPath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);

            string finalDisplayName = originalName;
            int counter = 1;

            // Keep counting up until we find a name that isn't taken!
            while (currentItems.Any(item => item.FileName.Equals(finalDisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                finalDisplayName = $"{nameWithoutExt} ({counter}){extension}";
                counter++;
            }

            return finalDisplayName;
        }

        // ══════════════════════════════════════════════════════
        // Move the Window Placement ALGORITHM
        // ══════════════════════════════════════════════════════
        public static Point CalculateWindowPosition(int placementMode, double cursorX, double cursorY, double w, double h, double workAreaLeft, double workAreaTop, double workAreaRight, double workAreaBottom)
        {
            // Default position (Near Mouse)
            double targetLeft = cursorX - (w / 2) + 40;
            double targetTop = cursorY - h - 80;

            // Keep "Near Mouse" safely on screen
            targetLeft = Math.Max(workAreaLeft + 8, Math.Min(targetLeft, workAreaRight - w - 8));
            targetTop = Math.Max(workAreaTop + 8, Math.Min(targetTop, workAreaBottom - h - 8));

            switch (placementMode)
            {
                case 1: // Top edge
                    targetLeft = workAreaLeft + ((workAreaRight - workAreaLeft) / 2) - (w / 2);
                    targetTop = workAreaTop + 8;
                    break;
                case 2: // Bottom edge
                    targetLeft = workAreaLeft + ((workAreaRight - workAreaLeft) / 2) - (w / 2);
                    targetTop = workAreaBottom - h - 8;
                    break;
                case 3: // Left edge
                    targetLeft = workAreaLeft + 8;
                    targetTop = workAreaTop + ((workAreaBottom - workAreaTop) / 2) - (h / 2);
                    break;
                case 4: // Right edge
                    targetLeft = workAreaRight - w - 8;
                    targetTop = workAreaTop + ((workAreaBottom - workAreaTop) / 2) - (h / 2);
                    break;
                case 5: // Top left corner
                    targetLeft = workAreaLeft + 8;
                    targetTop = workAreaTop + 8;
                    break;
                case 6: // Top right corner
                    targetLeft = workAreaRight - w - 8;
                    targetTop = workAreaTop + 8;
                    break;
                case 7: // Bottom left corner
                    targetLeft = workAreaLeft + 8;
                    targetTop = workAreaBottom - h - 8;
                    break;
                case 8: // Bottom right corner
                    targetLeft = workAreaRight - w - 8;
                    targetTop = workAreaBottom - h - 8;
                    break;
            }

            return new Point(targetLeft, targetTop);
        }

        // ══════════════════════════════════════════════════════
        // Calculate My Pocket position ALGORITHM
        // ══════════════════════════════════════════════════════
        public static Point CalculateTaskbarSnapPosition(double windowWidth, double windowHeight, double workAreaWidth, double workAreaHeight, double shadowMargin)
        {
            // Subtract the window size from the total screen size to push it to the right/bottom edge,
            // then add the shadow margin back so the invisible borders bleed off the screen.
            double targetLeft = workAreaWidth - windowWidth + shadowMargin;
            double targetTop = workAreaHeight - windowHeight + shadowMargin;

            return new Point(targetLeft, targetTop);
        }

        // ══════════════════════════════════════════════════════
        // Check DarkMode ALGORITHM
        // ══════════════════════════════════════════════════════

        public static bool IsWindowsInDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") != null)
                    {
                        return (int)key.GetValue("AppsUseLightTheme") == 0;
                    }
                }
            }
            catch { }

            return false;
        }

        // ══════════════════════════════════════════════════════
        // Run at Startup ALGORITHM
        // ══════════════════════════════════════════════════════
        public static bool IsRunAtStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key?.GetValue("PocketDrop") != null;
                }
            }
            catch { }
            return false;
        }

        public static bool SetRunAtStartup(bool enable, string exePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue("PocketDrop", $"\"{exePath}\"");
                        }
                        else
                        {
                            key.DeleteValue("PocketDrop", false);
                        }
                        return true; // Success
                    }
                }
            }
            catch { }
            return false; // Failed
        }

        // ══════════════════════════════════════════════════════
        // Version check ALGORITHM
        // ══════════════════════════════════════════════════════
        public static bool IsUpdateAvailable(string currentVersionText, string onlineVersionText)
        {
            // Clean up the strings just in case GitHub added invisible spaces or newlines
            string currentClean = currentVersionText?.Trim() ?? "";
            string onlineClean = onlineVersionText?.Trim() ?? "";

            if (Version.TryParse(currentClean, out Version current) &&
                Version.TryParse(onlineClean, out Version latest))
            {
                return latest > current;
            }

            // If the strings are garbage (like "HTML Error 404"), safely return false
            return false;
        }
    }
}