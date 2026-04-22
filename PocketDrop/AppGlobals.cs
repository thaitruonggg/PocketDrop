using System;
using Microsoft.Win32;
using static PocketDrop.AppHelpers;

namespace PocketDrop
{
    public static class AppGlobals
    {
        // ================================================ //
        // 1. GLOBAL VARIABLES
        // ================================================ //

        // Global master list to hold every file dropped during this session
        public static ObservableRangeCollection<PocketItem> SessionHistory = new ObservableRangeCollection<PocketItem>();

        // Global flag so the rest of the app knows an update is waiting
        public static bool UpdateAvailable = false;
        public static string UpdateUrl = "https://github.com/naofunyan/PocketDrop/releases/latest";

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

        // Required OS constants for registering the hotkeys
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CTRL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;


        // ================================================ //
        // 2. USER SETTINGS & PREFERENCES
        // ================================================ //

        // General
        public static int AppTheme = 0; // 0 = System, 1 = Light, 2 = Dark
        public static string AppLanguage = "English";

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
        public static bool CopyItemToDestination { get; set; } = true; // Default True = Copy, False = Move

        // Pocket Interaction - Auto Compress Folders When Sharing
        public static bool AutoCompressFoldersShare { get; set; } = true;

        // Pocket Interaction - Close Pocket After Action
        public static bool CloseWhenEmptied = true;
        public static bool CloseWhenOpenWith { get; set; } = false;
        public static bool CloseWhenShare { get; set; } = false;
        public static bool CloseWhenCompress = false;


        // ================================================ //
        // 3. REGISTRY SAVE & LOAD LOGIC
        // ================================================ //

        // Read all user preferences from Windows
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
                    AppTheme = Convert.ToInt32(key.GetValue("AppTheme", 0));
                    AppLanguage = key.GetValue("AppLanguage", "English").ToString();
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
                    key.SetValue("AppTheme", AppTheme);
                    key.SetValue("AppLanguage", AppLanguage);
                }
            }
            catch { }
        }

        // ================================================ //
        // 4. CROSS-WINDOW COMMUNICATION
        // ================================================ //
        public static Action RequestNewPocket;
        public static Action RequestPocketsRefresh;
        public static Action RequestPocketsForceClose;
        public static Action RequestHistoryRefresh;
    }
}