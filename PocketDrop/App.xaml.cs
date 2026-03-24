using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace PocketDrop
{
    public partial class App : Application
    {

        // Global master list to hold every file dropped during this session
        public static List<PocketItem> SessionHistory = new List<PocketItem>();

        private System.Windows.Forms.NotifyIcon? _trayIcon;

        // ✨ NEW: Native API to force focus and dismiss the tray menu!
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // --- GLOBAL HOTKEY APIS ---
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(ref Win32Point pt);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct Win32Point
        {
            public int X;
            public int Y;
        };

        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const int HOTKEY_NEW_POCKET = 9001;
        private const int HOTKEY_NEW_CLIPBOARD = 9002;

        // ✨ NEW: Dynamic variables instead of locked constants!
        public static uint PocketKeyVK = 0x5A; // Z
        public static uint ClipboardKeyVK = 0x58; // X
        public static string PocketKeyChar = "Z";
        public static string ClipboardKeyChar = "X";

        // ✨ NEW SHAKE SETTINGS
        public static bool EnableMouseShake = true;
        public static int ShakeMinimumDistance = 100; // Default to 100 pixels
        public static bool DisableInGameMode = true;

        // --- NATIVE GAME MODE DETECTION ---
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int pquns);

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


        // ✨ THE FIX: Save the keys permanently to the Windows Registry!
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

                    EnableMouseShake = Convert.ToBoolean(key.GetValue("EnableMouseShake", true));
                    ShakeMinimumDistance = Convert.ToInt32(key.GetValue("ShakeMinimumDistance", 100));
                    DisableInGameMode = Convert.ToBoolean(key.GetValue("DisableInGameMode", true));

                    ExcludedApps = key.GetValue("ExcludedApps", "").ToString();

                    PocketPlacement = Convert.ToInt32(key.GetValue("PocketPlacement", 0));

                    ItemsLayoutMode = Convert.ToInt32(key.GetValue("ItemsLayoutMode", 0));

                    CloseWhenEmptied = Convert.ToBoolean(key.GetValue("CloseWhenEmptied", true));
                }
            }
            catch { }
        }

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

                    key.SetValue("EnableMouseShake", EnableMouseShake);
                    key.SetValue("ShakeMinimumDistance", ShakeMinimumDistance);
                    key.SetValue("DisableInGameMode", DisableInGameMode);

                    key.SetValue("ExcludedApps", ExcludedApps);

                    key.SetValue("PocketPlacement", PocketPlacement);

                    key.SetValue("ItemsLayoutMode", ItemsLayoutMode);

                    key.SetValue("CloseWhenEmptied", CloseWhenEmptied);
                }
            }
            catch { }
        }

        // ✨ THE MASTER FIX 2: Force Thread Synchronization!
        public static void ReloadHotkeys()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                SaveSettings();

                // 1. Wipe the old keys
                UnregisterHotKey(IntPtr.Zero, HOTKEY_NEW_POCKET);
                UnregisterHotKey(IntPtr.Zero, HOTKEY_NEW_CLIPBOARD);

                // 2. Ask Windows for the new keys, and capture its response (true = success, false = rejected)
                bool successPocket = RegisterHotKey(IntPtr.Zero, HOTKEY_NEW_POCKET, MOD_WIN | MOD_SHIFT, PocketKeyVK);
                bool successClipboard = RegisterHotKey(IntPtr.Zero, HOTKEY_NEW_CLIPBOARD, MOD_WIN | MOD_SHIFT, ClipboardKeyVK);

                // 3. If Windows says NO, alert the user!
                if (!successPocket || !successClipboard)
                {
                    System.Windows.MessageBox.Show(
                        "Windows blocked this shortcut!\n\nThis usually means another background app (like Snipping Tool, PowerToys, or a graphics overlay) is already holding this key hostage.\n\nPlease try a different letter.",
                        "Shortcut Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            });
        }

        // ✨ THE NEW SETTING STATE: Copy by default (true). If false, it's a Move.
        public static bool CopyItemToDestination { get; set; } = true;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // THE FIX: Tell WPF to stay alive in the background even if zero windows are open!
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.ShowImageMargin = false;

            // --- NEW: Spawns a brand new Pocket Window ---
            var addPocketItem = new System.Windows.Forms.ToolStripMenuItem("Add Pocket");
            addPocketItem.Click += (s, ev) =>
            {
                var newPocket = new MainWindow();
                newPocket.Show();

                // THE FIX: Force the window to be fully visible and interactive!
                newPocket.Opacity = 1;
                newPocket.IsHitTestVisible = true;
                newPocket.Activate(); // Brings it to the front of your screen
            };

            // --- NEW: Spawns a new Pocket and Pastes from Clipboard ---
            var addClipboardItem = new System.Windows.Forms.ToolStripMenuItem("Add Pocket from clipboard");
            addClipboardItem.Click += (s, ev) =>
            {
                var newPocket = new MainWindow();
                newPocket.Show();

                // THE FIX: Force the window to be fully visible and interactive!
                newPocket.Opacity = 1;
                newPocket.IsHitTestVisible = true;
                newPocket.Activate();

                newPocket.PasteFromClipboard();
            };

            var savedPocketsItem = new System.Windows.Forms.ToolStripMenuItem("Saved Pockets");
            savedPocketsItem.Click += (s, ev) =>
            {
                // Check if the window is already open!
                var existingWindow = Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();

                if (existingWindow != null)
                {
                    // If it is, just bring it to the front
                    existingWindow.Activate();
                }
                else
                {
                    // If it isn't, spawn a new one
                    var historyWindow = new SavedPocketsWindow();
                    historyWindow.Show();
                    historyWindow.Activate();
                }
            };

            var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Settings");
            settingsItem.Click += (s, ev) =>
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.Show();
                settingsWindow.Activate();
            };

            var reportBugItem = new System.Windows.Forms.ToolStripMenuItem("Report bug");
            reportBugItem.Click += (s, ev) =>
            {
                try
                {
                    // This will open the user's default web browser to your issue tracker!
                    // (You can replace this URL with your actual GitHub/GitLab repo later)
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/naofunyan/PocketDrop/issues",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Could not open bug reporter: {ex.Message}");
                }
            };

            var quitItem = new System.Windows.Forms.ToolStripMenuItem("Quit Dropshelf");
            quitItem.Click += (s, ev) =>
            {
                Application.Current.Shutdown();
            };

            // ✨ Register Global Hotkeys (IntPtr.Zero attaches them to the app's main background thread)
            LoadSettings();
            ReloadHotkeys();

            // Tell WPF to listen for global Windows messages
            System.Windows.Interop.ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;

            trayMenu.Items.Add(addPocketItem);
            trayMenu.Items.Add(addClipboardItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(savedPocketsItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(settingsItem);
            trayMenu.Items.Add(reportBugItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(quitItem);

            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.Text = "PocketDrop";

            // ✨ THE FIX: Open Saved Pockets on Left-Click!
            _trayIcon.MouseClick += async (s, e) =>
            {
                // Only trigger on Left click
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    // ✨ THE NEW MAGIC TRICK: Fire a fake 'Escape' keypress!
                    // This forces Windows to instantly break the Hover Lock and close the tray menu.
                    System.Windows.Forms.SendKeys.SendWait("{ESC}");

                    var existingWindow = Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();
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

                    // Keep our tiny delay to let the ESC key closing animation finish smoothly
                    await System.Threading.Tasks.Task.Delay(100);

                    // Now command the absolute OS foreground!
                    targetWindow.Activate();
                    targetWindow.Focus();

                    var hwnd = new System.Windows.Interop.WindowInteropHelper(targetWindow).Handle;
                    SetForegroundWindow(hwnd);
                }
            };
        }

        private void TrayIcon_Click(object? sender, EventArgs e)
        {
            // For right now, just show a message box. 
            MessageBox.Show($"Your session history currently holds {SessionHistory.Count} items.");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up the tray icon so it doesn't linger after closing
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            // FINAL CLEANUP: Wipe all temp files when the app actually quits
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

        // --- CATCHING THE GLOBAL KEYSTROKE ---
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

        // --- SPAWNING THE POCKET ---
        private void SpawnPocketAtCursor(bool pasteFromClipboard)
        {
            // 1. Find exactly where the mouse is right now
            Win32Point mousePos = new Win32Point();
            GetCursorPos(ref mousePos);

            // 2. Find a sleeping pocket, or spawn a fresh one
            var hiddenPocket = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => !w.IsHitTestVisible);
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

            // 3. Trigger the animation right at the mouse cursor!
            targetPocket.ShowPocketDrop(mousePos.X, mousePos.Y);

            // 4. If they pressed X, automatically pull the clipboard data!
            if (pasteFromClipboard)
            {
                // Wait just 100ms for the window to finish expanding, then paste
                System.Threading.Tasks.Task.Delay(100).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        targetPocket.PasteFromClipboard();
                    });
                });
            }
        }

        // ✨ EXCLUDED APPS SETTINGS
        public static string ExcludedApps = "";

        // ✨ POCKET PREFERENCES
        public static int PocketPlacement = 0; // 0 = Near mouse, 1 = Top edge, etc.

        public static int ItemsLayoutMode = 0;

        public static bool CloseWhenEmptied = true;

        // --- NATIVE FOREGROUND WINDOW DETECTION ---
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static bool IsForegroundAppExcluded()
        {
            if (string.IsNullOrWhiteSpace(ExcludedApps)) return false;

            try
            {
                // 1. Get the currently active window
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return false;

                // 2. Get the Process ID of that window
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return false;

                // 3. Look up the process name
                using (var process = System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    string pName = process.ProcessName.ToLower(); // e.g., "notepad" or "msedge"
                    string pExe = pName + ".exe";                 // e.g., "notepad.exe"

                    // 4. Split the user's text box by new lines
                    var rules = ExcludedApps.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var ruleText in rules)
                    {
                        string rule = ruleText.Trim().ToLower();
                        if (string.IsNullOrEmpty(rule)) continue;

                        // Matches the logic: "notepad" matches "notepad++", but "notepad.exe" is exact
                        if (pName.Contains(rule) || pExe.Contains(rule))
                        {
                            return true; // Abort the shake!
                        }
                    }
                }
            }
            catch { } // Failsafe (e.g., system processes we don't have permission to read)

            return false;
        }
    }
}