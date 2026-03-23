using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

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
    }
}