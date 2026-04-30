// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace PocketDrop
{
    public static class AppHelpers
    {
        // ================================================ //
        // 1. NETWORK & SYSTEM
        // ================================================ //

        // Shared HTTP client instance to prevent socket exhaustion
        public static readonly System.Net.Http.HttpClient GlobalClient = new System.Net.Http.HttpClient();

        // Configure the HTTP client once on startup
        static AppHelpers()
        {
            GlobalClient.Timeout = TimeSpan.FromSeconds(5);
            GlobalClient.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            GlobalClient.DefaultRequestHeaders.Add("User-Agent", "PocketDrop-App");
        }

        // Detect Windows dark mode
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

        // Run at startup
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

        // Version check
        public static string GetAppVersion()
        {
            var versionAttr = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                as System.Reflection.AssemblyInformationalVersionAttribute[];

            if (versionAttr != null && versionAttr.Length > 0)
            {
                return versionAttr[0].InformationalVersion.Split('+')[0].Replace("-beta-", " Beta ");
            }
            return "1.0.0"; // Fallback
        }

        public static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ex.Data.Add("PocketDrop Context", "URL Error");
                SentrySdk.CaptureException(ex);
            }
        }

        public static bool IsUpdateAvailable(string currentVersionText, string onlineVersionText)
        {
            // Trim whitespace and newlines from the version string
            string currentClean = currentVersionText?.Trim() ?? "";
            string onlineClean = onlineVersionText?.Trim() ?? "";

            if (Version.TryParse(currentClean, out Version current) &&
                Version.TryParse(onlineClean, out Version latest))
            {
                return latest > current;
            }

            // Return false if the version string is malformed or an error
            return false;
        }


        // ================================================ //
        // 2. FILE & DATA MANAGEMENT
        // ================================================ //

        // File size calculation
        public static string FormatBytes(long bytes)
        {
            // Guard against zero and negative inputs to avoid Math.Log errors
            if (bytes <= 0) return "0 B";

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return $"{num.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {suffixes[place]}";
        }


        // Duplicate detection
        public static bool IsDuplicate(IEnumerable<PocketItem> currentItems, string newFilePath)
        {
            if (string.IsNullOrEmpty(newFilePath)) return false;
            return currentItems.Any(item =>
                item.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase));
        }


        // Remove dead files
        public static bool RemoveDeadFiles(IList<PocketItem> currentItems)
        {
            bool removedAny = false;

            // Iterate in reverse to safely remove items during the loop
            for (int i = currentItems.Count - 1; i >= 0; i--)
            {
                string path = currentItems[i].FilePath;

                // Remove entries whose file and directory no longer exist
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    currentItems.RemoveAt(i);
                    removedAny = true;
                }
            }
            return removedAny;
        }

        // Collision renamer 
        public static string GetSafeDisplayName(IEnumerable<PocketItem> currentItems, string originalPath)
        {
            string originalName = Path.GetFileName(originalPath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);

            string finalDisplayName = originalName;
            int counter = 1;

            // Increment suffix until the filename is unique
            while (currentItems.Any(item => item.FileName.Equals(finalDisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                finalDisplayName = $"{nameWithoutExt} ({counter}){extension}";
                counter++;
            }

            return finalDisplayName;
        }

        // Security Utilities
        public static bool VerifyFileHash(string filePath, string expectedHash)
        {
            // Skip hash check if no hash file exists on GitHub
            if (string.IsNullOrWhiteSpace(expectedHash)) return true;

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            using (var stream = System.IO.File.OpenRead(filePath))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                string computedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                return computedHash == expectedHash.ToLowerInvariant().Trim();
            }
        }


        // ================================================ //
        // 3. WINDOW & UI CALCULATIONS
        // ================================================ //

        // Pocket placement
        public static Point CalculateWindowPosition(int placementMode, double cursorX, double cursorY, double w, double h, double workAreaLeft, double workAreaTop, double workAreaRight, double workAreaBottom)
        {
            // Default position (Near Mouse)
            double targetLeft = cursorX - (w / 2) + 40;
            double targetTop = cursorY - h - 80;

            // Clamp "Near Mouse" position to stay within screen bounds
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

        // Calculate My Pocket position
        public static Point CalculateTaskbarSnapPosition(double windowWidth, double windowHeight, double workAreaWidth, double workAreaHeight, double shadowMargin)
        {
            double targetLeft = workAreaWidth - windowWidth + shadowMargin;
            double targetTop = workAreaHeight - windowHeight + shadowMargin;

            return new Point(targetLeft, targetTop);
        }


        // ================================================ //
        // 4. NATIVE WINDOWS APIS
        // ================================================ //

        // Game mode detection
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int pquns);

        public static bool IsGameModeActive()
        {
            try
            {
                SHQueryUserNotificationState(out int state);
                // 3 = QUNS_RUNNING_D3D_FULL_SCREEN (DirectX Fullscreen Games)
                // 4 = QUNS_PRESENTATION_MODE (Fullscreen Video / PowerPoint)
                return state == 3 || state == 4;
            }
            catch { return false; }
        }

        // Foreground window detection
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        // Add required OS constants for hotkey registration
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CTRL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        // Register Win+Shift+Z and Win+Shift+X as global hotkeys
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName([System.Runtime.InteropServices.In] IntPtr hProcess, [System.Runtime.InteropServices.In] int dwFlags, [System.Runtime.InteropServices.Out] System.Text.StringBuilder lpExeName, ref int lpdwSize);

        private static string _lastExcludedAppsRaw = null;
        private static HashSet<string> _cachedExcludedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add native interop for Windows Share UI
        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        // Native File Picker
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO poainfo);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public struct OPENASINFO
        {
            public string pcszFile;
            public string pcszClass;
            public int oaUIAction;
        }

        // Native Window Dragging
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        // Hardware Mouse State API
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
        public const int VK_LBUTTON = 0x01;

        // Update the cache only when settings change
        private static void UpdateExcludedAppsCache()
        {
            if (_lastExcludedAppsRaw == AppGlobals.ExcludedApps) return;

            _cachedExcludedApps.Clear();
            if (!string.IsNullOrWhiteSpace(AppGlobals.ExcludedApps))
            {
                var rules = AppGlobals.ExcludedApps.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var ruleText in rules)
                {
                    string rule = System.IO.Path.GetFileNameWithoutExtension(ruleText.Trim());
                    if (!string.IsNullOrEmpty(rule))
                    {
                        _cachedExcludedApps.Add(rule);
                    }
                }
            }
            _lastExcludedAppsRaw = AppGlobals.ExcludedApps;
        }

        public static bool IsForegroundAppExcluded()
        {
            if (string.IsNullOrWhiteSpace(AppGlobals.ExcludedApps)) return false;

            UpdateExcludedAppsCache();
            if (_cachedExcludedApps.Count == 0) return false;

            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return false;

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return false;

                const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero) return false;

                try
                {
                    int capacity = 1024;
                    var sb = new System.Text.StringBuilder(capacity);
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                    {
                        string exePath = sb.ToString();
                        string processName = System.IO.Path.GetFileNameWithoutExtension(exePath);

                        return _cachedExcludedApps.Contains(processName);
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch { }

            return false;
        }        
    }
}