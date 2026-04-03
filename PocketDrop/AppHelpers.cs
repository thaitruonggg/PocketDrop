using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;

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
    }
}