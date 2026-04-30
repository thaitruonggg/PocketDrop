// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using System;
using Microsoft.Win32;
using static PocketDrop.AppHelpers;

namespace PocketDrop
{
    public static class AppGlobals
    {
        // ================================================ //
        // 1. GLOBAL STATE & SESSION
        // ================================================ //

        // Track all files dropped in the current session
        public static ObservableRangeCollection<PocketItem> SessionHistory = new ObservableRangeCollection<PocketItem>();

        // O(1) Fast-Lookup Cache
        public static System.Collections.Generic.HashSet<string> SessionHistoryPaths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Keep the hash set in sync with the file list
        static AppGlobals()
        {
            SessionHistory.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (PocketItem item in e.NewItems)
                        if (!string.IsNullOrEmpty(item.FilePath)) SessionHistoryPaths.Add(item.FilePath);
                }
                if (e.OldItems != null)
                {
                    foreach (PocketItem item in e.OldItems)
                        if (!string.IsNullOrEmpty(item.FilePath)) SessionHistoryPaths.Remove(item.FilePath);
                }
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    SessionHistoryPaths.Clear();
                    foreach (PocketItem item in SessionHistory)
                        if (!string.IsNullOrEmpty(item.FilePath)) SessionHistoryPaths.Add(item.FilePath);
                }
            };
        }

        // Flag to indicate a pending update
        public static bool UpdateAvailable = false;
        public static string UpdateUrl = "https://github.com/naofunyan/PocketDrop/releases/latest";


        // ================================================ //
        // 2. USER PREFERENCES
        // ================================================ //

        // General
        public static int AppTheme = 0; // 0 = System, 1 = Light, 2 = Dark
        public static string AppLanguage = "English";
        public static bool HasSeenWelcome = false;

        // Pocket Activation - Hotkey Preferences
        public static uint PocketKeyVK = 0x5A;
        public static string PocketKeyChar = "Z";
        public static uint ClipboardKeyVK = 0x58;
        public static string ClipboardKeyChar = "X";
        public static uint PocketModifiers = MOD_WIN | MOD_SHIFT;
        public static uint ClipboardModifiers = MOD_WIN | MOD_SHIFT;

        // Pocket Activation - Mouse Shake Preferences
        public static bool EnableMouseShake = true;
        public static int ShakeMinimumDistance = 130;
        public static bool DisableInGameMode = true;

        // Pocket Activation - Pocket Placement
        public static int PocketPlacement = 0; // 0 = Near mouse, 1 = Top edge,...

        // Pocket Activation - App Exceptions
        public static string ExcludedApps = "";

        // Pocket Interaction - Detail View Layout
        public static int ItemsLayoutMode = 0; // 0 = Grid, 1 = List

        // Pocket Interaction - Copy Item To Destination
        public static bool CopyItemToDestination = true; // Default True = Copy, False = Move

        // Pocket Interaction - Auto Compress Folders When Sharing
        public static bool AutoCompressFoldersShare = true;

        // Pocket Interaction - Close Pocket After Action
        public static bool CloseWhenEmptied = true;
        public static bool CloseWhenOpenWith = false;
        public static bool CloseWhenShare = false;
        public static bool CloseWhenCompress = false;


        // ================================================ //
        // 3. REGISTRY STORAGE
        // ================================================ //

        // Load all user preferences from the registry
        public static void LoadSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\PocketDrop"))
                {
                    PocketKeyChar = key.GetValue("PocketKeyChar", "Z").ToString();
                    PocketKeyVK = Convert.ToUInt32(key.GetValue("PocketKeyVK", 0x5A));
                    ClipboardKeyChar = key.GetValue("ClipboardKeyChar", "X").ToString();
                    ClipboardKeyVK = Convert.ToUInt32(key.GetValue("ClipboardKeyVK", 0x58));
                    PocketModifiers = Convert.ToUInt32(key.GetValue("PocketModifiers", MOD_WIN | MOD_SHIFT));
                    ClipboardModifiers = Convert.ToUInt32(key.GetValue("ClipboardModifiers", MOD_WIN | MOD_SHIFT));

                    AppTheme = Convert.ToInt32(key.GetValue("AppTheme", 0));
                    AppLanguage = key.GetValue("AppLanguage", "English").ToString();
                    HasSeenWelcome = Convert.ToBoolean(key.GetValue("HasSeenWelcome", false));
                    EnableMouseShake = Convert.ToBoolean(key.GetValue("EnableMouseShake", true));
                    ShakeMinimumDistance = Convert.ToInt32(key.GetValue("ShakeMinimumDistance", 100));
                    DisableInGameMode = Convert.ToBoolean(key.GetValue("DisableInGameMode", true));
                    ExcludedApps = key.GetValue("ExcludedApps", "").ToString();
                    PocketPlacement = Convert.ToInt32(key.GetValue("PocketPlacement", 0));
                    ItemsLayoutMode = Convert.ToInt32(key.GetValue("ItemsLayoutMode", 0));
                    CloseWhenEmptied = Convert.ToBoolean(key.GetValue("CloseWhenEmptied", true));
                    CloseWhenOpenWith = Convert.ToBoolean(key.GetValue("CloseWhenOpenWith", false));
                    CloseWhenShare = Convert.ToBoolean(key.GetValue("CloseWhenShare", true));
                    CloseWhenCompress = Convert.ToBoolean(key.GetValue("CloseWhenCompress", false));
                    CopyItemToDestination = Convert.ToBoolean(key.GetValue("CopyItemToDestination", true));
                    AutoCompressFoldersShare = Convert.ToBoolean(key.GetValue("AutoCompressFoldersShare", true));
                }
            }
            catch { }
        }

        // Save preferences on every setting change
        public static void SaveSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\PocketDrop"))
                {
                    key.SetValue("PocketKeyChar", PocketKeyChar);
                    key.SetValue("PocketKeyVK", (int)PocketKeyVK);
                    key.SetValue("ClipboardKeyChar", ClipboardKeyChar);
                    key.SetValue("ClipboardKeyVK", (int)ClipboardKeyVK);
                    key.SetValue("PocketModifiers", (int)PocketModifiers);
                    key.SetValue("ClipboardModifiers", (int)ClipboardModifiers);
                    key.SetValue("AppTheme", AppTheme);
                    key.SetValue("AppLanguage", AppLanguage);
                    key.SetValue("HasSeenWelcome", HasSeenWelcome);
                    key.SetValue("EnableMouseShake", EnableMouseShake);
                    key.SetValue("ShakeMinimumDistance", ShakeMinimumDistance);
                    key.SetValue("DisableInGameMode", DisableInGameMode);
                    key.SetValue("ExcludedApps", ExcludedApps);
                    key.SetValue("PocketPlacement", PocketPlacement);
                    key.SetValue("ItemsLayoutMode", ItemsLayoutMode);
                    key.SetValue("CloseWhenEmptied", CloseWhenEmptied);
                    key.SetValue("CloseWhenOpenWith", CloseWhenOpenWith);
                    key.SetValue("CloseWhenShare", CloseWhenShare);
                    key.SetValue("CloseWhenCompress", CloseWhenCompress);
                    key.SetValue("CopyItemToDestination", CopyItemToDestination);
                    key.SetValue("AutoCompressFoldersShare", AutoCompressFoldersShare);
                }
            }
            catch { }
        }

        // ================================================ //
        // 4. CROSS-WINDOW COMMUNICATION
        // ================================================ //

        // 1. The locked-down events
        public static event Action RequestNewPocket;
        public static event Action RequestPocketsRefresh;
        public static event Action RequestPocketsForceClose;
        public static event Action RequestHistoryRefresh;

        // 2. The safe trigger methods
        public static void TriggerNewPocket() => RequestNewPocket?.Invoke();
        public static void TriggerPocketsRefresh() => RequestPocketsRefresh?.Invoke();
        public static void TriggerPocketsForceClose() => RequestPocketsForceClose?.Invoke();
        public static void TriggerHistoryRefresh() => RequestHistoryRefresh?.Invoke();
    }
}