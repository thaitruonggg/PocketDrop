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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Sentry;

namespace PocketDrop
{
    public partial class App : System.Windows.Application
    {

        // ================================================ //
        // 1. GLOBAL VARIABLES & SETTINGS
        // ================================================ //

        // Global master list to hold every file dropped during this session
        public static List<PocketItem> SessionHistory = new List<PocketItem>();

        // Global flag so the rest of the app knows an update is waiting
        public static bool UpdateAvailable = false;
        public static string UpdateUrl = "https://github.com/naofunyan/PocketDrop/releases/latest";


        // ================================================ //
        // 2. USER SETTINGS & PREFERENCES
        // ================================================ //

        // General
        public static int AppTheme = 0; // 0 = System, 1 = Light, 2 = Dark
        public static string AppLanguage = "English";

        // Pocket Activation - Hotkey Preferences
        public static uint PocketKeyVK = 0x5A; // Z
        public static uint ClipboardKeyVK = 0x58; // X
        public static string PocketKeyChar = "Z";
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
        public static bool CloseWhenShare { get; set; } = true;
        public static bool CloseWhenCompress = false;


        // ================================================ //
        // 3. NATIVE WINDOWS APIS (P / INVOKE)
        // ================================================ //

        // Force the My Pockets window to the absolute front of the screen
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // Global Hotkeys - Register Win+Shift+Z and Win+Shift+X so they work even when the app is minimized
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Required OS constants for registering the hotkeys
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CTRL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        private const int HOTKEY_NEW_POCKET = 9001;
        private const int HOTKEY_NEW_CLIPBOARD = 9002;

        // Mouse Tracking
        // Find the exact pixel location of the mouse cursor to spawn the Pocket
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(ref Win32Point pt);

        // Required OS struct to hold the X/Y coordinates from GetCursorPos
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct Win32Point
        {
            public int X;
            public int Y;
        };


        // ================================================ //
        // 4. APP LIFECYCLE (STARTUP & EXIT)
        // ================================================ //

        // Setup Sentry
        public App()
        {
            SentrySdk.Init(options =>
            {
                options.Dsn = "https://4d1f664fbd5da3c2414771f1ca89870e@o4511181788741632.ingest.de.sentry.io/4511181794050128";
                options.IsGlobalModeEnabled = true;
                options.SendDefaultPii = false;
            });

            // Hook into the global WPF safety net
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        // Sentry crash catcher
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // 1. Send to Sentry
            SentrySdk.CaptureException(e.Exception);

            // Wait up to 2 seconds for Sentry to finish uploading before the app dies
            SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();

            // 2. Prevent default Windows error popup
            e.Handled = true;

            // 3. Close safely
            System.Windows.Application.Current.Shutdown();
        }

