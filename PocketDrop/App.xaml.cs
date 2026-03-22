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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // THE FIX: Tell WPF to stay alive in the background even if zero windows are open!
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var trayMenu = new System.Windows.Forms.ContextMenuStrip();

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

            var historyItem = new System.Windows.Forms.ToolStripMenuItem("Session History");
            historyItem.Click += TrayIcon_Click;

            var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Settings");

            var quitItem = new System.Windows.Forms.ToolStripMenuItem("Quit Dropshelf");
            quitItem.Click += (s, ev) =>
            {
                Application.Current.Shutdown();
            };

            trayMenu.Items.Add(addPocketItem);
            trayMenu.Items.Add(addClipboardItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(historyItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(settingsItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            trayMenu.Items.Add(quitItem);

            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.Text = "PocketDrop";

            _trayIcon.Click += (s, ev) =>
            {
                var mouseArgs = ev as System.Windows.Forms.MouseEventArgs;
                if (mouseArgs != null && mouseArgs.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    TrayIcon_Click(s, ev);
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