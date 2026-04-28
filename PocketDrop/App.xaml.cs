// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using Microsoft.Win32;
using Sentry;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using static PocketDrop.AppHelpers;

namespace PocketDrop
{
    public partial class App : System.Windows.Application
    {
        // ================================================ //
        // 1. APP LIFECYCLE (STARTUP, ERRORS, EXIT)
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

            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        // Initialize Sentry for crash reporting
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            SentrySdk.CaptureException(e.Exception); // Send to Sentry

            SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); // Flush Sentry with a 2s timeout before app exit

            e.Handled = true; // Suppress the default Windows error dialog

            System.Windows.Application.Current.Shutdown();
        }

        // The boot sequence
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Load all settings early so we can apply Themes/Languages right away
            AppGlobals.LoadSettings();

            // Handle cross-window events triggered from AppGlobals
            AppGlobals.RequestNewPocket += () => ShowPocketWindow(false, false);

            AppGlobals.RequestPocketsForceClose += () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var openPockets = System.Windows.Application.Current.Windows.OfType<MainWindow>().ToList();
                    foreach (var pocket in openPockets)
                    {
                        if (pocket.IsLoaded && pocket.Visibility == Visibility.Visible && pocket.Opacity >= 0.99)
                            pocket.ForceClose();
                    }
                });
            };

            AppGlobals.RequestPocketsRefresh += () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var openPockets = System.Windows.Application.Current.Windows.OfType<MainWindow>().ToList();
                    foreach (var pocket in openPockets)
                    {
                        if (pocket.IsLoaded) pocket.RefreshPocketUI();
                    }
                });
            };

            AppGlobals.RequestHistoryRefresh += () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var openHistoryWindow = System.Windows.Application.Current.Windows.OfType<MyPocketsWindow>().FirstOrDefault();
                    if (openHistoryWindow != null) openHistoryWindow.RefreshHistory();
                });
            };

            // Apply Language and Theme resource dictionaries
            string dictPath = AppGlobals.AppLanguage == "Vietnamese" ? "pack://application:,,,/PocketDrop;component/Languages/Strings.vi.xaml" : "pack://application:,,,/PocketDrop;component/Languages/Strings.en.xaml";
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(dictPath) });

            bool useDarkMode = AppGlobals.AppTheme == 0 ? AppHelpers.IsWindowsInDarkMode() : AppGlobals.AppTheme == 2;
            string themeFileName = useDarkMode ? "DarkTheme.xaml" : "LightTheme.xaml";
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/PocketDrop;component/Themes/{themeFileName}") });

            // Keep the app running in the background without a main window
            System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Register global hotkeys
            ReloadHotkeys();

            // Hook into the global Windows message loop
            System.Windows.Interop.ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;

            // Show welcome screen on first run
            if (!AppGlobals.HasSeenWelcome)
            {
                WelcomeWindow welcome = new WelcomeWindow();
                welcome.Show();

                // Flip the flag and save immediately
                AppGlobals.HasSeenWelcome = true;
                AppGlobals.SaveSettings();
            }

            // Build and launch the System Tray
            InitializeSystemTray();

            _ = CheckForUpdatesOnStartup();
        }

        // Check for updates in the background
        private async Task CheckForUpdatesOnStartup()
        {
            try
            {
                string url = "https://raw.githubusercontent.com/naofunyan/PocketDrop/main/version.txt";
                string latestVersionString = await AppHelpers.GlobalClient.GetStringAsync(url);

                string currentVersionString = AppGlobals.GetAppVersion().Replace(" Beta ", "-beta");
                bool hasUpdate = AppHelpers.IsUpdateAvailable(currentVersionString, latestVersionString);

                if (hasUpdate)
                {
                    AppGlobals.UpdateAvailable = true;

                    string titleTemplate = (string)System.Windows.Application.Current.TryFindResource("Text_UpdateAvailableTitle") ?? "Update Available";
                    string msgTemplate = (string)System.Windows.Application.Current.TryFindResource("Text_UpdateAvailableMsg") ?? "A new version of PocketDrop ({0}) is available!\n\nWould you like to download it now?";
                    string finalMessage = string.Format(msgTemplate, latestVersionString.Trim());

                    MessageBoxResult result = System.Windows.MessageBox.Show(finalMessage, titleTemplate, MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        AppHelpers.OpenUrl(AppGlobals.UpdateUrl);
                    }
                }
            }
            catch
            {
                // Silent fail if no internet or GitHub blocks the request
            }
        }

        // The shutdown sequence
        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up the tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            // Clean up temp files on app exit
            foreach (var item in AppGlobals.SessionHistory)
            {
                try
                {
                    string tempFolder = Path.GetTempPath();
                    if (item.FilePath.StartsWith(tempFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
                    }
                }
                catch { } // Skip locked files silently
            }

            base.OnExit(e);
        }


        // ================================================ //
        // 2. SYSTEM TRAY & MENUS
        // ================================================ //

        // Declare the tray icon and its context menu items for localization
        private static System.Windows.Forms.NotifyIcon? _trayIcon;
        public static System.Windows.Forms.ToolStripMenuItem TrayAddPocketItem;
        public static System.Windows.Forms.ToolStripMenuItem TrayAddClipboardItem;
        public static System.Windows.Forms.ToolStripMenuItem TraySavedPocketsItem;
        public static System.Windows.Forms.ToolStripMenuItem TraySettingsItem;
        public static System.Windows.Forms.ToolStripMenuItem TrayReportBugItem;
        public static System.Windows.Forms.ToolStripMenuItem TrayQuitItem;

        // System Tray builder
        private void InitializeSystemTray()
        {
            var trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.ShowImageMargin = false;

            TrayAddPocketItem = new System.Windows.Forms.ToolStripMenuItem();
            TrayAddPocketItem.Click += (s, ev) => ShowPocketWindow(false, false);

            TrayAddClipboardItem = new System.Windows.Forms.ToolStripMenuItem();
            TrayAddClipboardItem.Click += (s, ev) => ShowPocketWindow(true, false);

            TraySavedPocketsItem = new System.Windows.Forms.ToolStripMenuItem();
            TraySavedPocketsItem.Click += (s, ev) => ShowMyPocketsWindow(false);

            TraySettingsItem = new System.Windows.Forms.ToolStripMenuItem();
            TraySettingsItem.Click += (s, ev) =>
            {
                var existingSettings = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
                if (existingSettings != null)
                {
                    if (existingSettings.WindowState == WindowState.Minimized) existingSettings.WindowState = WindowState.Normal;
                    existingSettings.Activate();
                }
                else
                {
                    var settingsWindow = new SettingsWindow();
                    settingsWindow.Show();
                    settingsWindow.Activate();
                }
            };

            TrayReportBugItem = new System.Windows.Forms.ToolStripMenuItem();
            TrayReportBugItem.Click += (s, ev) => AppHelpers.OpenUrl("https://github.com/naofunyan/PocketDrop/issues");

            TrayQuitItem = new System.Windows.Forms.ToolStripMenuItem();
            TrayQuitItem.Click += (s, ev) => System.Windows.Application.Current.Shutdown();

            // Apply translations before adding to menu
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
            var iconUri = new Uri("pack://application:,,,/Assets/PocketDrop.ico", UriKind.Absolute);
            using (var iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream)
            {
                _trayIcon.Icon = new System.Drawing.Icon(iconStream);
            }

            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.Text = "PocketDrop";

            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    ShowMyPocketsWindow(true);
                }
            };
        }

        // Load tray label translations on startup and language change
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


        // ================================================ //
        // 3. GLOBAL INPUT HANDLING (HOTKEYS)
        // ================================================ //

        // Add required OS constants for hotkey registration
        private const int HOTKEY_NEW_POCKET = 9001;
        private const int HOTKEY_NEW_CLIPBOARD = 9002;

        // Force thread sync on startup and shortcut settings change
        public static void ReloadHotkeys()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AppGlobals.SaveSettings();

                // 1. Unregister existing hotkeys
                UnregisterHotKey(IntPtr.Zero, HOTKEY_NEW_POCKET);
                UnregisterHotKey(IntPtr.Zero, HOTKEY_NEW_CLIPBOARD);

                // 2. Ask Windows for the new keys, and capture the response (true = success, false = rejected)
                bool successPocket = RegisterHotKey(IntPtr.Zero, HOTKEY_NEW_POCKET, AppGlobals.PocketModifiers, AppGlobals.PocketKeyVK);
                bool successClipboard = RegisterHotKey(IntPtr.Zero, HOTKEY_NEW_CLIPBOARD, AppGlobals.ClipboardModifiers, AppGlobals.ClipboardKeyVK);

                // 3. Show an alert if hotkey registration fails
                if (!successPocket || !successClipboard)
                {
                    string titleText = (string)System.Windows.Application.Current.TryFindResource("Text_ShortcutErrorTitle") ?? "Shortcut Error";
                    string msgText = (string)System.Windows.Application.Current.TryFindResource("Text_ShortcutErrorMsg") ?? "Windows blocked this shortcut!\n\nThis usually means another background app (like Snipping Tool, PowerToys, or a graphics overlay) is already holding this key hostage.\n\nPlease try a different letter.";

                    System.Windows.MessageBox.Show(
                        msgText,
                        titleText,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            });
        }

        // Listen for global hotkey messages on the UI thread
        private void ComponentDispatcher_ThreadPreprocessMessage(ref System.Windows.Interop.MSG msg, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg.message == WM_HOTKEY)
            {
                int id = msg.wParam.ToInt32();

                // Win + Shift + Z
                if (id == HOTKEY_NEW_POCKET)
                {
                    ShowPocketWindow(false, true);
                    handled = true;
                }
                // Win + Shift + X
                else if (id == HOTKEY_NEW_CLIPBOARD)
                {
                    ShowPocketWindow(true, true);
                    handled = true;
                }
            }
        }


        // ================================================ //
        // 4. WINDOW MANAGEMENT
        // ================================================ //

        // Open a new Pocket Window
        private void ShowPocketWindow(bool pasteFromClipboard, bool atCursor)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Reuse an existing idle Pocket or create a new one
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

                if (atCursor)
                {
                    // Get current mouse position and place the pocket there
                    GetCursorPos(out POINT mousePos);
                    targetPocket.ShowPocketDrop(mousePos.X, mousePos.Y);
                }
                else
                {
                    // Ensure the window is visible and interactive at its default/saved position
                    targetPocket.Opacity = 1;
                    targetPocket.IsHitTestVisible = true;
                    targetPocket.Activate();
                }

                // Auto-paste clipboard if requested
                if (pasteFromClipboard)
                {
                    if (atCursor)
                    {
                        // Wait 100ms for the window to expand at the cursor, then paste
                        System.Threading.Tasks.Task.Delay(100).ContinueWith(_ =>
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                targetPocket.PasteFromClipboard();
                            });
                        });
                    }
                    else
                    {
                        targetPocket.PasteFromClipboard();
                    }
                }
            });
        }

        // My Pockets Window
        private async void ShowMyPocketsWindow(bool delayForTrayMenu)
        {
            if (delayForTrayMenu)
            {
                // Send a fake Escape keypress to dismiss the tray menu
                System.Windows.Forms.SendKeys.SendWait("{ESC}");
            }

            var existingWindow = System.Windows.Application.Current.Windows.OfType<MyPocketsWindow>().FirstOrDefault();
            MyPocketsWindow targetWindow;

            if (existingWindow != null)
            {
                targetWindow = existingWindow;
                targetWindow.Activate();
            }
            else
            {
                targetWindow = new MyPocketsWindow();
                targetWindow.Show();
            }

            if (delayForTrayMenu)
            {
                await System.Threading.Tasks.Task.Delay(100); // Wait for the ESC dismiss animation to finish
            }

            targetWindow.Activate();
            targetWindow.Focus();

            var hwnd = new System.Windows.Interop.WindowInteropHelper(targetWindow).Handle;
            SetForegroundWindow(hwnd);
        }
    }
}