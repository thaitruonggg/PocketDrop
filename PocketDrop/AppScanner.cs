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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PocketDrop
{
    public static class AppScanner
    {

        // ================================================ //
        // 1. THE CORE SCANNING ENGINE
        // ================================================ //

        // Scan Windows Registry to find all installed applications and their executable paths.
        public static List<AppItem> GetInstalledApps()
        {
            var appList = new List<AppItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] registryKeys = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", // Standard 64-bit apps
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", // Standard 32-bit apps
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" // Current User specific apps
            };

            foreach (var keyPath in registryKeys)
            {
                RegistryKey baseKey = keyPath == registryKeys[2] ? Registry.CurrentUser : Registry.LocalMachine;

                using (RegistryKey key = baseKey.OpenSubKey(keyPath))
                {
                    if (key == null) continue;

                    foreach (var subkeyName in key.GetSubKeyNames())
                    {
                        using (RegistryKey subkey = key.OpenSubKey(subkeyName))
                        {
                            if (subkey == null) continue;

                            string displayName = subkey.GetValue("DisplayName") as string;
                            string displayIcon = subkey.GetValue("DisplayIcon") as string;
                            string installLocation = subkey.GetValue("InstallLocation") as string;

                            string exePath = CleanExePath(displayIcon);

                            if (string.IsNullOrEmpty(exePath) && !string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                exePath = Directory.GetFiles(installLocation, "*.exe").FirstOrDefault();
                            }

                            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath) && !seenPaths.Contains(exePath))
                            {
                                if (string.IsNullOrEmpty(displayName))
                                    displayName = Path.GetFileNameWithoutExtension(exePath);

                                seenPaths.Add(exePath);

                                // Create the AppItem and pass the path
                                appList.Add(new AppItem
                                {
                                    AppName = displayName,
                                    ExePath = exePath,
                                    IsSelected = false
                                });
                            }
                        }
                    }
                }
            }

            // Sort app list alphabetically before returning
            return appList.OrderBy(a => a.AppName).ToList();
        }


        // ================================================ //
        // 2. DATA EXTRACTION HELPERS
        // ================================================ //

        // Extracts the native Windows icon from an .exe and converts it to a WPF-friendly ImageSource.
        public static ImageSource GetIconFromExe(string path)
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(path))
                {
                    if (icon != null)
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                       
                        bitmapSource.Freeze(); // Freeze the image for UI thread access

                        return bitmapSource;
                    }
                }
            }
            catch { }
            return null; // Return null if it fails (the UI will just show no icon)
        }

        // Strip quotes and icon indexes from messy registry paths
        private static string CleanExePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Strip out surrounding spaces and quotation marks
            path = path.Trim('"', ' ');

            // Remove icon indexes (e.g., "C:\app.exe,0")
            int commaIndex = path.LastIndexOf(',');
            if (commaIndex > 0)
            {
                path = path.Substring(0, commaIndex);
            }

            // Return path only if it resolves to a valid executable file
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return null;
        }
    }
}