        // Read all user preferences from Windows
        public static void LoadSettings()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\PocketDrop"))
                {
                    // Convert safely handles the conversion so the app never crashes on load
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
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\PocketDrop"))
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
                    key.SetValue("AppTheme", AppTheme);
                    key.SetValue("AppLanguage", AppLanguage);
                }
            }
            catch { }
        }

        // The Boot Sequence
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Safely read Language and Theme from the Registry
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\PocketDrop"))
                {
                    if (key != null)
                    {
                        if (key.GetValue("AppLanguage") != null)
                            AppLanguage = key.GetValue("AppLanguage").ToString();

                        if (key.GetValue("AppTheme") != null)
                            AppTheme = (int)key.GetValue("AppTheme");
                    }
                }
            }
            catch { }

            // 2. Inject Language & Theme Dictionaries
            string dictPath = AppLanguage == "Vietnamese" ? "pack://application:,,,/PocketDrop;component/Languages/Strings.vi.xaml" : "pack://application:,,,/PocketDrop;component/Languages/Strings.en.xaml";
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(dictPath) });

            bool useDarkMode = AppTheme == 0 ? AppHelpers.IsWindowsInDarkMode() : AppTheme == 2;
            string themeFileName = useDarkMode ? "DarkTheme.xaml" : "LightTheme.xaml";
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/PocketDrop;component/Themes/{themeFileName}") });

            // 3. Tell WPF to stay alive in the background
            System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.ShowImageMargin = false;

            // Spawns a brand new Pocket Window
            TrayAddPocketItem = new System.Windows.Forms.ToolStripMenuItem();
            TrayAddPocketItem.Click += (s, ev) =>
            {
                var newPocket = new MainWindow();
                newPocket.Show();

                // Force the window to be fully visible and interactive
                newPocket.Opacity = 1;
                newPocket.IsHitTestVisible = true;
                newPocket.Activate(); // Brings it to the front
            };

            // Spawns a new Pocket and Pastes from Clipboard
            TrayAddClipboardItem = new System.Windows.Forms.ToolStripMenuItem("Add Pocket from clipboard");
            TrayAddClipboardItem.Click += (s, ev) =>
            {
                var newPocket = new MainWindow();
                newPocket.Show();

                // Force the window to be fully visible and interactive
                newPocket.Opacity = 1;
                newPocket.IsHitTestVisible = true;
                newPocket.Activate();
                newPocket.PasteFromClipboard();
            };

            TraySavedPocketsItem = new System.Windows.Forms.ToolStripMenuItem("Saved Pockets");
            TraySavedPocketsItem.Click += (s, ev) =>
            {
                // Check if the window is already open
                var existingWindow = System.Windows.Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();

                if (existingWindow != null)
                {
                    existingWindow.Activate(); // Bring existing window to front if already running
                }
                else
                {
                    // Spawn new window if no instance is running
                    var historyWindow = new SavedPocketsWindow();
                    historyWindow.Show();
                    historyWindow.Activate();
                }
            };

            TraySettingsItem = new System.Windows.Forms.ToolStripMenuItem("Settings");
            TraySettingsItem.Click += (s, ev) =>
            {
                // Check if the settings window is already open
                var existingSettings = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

                if (existingSettings != null)
                {
                    // If it is open / minimized, restore it and bring it to the front
                    if (existingSettings.WindowState == WindowState.Minimized)
                    {
                        existingSettings.WindowState = WindowState.Normal;
                    }
                    existingSettings.Activate();
                }
                else
                {
                    // If it isn't open at all, spawn a new one
                    var settingsWindow = new SettingsWindow();
                    settingsWindow.Show();
                    settingsWindow.Activate();
                }
            };

            TrayReportBugItem = new System.Windows.Forms.ToolStripMenuItem("Report bug");
            TrayReportBugItem.Click += (s, ev) =>
            {
                AppHelpers.OpenUrl("https://github.com/naofunyan/PocketDrop/issues");
            };

            TrayQuitItem = new System.Windows.Forms.ToolStripMenuItem("Quit PocketDrop");
            TrayQuitItem.Click += (s, ev) =>
            {
                System.Windows.Application.Current.Shutdown();
            };

            // 4. Register Global Hotkeys
            LoadSettings();
            ReloadHotkeys();

            // 5. Tell WPF to listen for global Windows messages
            System.Windows.Interop.ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;

            // 6. Run the update check silently in the background
            await CheckForUpdatesOnStartup();


            // Tray System - Load the text before adding to the menu
            UpdateTrayMenuLanguage();

            trayMenu.Items.Add(TrayAddPocketItem);
            trayMenu.Items.Add(TrayAddClipboardItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(TraySavedPocketsItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(TraySettingsItem);
            trayMenu.Items.Add(TrayReportBugItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(TrayQuitItem);

            _trayIcon = new System.Windows.Forms.NotifyIcon();

            // 1. Grab icon from the Assets folder
            var iconUri = new Uri("pack://application:,,,/Assets/PocketDrop.ico", UriKind.Absolute);
            var iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream;

            // 2. Feed it directly to the tray icon
            _trayIcon.Icon = new System.Drawing.Icon(iconStream);

            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.Text = "PocketDrop";

            // Open My Pockets on Left-Click
            _trayIcon.MouseClick += async (s, e) =>
            {
                // Only trigger on Left click
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    // Fire fake Escape keypress to break hover lock and close tray menu
                    System.Windows.Forms.SendKeys.SendWait("{ESC}");

                    var existingWindow = System.Windows.Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();
                    SavedPocketsWindow targetWindow;

                    if (existingWindow != null)
                    {
                        targetWindow = existingWindow;
                        targetWindow.Activate(); // Bring existing to front
                    }
                    else
                    {
                        targetWindow = new SavedPocketsWindow();
                        targetWindow.Show(); // Spawn a new one instantly
                    }

                    // Keep delay to let ESC closing animation finish
                    await System.Threading.Tasks.Task.Delay(100);

                    targetWindow.Activate();
                    targetWindow.Focus();

                    var hwnd = new System.Windows.Interop.WindowInteropHelper(targetWindow).Handle;
                    SetForegroundWindow(hwnd);
                }
            };
        }

        // Background Update check
        private async Task CheckForUpdatesOnStartup()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

                    string url = "https://raw.githubusercontent.com/naofunyan/PocketDrop/main/version.txt";
                    string latestVersionString = await client.GetStringAsync(url);

                    // Bump version to match current app version
                    string currentVersionString = "1.0.0";

                    bool hasUpdate = AppHelpers.IsUpdateAvailable(currentVersionString, latestVersionString);

                    if (hasUpdate)
                    {
                        // 1. Flip the global flag to true
                        UpdateAvailable = true;

                        // Fetch translations with English fallback
                        string titleTemplate = (string)System.Windows.Application.Current.TryFindResource("Text_UpdateAvailableTitle") ?? "Update Available";
                        string msgTemplate = (string)System.Windows.Application.Current.TryFindResource("Text_UpdateAvailableMsg") ?? "A new version of PocketDrop ({0}) is available!\n\nWould you like to download it now?";

                        // Inject the version number into the {0} placeholder
                        string finalMessage = string.Format(msgTemplate, latestVersionString.Trim());

                        // 2. Alert the user immediately with a localized prompt
                        MessageBoxResult result = System.Windows.MessageBox.Show(
                            finalMessage,
                            titleTemplate,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            AppHelpers.OpenUrl(UpdateUrl);
                        }
                    }
                }
            }
            catch
            {
                // Silent fail if no internet or GitHub blocks the request
            }
        }

        // The Shutdown Sequence
        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up the tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            // Wipe all temp files when the app quits
            foreach (var item in SessionHistory)
            {
                try
                {
                    string tempFolder = Path.GetTempPath();
                    if (item.FilePath.StartsWith(tempFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
                    }
                }
                catch { } // Fail silently if a file is locked
            }

            base.OnExit(e);
        }


        // ================================================ //
        // 5. SYSTEM TRAY & MENUS
        // ================================================ //

        // The master taskbar icon
        private static System.Windows.Forms.NotifyIcon? _trayIcon;

        // Class-level tray menu items for translation
        public static System.Windows.Forms.ToolStripMenuItem TrayAddPocketItem;
        public static System.Windows.Forms.ToolStripMenuItem TrayAddClipboardItem;
        public static System.Windows.Forms.ToolStripMenuItem TraySavedPocketsItem;
        public static System.Windows.Forms.ToolStripMenuItem TraySettingsItem;
        public static System.Windows.Forms.ToolStripMenuItem TrayReportBugItem;
        public static System.Windows.Forms.ToolStripMenuItem TrayQuitItem;

        // Fetch tray translations on startup and language change
        public static void UpdateTrayMenuLanguage()
        {
            if (TrayAddPocketItem == null) return;

            TrayAddPocketItem.Text = (string)System.Windows.Application.Current.TryFindResource("Text_TrayAddPocket");
            TrayAddClipboardItem.Text = (string)System.Windows.Application.Current.TryFindResource("Text_TrayAddClipboard");
            TraySavedPocketsItem.Text = (string)System.Windows.Application.Current.TryFindResource("Text_TraySavedPockets");
            TraySettingsItem.Text = (string)System.Windows.Application.Current.TryFindResource("Text_TraySettings");
            TrayReportBugItem.Text = (string)System.Windows.Application.Current.TryFindResource("Text_TrayReportBug");
            TrayQuitItem.Text = (string)System.Windows.Application.Current.TryFindResource("Text_TrayQuit");

            if (_trayIcon != null) _trayIcon.Text = "PocketDrop";
        }

        // Triggered when the user clicks the tray icon
        private void TrayIcon_Click(object? sender, EventArgs e)
        {
            // 1. Fetch raw translated strings with English fallback
            string titleTemplate = (string)System.Windows.Application.Current.TryFindResource("Text_SessionHistoryTitle") ?? "Session History";
            string msgTemplate = (string)System.Windows.Application.Current.TryFindResource("Text_SessionHistoryMsg") ?? "Your session history currently holds {0} items.";

            // 2. Inject the live count into the {0} placeholder
            string finalMessage = string.Format(msgTemplate, SessionHistory.Count);

            // 3. Show the message box
            System.Windows.MessageBox.Show(
                finalMessage,
                titleTemplate,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        // ================================================ //
        // 6. GLOBAL INPUT HANDLING (HOTKEYS & MOUSE)
        // ================================================ //

        // Force thread sync on startup and shortcut settings change
        public static void ReloadHotkeys()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SaveSettings();

                // 1. Wipe the old keys
                UnregisterHotKey(IntPtr.Zero, HOTKEY_NEW_POCKET);
                UnregisterHotKey(IntPtr.Zero, HOTKEY_NEW_CLIPBOARD);

                // 2. Ask Windows for the new keys, and capture its response (true = success, false = rejected)
                bool successPocket = RegisterHotKey(IntPtr.Zero, HOTKEY_NEW_POCKET, PocketModifiers, PocketKeyVK);
                bool successClipboard = RegisterHotKey(IntPtr.Zero, HOTKEY_NEW_CLIPBOARD, ClipboardModifiers, ClipboardKeyVK);

                // 3. If Windows says NO, alert the user!
                if (!successPocket || !successClipboard)
                {
                    // Fetch raw translated strings with English fallback
                    string titleText = (string)System.Windows.Application.Current.TryFindResource("Text_ShortcutErrorTitle") ?? "Shortcut Error";
                    string msgText = (string)System.Windows.Application.Current.TryFindResource("Text_ShortcutErrorMsg") ?? "Windows blocked this shortcut!\n\nThis usually means another background app (like Snipping Tool, PowerToys, or a graphics overlay) is already holding this key hostage.\n\nPlease try a different letter.";

                    // Show the message box
                    System.Windows.MessageBox.Show(
                        msgText,
                        titleText,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            });
        }

        // Listen for global keystrokes in background thread
        private void ComponentDispatcher_ThreadPreprocessMessage(ref System.Windows.Interop.MSG msg, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg.message == WM_HOTKEY)
            {
                int id = msg.wParam.ToInt32();

                if (id == HOTKEY_NEW_POCKET)
                {
                    // Win + Shift + Z pressed!
                    SpawnPocketAtCursor(false);
                    handled = true;
                }
                else if (id == HOTKEY_NEW_CLIPBOARD)
                {
                    // Win + Shift + X pressed!
                    SpawnPocketAtCursor(true);
                    handled = true;
                }
            }
        }

        // Spawning the Pocket
        private void SpawnPocketAtCursor(bool pasteFromClipboard)
        {
            // 1. Find exactly where the mouse is right now
            Win32Point mousePos = new Win32Point();
            GetCursorPos(ref mousePos);

            // 2. Find a sleeping pocket, or spawn a fresh one
            var hiddenPocket = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => !w.IsHitTestVisible);
            MainWindow targetPocket;

            if (hiddenPocket != null)
            {
                targetPocket = hiddenPocket;
            }
            else
            {
                targetPocket = new MainWindow();
                targetPocket.Show();
            }

            // 3. Trigger the animation right at the mouse cursor
            targetPocket.ShowPocketDrop(mousePos.X, mousePos.Y);

            // 4. If the user pressed X, automatically pull the clipboard data
            if (pasteFromClipboard)
            {
                // Wait 100ms for the window to finish expanding, then paste
                System.Threading.Tasks.Task.Delay(100).ContinueWith(_ =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        targetPocket.PasteFromClipboard();
                    });
                });
            }
        }
    }
}