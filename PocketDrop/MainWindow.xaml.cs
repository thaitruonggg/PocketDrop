// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;
using static PocketDrop.AppHelpers;

namespace PocketDrop
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // ================================================ //
        // 1. NATIVE WINDOWS APIS (P/INVOKE)
        // ================================================ //

        // Add native interop for Windows Share UI
        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        // Low-level global mouse hook
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public System.Drawing.Point pt;
            public uint mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        // Native File Picker
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO poainfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENASINFO
        {
            public string pcszFile;
            public string pcszClass;
            public int oaUIAction;
        }

        // Native Window Focus API
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);


        // ================================================ //
        // 2. STATE & VARIABLES
        // ================================================ //

        // Mouse Tracking & Shake
        private static IntPtr _hookHandle = IntPtr.Zero;
        private static LowLevelMouseProc _hookProc;
        private static ShakeDetector _shakeDetector = new ShakeDetector();
        private static bool _leftButtonHeld = false;
        private static bool _hasSpawnedPocketThisDrag = false;

        // Core Data
        // Holds multiple items and updates the UI automatically
        public ObservableCollection<PocketItem> PocketedItems { get; set; } = new ObservableCollection<PocketItem>();
        public bool IsGhost { get; set; } = false;
        private bool _isDraggingFromApp = false; // The safety flag to prevent self-drops
        private Point? startPoint = null;

        // Share Lifecycle
        private List<string> _filesToSharePaths;
        private DataTransferManager _shareManager;

        // View Mode Binding
        private string _currentViewMode = "Grid"; // Default to Grid
        public string CurrentViewMode
        {
            get => _currentViewMode;
            set
            {
                _currentViewMode = value;
                OnPropertyChanged(nameof(CurrentViewMode));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Thread-safe icon cache to prevent memory leaks during mass file drops
        private static System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource> _iconCache = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource>(StringComparer.OrdinalIgnoreCase);
        // Idle timer to trigger aggressive memory cleanup
        private DispatcherTimer _idleMemoryTimer;

        // ================================================ //
        // 3. LIFECYCLE (STARTUP & SHUTDOWN)
        // ================================================ //
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            // Start hidden — shake to reveal
            this.Opacity = 0;
            this.IsHitTestVisible = false;

            // Install global hook only once per application lifetime
            if (_hookHandle == IntPtr.Zero)
            {
                _hookProc = HookCallback;
                using (var proc = Process.GetCurrentProcess())
                using (var mod = proc.MainModule)
                    _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(mod.ModuleName), 0);
            }

            // Clean up heavy temp files from previous sessions
            System.Threading.Tasks.Task.Run(() => CleanupOldShareZips());

            // Setup the Idle Timer
            _idleMemoryTimer = new DispatcherTimer();
            _idleMemoryTimer.Interval = TimeSpan.FromSeconds(10);
            _idleMemoryTimer.Tick += (s, e) =>
            {
                _idleMemoryTimer.Stop(); // Stop the timer

                // Force the garbage collection
                System.Threading.Tasks.Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }

        // Add safe external kill switch to clear and close window
        public void ForceClose()
        {
            IsGhost = true;

            // Let HidePocketDrop handle all the cleanup and closing
            bool isLastWindow = Application.Current.Windows.OfType<MainWindow>().Count() <= 1;
            HidePocketDrop(!isLastWindow);
        }


        // ================================================ //
        // 4. CORE DRAG & DROP LOGIC
        // ================================================ //

        // Drag hover effects
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                DragGlowBorder.Visibility = Visibility.Visible; // Activate overlay on drag enter
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            DragGlowBorder.Visibility = Visibility.Collapsed; // Deactivate overlay on drag leave
        }

        // Handle file drop event
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            // Reset glow and margin on file drop
            DragGlowBorder.Visibility = Visibility.Collapsed;

            // Cancel drop if files dropped back onto the app
            if (_isDraggingFromApp)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            // 1. Handle standard file types (.png, .pdf, .txt, etc.)
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (droppedFiles != null && droppedFiles.Length > 0)
                {
                    foreach (string filePath in droppedFiles)
                    {
                        // Scenario 1: Skip file if exact path already exists in Pocket
                        if (AppHelpers.IsDuplicate(PocketedItems, filePath))
                        {
                            continue;
                        }

                        // Scenario 2: Reject 0-byte files (corrupted, empty, or currently downloading)
                        try
                        {
                            if (File.Exists(filePath))
                            {
                                FileInfo fi = new FileInfo(filePath);
                                if (fi.Length == 0)
                                {
                                    Console.WriteLine($"Skipped 0-byte file: {filePath}");
                                    continue; // Skip processing this file entirely
                                }
                            }
                        }
                        catch
                        {
                            continue; // If we can't even read the file size (strict lock), skip it safely!
                        }

                        // Scenario 3: Auto-rename if the name is taken, but the path is different
                        string finalDisplayName = AppHelpers.GetSafeDisplayName(PocketedItems, filePath);

                        System.Windows.Media.ImageSource fileIcon = await LoadFileIconAsync(filePath);

                        // Create item using resolved display name
                        var newItem = new PocketItem { FileName = finalDisplayName, FilePath = filePath, Icon = fileIcon };
                        PocketedItems.Add(newItem);

                        // Sync dropped file to global list immediately on drop
                        if (!App.SessionHistory.Exists(x => x.FilePath == newItem.FilePath))
                        {
                            App.SessionHistory.Add(newItem);
                        }
                    }

                    // Refresh UI after items load
                    StatusText.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;
                    UpdateStackPreview();

                    if (ItemsListBox == null || ItemsListBox.SelectedItems.Count == 0)
                    {
                        UpdateItemCountDisplay(PocketedItems.Count);
                        ResetIdleMemoryTimer();
                    }
                }
            }

            // 2. Handle URL drops from web browsers
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                string droppedText = (string)e.Data.GetData(DataFormats.Text);

                // Check if the text is a valid web link (http or https)
                if (Uri.TryCreate(droppedText, UriKind.Absolute, out Uri uriResult) &&
                   (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    try
                    {
                        string domain = uriResult.Host.Replace("www.", "");

                        // URL scenario 3: Number multiple dropped links from the same domain
                        string finalDomainName = AppHelpers.GetSafeDisplayName(PocketedItems, domain);

                        string tempFolder = Path.GetTempPath();
                        string fileName = $"{domain} Link_{DateTime.Now.Ticks}.url";
                        string filePath = Path.Combine(tempFolder, fileName);

                        // Generate physical shortcut file on URL drop
                        File.WriteAllText(filePath, $"[InternetShortcut]\nURL={droppedText}");

                        ShellObject shellObj = ShellObject.FromParsingName(filePath);

                        var fileIcon = shellObj.Thumbnail.LargeBitmapSource;
                        fileIcon.Freeze();

                        // Create the URL item
                        var newUrlItem = new PocketItem { FileName = finalDomainName, FilePath = filePath, Icon = fileIcon };
                        PocketedItems.Add(newUrlItem);

                        // Sync dropped URL to global list immediately on drop
                        if (!App.SessionHistory.Exists(x => x.FilePath == newUrlItem.FilePath))
                        {
                            App.SessionHistory.Add(newUrlItem);
                        }

                        // Refresh UI after items load
                        StatusText.Visibility = Visibility.Collapsed;
                        FileIconContainer.Visibility = Visibility.Visible;
                        UpdateStackPreview();

                        if (ItemsListBox == null || ItemsListBox.SelectedItems.Count == 0)
                        {
                            UpdateItemCountDisplay(PocketedItems.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not save URL: {ex.Message}");
                    }
                }
            }
        }

        // Handle file paste from Windows clipboard
        public async void PasteFromClipboard()
        {
            try
            {
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var files = System.Windows.Clipboard.GetFileDropList();
                    string[] fileArray = new string[files.Count];
                    files.CopyTo(fileArray, 0);

                    // Loop through the clipboard files and process the same as drag-and-drop
                    foreach (string filePath in fileArray)
                    {
                        string fileName = Path.GetFileName(filePath);

                        System.Windows.Media.ImageSource fileIcon = await LoadFileIconAsync(filePath);

                        // Create item, add to pocket, and sync to global list
                        var newItem = new PocketItem { FileName = fileName, FilePath = filePath, Icon = fileIcon };
                        PocketedItems.Add(newItem);

                        // Sync pasted file to global list immediately on paste
                        if (!App.SessionHistory.Exists(x => x.FilePath == newItem.FilePath))
                        {
                            App.SessionHistory.Add(newItem);
                        }
                    }

                    // Refresh UI after paste load
                    StatusText.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;
                    UpdateStackPreview();

                    UpdateItemCountDisplay(PocketedItems.Count);

                    // Ping the window: Notify My Pockets window to refresh in real-time
                    var openHistoryWindow = Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();
                    if (openHistoryWindow != null)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            openHistoryWindow.RefreshHistory();
                        }));
                    }
                }
                else
                {
                    // Show warning when clipboard has no files
                    string emptyDesc = (string)Application.Current.Resources["Text_ClipboardEmpty"];
                    string emptyTitle = (string)Application.Current.Resources["Text_ClipboardEmptyTitle"];
                    MessageBox.Show(emptyDesc, emptyTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Clipboard error: {ex.Message}");
            }
        }

        // Track click to prepare file drag-out
        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ignore drag initiation on UI control clicks
            if (e.Source == CloseButton || e.Source == ExpandButton || e.Source == TopBar || e.Source == DragHandle)
                return;

            startPoint = e.GetPosition(null); // Record the exact start point
        }

        // Reset drag state on mouse release
        private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            startPoint = null; // Clear drag origin on mouse release to prevent stuck drag
        }


        // Handle file drag-out on mouse release
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            // Abort drag-out if mouse not pressed, no start point, or pocket empty
            if (e.LeftButton != MouseButtonState.Pressed || startPoint == null || PocketedItems.Count == 0)
            {
                startPoint = null; // Add extra safety reset after drag-out
                return;
            }

            Point mousePos = e.GetPosition(null);
            Vector diff = (Point)startPoint - mousePos;

            // Only start if the mouse has moved significantly (Drag threshold)
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                string[] pathsToDrag = new string[PocketedItems.Count];
                for (int i = 0; i < PocketedItems.Count; i++)
                {
                    pathsToDrag[i] = PocketedItems[i].FilePath;
                }

                DataObject dragData = new DataObject(DataFormats.FileDrop, pathsToDrag);

                // Force Windows Explorer to copy or move on drag-out
                // 1 = Copy (Leaves original file), 2 = Move (Deletes original file)
                byte[] dropEffect = new byte[] { (byte)(App.CopyItemToDestination ? 1 : 2), 0, 0, 0 };
                dragData.SetData("Preferred DropEffect", new System.IO.MemoryStream(dropEffect));

                Point tempStart = (Point)startPoint;
                startPoint = null;

                _isDraggingFromApp = true;
                DragDropEffects allowedEffects = App.CopyItemToDestination ? DragDropEffects.Copy : DragDropEffects.Move;
                DragDropEffects result = DragDrop.DoDragDrop((DependencyObject)sender, dragData, allowedEffects);
                _isDraggingFromApp = false;

                // Always clear Pocket after successful drop
                if (result != DragDropEffects.None)
                {
                    foreach (var item in PocketedItems)
                    {
                        CleanupTempFile(item.FilePath);
                    }

                    PocketedItems.Clear();

                    // Check auto-close condition after drop
                    if (App.CloseWhenEmptied)
                    {
                        ExpandButton.IsChecked = false; // Ensure popup closes
                        ForceClose();
                    }
                    else
                    {
                        // Reset UI when Pocket stays open after drop
                        StackContainer.Children.Clear();
                        ExpandButton.IsChecked = false;
                        UpdateItemCountDisplay(0);

                        if (SelectAllCheckBox != null)
                            SelectAllCheckBox.IsChecked = false;
                    }
                }
            }

            // Ping the window: Notify My Pockets window to refresh in real-time
            var openHistoryWindow = Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();
            if (openHistoryWindow != null)
            {
                // Use Dispatcher to safely trigger UI update from background thread
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    openHistoryWindow.RefreshHistory();
                }));
            }
        }

        private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Only initiate drag when left mouse button is held
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            // Just-In-Time check: Check for deleted files just before drag-out
            if (CheckForMissingFiles()) return;

            // Ignore drag if target is not a ListBoxItem
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && !(hit is ListBoxItem) && !(hit is ListBox))
                hit = VisualTreeHelper.GetParent(hit);

            if (!(hit is ListBoxItem lbi))
                return; // Ignore drag on empty list space

            var draggedItem = lbi.DataContext as PocketItem;
            if (draggedItem == null)
                return;

            // Auto-select item on drag if not already selected
            if (!ItemsListBox.SelectedItems.Contains(draggedItem))
            {
                ItemsListBox.SelectedItems.Add(draggedItem);
            }

            var selectedItems = ItemsListBox.SelectedItems.Cast<PocketItem>().ToList();
            if (selectedItems.Count == 0) return;

            // Gather file paths and prepare drag payload
            string[] paths = selectedItems.Select(item => item.FilePath).ToArray();
            DataObject dragData = new DataObject(DataFormats.FileDrop, paths);

            byte[] dropEffect = new byte[] { (byte)(App.CopyItemToDestination ? 1 : 2), 0, 0, 0 };
            dragData.SetData("Preferred DropEffect", new System.IO.MemoryStream(dropEffect));

            _isDraggingFromApp = true;
            DragDropEffects allowedEffects = App.CopyItemToDestination ? DragDropEffects.Copy : DragDropEffects.Move;

            // Initiate drag-and-drop operation
            DragDropEffects result = DragDrop.DoDragDrop(ItemsListBox, dragData, allowedEffects);

            _isDraggingFromApp = false;

            // Cleanup after drag-and-drop completes
            if (result != DragDropEffects.None)
            {
                foreach (var item in selectedItems)
                {
                    CleanupTempFile(item.FilePath);
                    PocketedItems.Remove(item);
                }

                if (PocketedItems.Count == 0)
                {
                    if (App.CloseWhenEmptied)
                    {
                        ExpandButton.IsChecked = false;
                        ForceClose();
                    }
                    else
                    {
                        StatusText.Visibility = Visibility.Visible;
                        FileIconContainer.Visibility = Visibility.Collapsed;
                        StackContainer.Children.Clear();
                        ExpandButton.IsChecked = false;
                    }
                }
                else
                {
                    UpdateStackPreview();
                }

                UpdateItemCountDisplay(PocketedItems.Count);
            }
        }

        // Dragging the window
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent window drag when clicking the close button
            if (e.OriginalSource == CloseButton || e.Source == MoreButton)
                return;

            // Tell Windows to take over and drag the physical app window
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }


        // ================================================ //
        // 5. UI ANIMATIONS & RENDERING
        // ================================================ //

        // Show with animation
        public void ShowPocketDrop(int rawCursorX, int rawCursorY)
        {
            if (this.IsHitTestVisible) return;

            // Apply layout mode before UI size calculation
            // 0 = Grid view, 1 = List view
            this.CurrentViewMode = App.ItemsLayoutMode == 1 ? "List" : "Grid";

            this.UpdateLayout();

            // 1. Detect physical screen under the current mouse position
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(rawCursorX, rawCursorY));
            var rawWorkArea = screen.WorkingArea;

            // Calculate Windows DPI display scaling
            double dpiX = 1.0;
            double dpiY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            else
            {
                // Add failsafe for window not yet fully loaded
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpiX = g.DpiX / 96.0;
                    dpiY = g.DpiY / 96.0;
                }
            }

            // 2. Convert physical pixels to WPF logical units
            double workAreaLeft = rawWorkArea.Left / dpiX;
            double workAreaTop = rawWorkArea.Top / dpiY;
            double workAreaWidth = rawWorkArea.Width / dpiX;
            double workAreaHeight = rawWorkArea.Height / dpiY;
            double workAreaRight = workAreaLeft + workAreaWidth;
            double workAreaBottom = workAreaTop + workAreaHeight;

            double cursorX = rawCursorX / dpiX;
            double cursorY = rawCursorY / dpiY;

            // 3. Safely read window dimensions
            double w = this.ActualWidth > 0 ? this.ActualWidth : (double.IsNaN(this.Width) ? 380 : this.Width);
            double h = this.ActualHeight > 0 ? this.ActualHeight : (double.IsNaN(this.Height) ? 500 : this.Height);

            // 4. Override placement based on user setting using decoupled math
            Point finalPos = AppHelpers.CalculateWindowPosition(
                App.PocketPlacement,
                cursorX, cursorY, w, h,
                workAreaLeft, workAreaTop, workAreaRight, workAreaBottom);

            // 5. Apply final DPI-scaled window position
            this.Left = finalPos.X;
            this.Top = finalPos.Y;
            this.IsHitTestVisible = true;

            // Animations
            // 1. Fade In
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

            // 2. Bouncy Easing Function (Amplitude controls how much it overshoots and bounces back)
            var bounceEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

            // 3. Scale and Slide animations
            var scaleAnim = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = bounceEase };
            var slideAnim = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = bounceEase };

            // 4. Combine the transforms
            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(0.85, 0.85);
            var translateTransform = new TranslateTransform(0, 30);

            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);

            // 5. Apply scale transform to MainContainer from center origin
            MainContainer.RenderTransformOrigin = new Point(0.5, 0.5);
            MainContainer.RenderTransform = transformGroup;

            // 6. Run all animations in parallel
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);

            this.BeginAnimation(OpacityProperty, null); // Clear old animation
            this.BeginAnimation(OpacityProperty, fadeIn);

            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                this.Activate();
            }));
        }

        // Hide with animation
        private void HidePocketDrop(bool closeWindow = false)
        {
            // Disable window interaction immediately on close
            this.IsHitTestVisible = false;

            // 1. Fade Out
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            // 2. Shrink and Drop animations
            var scaleAnim = new DoubleAnimation(1, 0.85, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            var slideAnim = new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            // 3. Initialize transforms safely before animation
            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(1, 1);
            var translateTransform = new TranslateTransform(0, 0);

            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);

            MainContainer.RenderTransformOrigin = new Point(0.5, 0.5);
            MainContainer.RenderTransform = transformGroup;

            // 4. Defer UI cleanup until close animation completes
            fadeOut.Completed += (s, e) =>
            {
                PocketedItems.Clear();
                StackContainer.Children.Clear();
                UpdateItemCountDisplay(0);

                if (PopupCountText != null) PopupCountText.Text = "0 Items";
                if (SelectAllCheckBox != null) SelectAllCheckBox.IsChecked = false;

                System.Threading.Tasks.Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });

                // Destroy window after hide animation completes
                if (closeWindow)
                {
                    this.Close();
                }
            };

            // 5. Run all animations in parallel
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        // Update stack preview
        private void UpdateStackPreview()
        {
            StackContainer.Children.Clear();

            int count = PocketedItems.Count;
            if (count == 0) return;

            // Define rotation pattern for stacked card preview
            // Index 0 = bottom-most (oldest), last index = top (latest).
            double[] angles = { -11, 8, -7, 6, -5, 4, -4, 3, -3, 2, -2, 1, -1 };
            double[] offsetsX = { -7, 6, -5, 4, -4, 3, -3, 2, -2, 1, -1, 1, 0 };
            double[] offsetsY = { 5, 4, 4, 3, 3, 2, 2, 2, 1, 1, 1, 0, 0 };

            for (int i = 0; i < count; i++)
            {
                // i=0 is oldest (bottom), i=count-1 is newest (top)
                int patternIndex = Math.Min(count - 1 - i, angles.Length - 1);
                bool isTop = (i == count - 1);

                double angle = isTop ? 0 : angles[patternIndex];
                double offsetX = isTop ? 0 : offsetsX[patternIndex];
                double offsetY = isTop ? 0 : offsetsY[patternIndex];

                var img = new System.Windows.Controls.Image
                {
                    Source = PocketedItems[i].Icon,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Margin = new Thickness(4),
                    UseLayoutRounding = true,
                    SnapsToDevicePixels = true
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    img, System.Windows.Media.BitmapScalingMode.HighQuality);

                var card = new Border
                {
                    Width = 108,
                    Height = 88,
                    CornerRadius = new CornerRadius(8),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = img,
                    RenderTransform = new TransformGroup
                    {
                        Children = new TransformCollection
                        {
                            new RotateTransform(angle, 54, 44),
                            new TranslateTransform(offsetX, offsetY)
                        }
                    }
                };

                // Add drop shadow to top card only in stack preview
                if (isTop)
                {
                    card.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 10,
                        Color = System.Windows.Media.Colors.Black,
                        Opacity = 0.18,
                        Direction = 270,
                        ShadowDepth = 3
                    };
                }

                StackContainer.Children.Add(card);
            }
        }


        // ================================================ //
        // 6. CONTEXT MENU & POPUP ACTIONS
        // ================================================ //
        private void ViewMode_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag != null)
            {
                CurrentViewMode = border.Tag.ToString();
            }
        }

        // Select-all logic
        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            if (ItemsListBox != null)
                ItemsListBox.SelectAll();
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ItemsListBox != null)
                ItemsListBox.UnselectAll();
        }

        // Update header text on selection change
        private void ItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int selectedCount = ItemsListBox.SelectedItems.Count;

            if (selectedCount > 0)
            {
                long totalBytes = 0;
                foreach (PocketItem item in ItemsListBox.SelectedItems)
                {
                    if (File.Exists(item.FilePath))
                        totalBytes += new FileInfo(item.FilePath).Length;
                }

                string fileWord = selectedCount == 1
                    ? (string)Application.Current.Resources["Text_FileSelected"]
                    : (string)Application.Current.Resources["Text_FilesSelected"];

                PopupCountText.Text = $"{selectedCount} {fileWord} ({AppHelpers.FormatBytes(totalBytes)})";
            }
            else
            {
                UpdateItemCountDisplay(PocketedItems.Count);
            }

            if (DeleteSelectedButton != null)
            {
                // Show delete button when at least one item is selected
                DeleteSelectedButton.Visibility = ItemsListBox.SelectedItems.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            // Copy selected items before deletion to avoid enumeration errors
            var selectedItems = ItemsListBox.SelectedItems.Cast<PocketItem>().ToList();

            if (selectedItems.Count == 0) return;

            foreach (var item in selectedItems)
            {
                PocketedItems.Remove(item);
                CleanupTempFile(item.FilePath); // Delete if error
            }

            // Refresh background stack preview after popup closes
            if (PocketedItems.Count == 0)
            {
                if (StackContainer != null) StackContainer.Children.Clear();
            }
            else
            {
                UpdateStackPreview();
            }

            // Update item counter dynamically
            UpdateItemCountDisplay(PocketedItems.Count);

            // Hide delete button when selection is cleared
            DeleteSelectedButton.Visibility = Visibility.Collapsed;
        }

        // More Button dropdown menu
        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (MoreButton.ContextMenu != null)
            {
                // 1. Count the files
                int selectedCount = (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
                    ? ItemsListBox.SelectedItems.Count
                    : PocketedItems.Count;

                // 2. Dynamically link to the translation resources!
                if (selectedCount > 1)
                {
                    Menu_DynamicOpen.SetResourceReference(MenuItem.HeaderProperty, "Text_MenuOpen");
                }
                else
                {
                    Menu_DynamicOpen.SetResourceReference(MenuItem.HeaderProperty, "Text_MenuOpenWith");
                }

                // 3. Align the menu directly under the 3-dots icon
                MoreButton.ContextMenu.PlacementTarget = MoreButton;
                MoreButton.ContextMenu.Placement = PlacementMode.Bottom;
                MoreButton.ContextMenu.VerticalOffset = 8;

                // 4. Open the menu
                MoreButton.ContextMenu.IsOpen = true;
            }
        }

        // Menu action: Dynamic open
        private void Menu_DynamicOpen_Click(object sender, RoutedEventArgs e)
        {

            // JUST-IN-TIME check
            if (CheckForMissingFiles()) return;

            // 1. Gather the selected files (or all of them if none are selected)
            var itemsToOpen = (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
                ? ItemsListBox.SelectedItems.Cast<PocketItem>().ToList()
                : PocketedItems.ToList();

            if (itemsToOpen.Count == 0) return;

            // 2. Scenario A: Differentiate folder and file handling for single selection
            if (itemsToOpen.Count == 1)
            {
                string targetFilePath = itemsToOpen[0].FilePath;

                // Check if selected item is a folder before opening
                if (Directory.Exists(targetFilePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = targetFilePath,
                            UseShellExecute = true // Opens the folder in File Explorer
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not open folder: {ex.Message}");
                    }
                }
                // Show native open-with dialog for file selection
                else if (!string.IsNullOrEmpty(targetFilePath) && File.Exists(targetFilePath))
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            OPENASINFO info = new OPENASINFO();
                            info.pcszFile = targetFilePath;
                            info.pcszClass = null;
                            info.oaUIAction = 7;
                            SHOpenWithDialog(IntPtr.Zero, ref info);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not open file picker: {ex.Message}");
                        }
                    });
                }
            }
            // 3. Scenario B: Open all items when multiple are selected
            else
            {
                foreach (var item in itemsToOpen)
                {
                    // Check both directory and file existence before openin
                    if (Directory.Exists(item.FilePath) || File.Exists(item.FilePath))
                    {
                        try
                        {
                            // Use ShellExecute to open items like Explorer double-click
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = item.FilePath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not open {item.FileName}: {ex.Message}");
                        }
                    }
                }
            }

            // 4. Auto-close the pocket if the user enabled it in Settings
            if (App.CloseWhenOpenWith)
            {
                if (ExpandButton != null) ExpandButton.IsChecked = false; // Collapse the popup menu
                ForceClose(); // Animate window close and free memory
            }
        }

        // Menu action: Share
        private async void Menu_Share_Click(object sender, RoutedEventArgs e)
        {
            // JUST-IN-TIME check
            if (CheckForMissingFiles()) return;

            var itemsToShare = (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
                ? ItemsListBox.SelectedItems.Cast<PocketItem>().ToList()
                : PocketedItems.ToList();

            if (itemsToShare.Count == 0) return;
            if (ExpandButton != null) ExpandButton.IsChecked = false; // Collapse the popup menu

            bool containsFolders = itemsToShare.Any(item => Directory.Exists(item.FilePath));

            // 1. Handle folders and zipping first
            if (containsFolders)
            {
                if (App.AutoCompressFoldersShare)
                {
                    try
                    {
                        // Show loading UI
                        if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
                        if (StatusText != null)
                        {
                            StatusText.Text = (string)Application.Current.Resources["Text_CompressingShare"];
                            StatusText.Visibility = Visibility.Visible;
                        }

                        // Await ZIP completion before proceeding
                        string tempZipPath = await CreateTempZipFromItemsAsync(itemsToShare);

                        // Set the payload to the new zip file
                        _filesToSharePaths = new List<string> { tempZipPath };
                    }
                    catch (Exception ex)
                    {
                        string errorDesc = (string)Application.Current.Resources["Text_ShareCompressErrorDesc"];
                        string errorTitle = (string)Application.Current.Resources["Text_ShareCompressErrorTitle"];
                        string errorPrefix = (string)Application.Current.Resources["Text_ErrorPrefix"];

                        MessageBox.Show($"{errorDesc}\n\n{errorPrefix} {ex.Message}", errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    finally
                    {
                        // Clean up UI
                        if (StatusText != null) StatusText.Visibility = Visibility.Collapsed;
                        if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    string warningDesc = (string)Application.Current.Resources["Text_ShareFolderErrorDesc"];
                    string warningTitle = (string)Application.Current.Resources["Text_ShareFolderErrorTitle"];
                    MessageBox.Show(warningDesc, warningTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                // Use standard file paths when no folders are present
                _filesToSharePaths = itemsToShare.Select(item => item.FilePath).ToList();
            }

            // 2. Trigger the share UI
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                var factory = WinRT.ActivationFactory.Get("Windows.ApplicationModel.DataTransfer.DataTransferManager");
                var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);

                Guid guid = Guid.Parse("a5caee9b-8708-49d1-8d36-67d25a8da00c");
                IntPtr ptr = interop.GetForWindow(hwnd, ref guid);
                _shareManager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);

                // Hook the event and show the UI
                _shareManager.DataRequested -= ShareManager_DataRequested;
                _shareManager.DataRequested += ShareManager_DataRequested;
                interop.ShowShareUIForWindow(hwnd);

                // Ensure ZIP finishes and share UI opens before closing pocket
                if (App.CloseWhenShare)
                {
                    ForceClose(); // Safely animate window close and free memory
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Share error: {ex.Message}");
            }
        }

        // Menu action: Compress to ZIP
        private async void Menu_CompressZip_Click(object sender, RoutedEventArgs e)
        {
            // JUST-IN-TIME check
            if (CheckForMissingFiles()) return;

            // 1. Compress selected items or all items if none selected
            var itemsToCompress = (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
                ? ItemsListBox.SelectedItems.Cast<PocketItem>().ToList()
                : PocketedItems.ToList();

            if (itemsToCompress.Count == 0) return;

            // 2. Prompt user to choose ZIP save location
            Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = (string)Application.Current.Resources["Text_SaveZipTitle"],
                FileName = (string)Application.Current.Resources["Text_SaveZipFileName"],
                DefaultExt = ".zip",
                Filter = (string)Application.Current.Resources["Text_SaveZipFilter"]
            };

            if (saveDialog.ShowDialog() == true)
            {
                string zipPath = saveDialog.FileName;

                // Show loading state
                if (ExpandButton != null) ExpandButton.IsChecked = false; // Collapse the menu immediately

                if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
                if (StatusText != null)
                {
                    StatusText.Text = (string)Application.Current.Resources["Text_CompressingShare"] ?? "Compressing files...";
                    StatusText.Visibility = Visibility.Visible;
                }

                // 3. Compress in the background
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // Delete existing ZIP before creating new one
                        if (File.Exists(zipPath)) File.Delete(zipPath);

                        using (FileStream zipToOpen = new FileStream(zipPath, FileMode.Create))
                        using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                        {
                            foreach (var item in itemsToCompress)
                            {
                                // 1. Check if it is a folder
                                if (Directory.Exists(item.FilePath))
                                {
                                    AddDirectoryToZip(archive, item.FilePath, item.FileName);
                                }
                                // 2. Check if it is a standard file
                                else if (File.Exists(item.FilePath))
                                {
                                    archive.CreateEntryFromFile(item.FilePath, item.FileName);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not create ZIP: {ex.Message}");
                    }
                });

                // Restore UI (if the Pocket stays open after compression based on user settings)
                if (!App.CloseWhenCompress)
                {
                    if (StatusText != null) StatusText.Visibility = Visibility.Collapsed;
                    if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Visible;
                }

                // 4. Search existing Windows Explorer tabs
                try
                {
                    Type shellType = Type.GetTypeFromProgID("Shell.Application");
                    dynamic shell = Activator.CreateInstance(shellType);
                    bool windowFound = false;

                    // Convert file path to URI format for Explorer tab navigation
                    string targetUri = new Uri(Path.GetDirectoryName(zipPath)).AbsoluteUri;

                    // Check all open Explorer windows and tabs
                    foreach (dynamic window in shell.Windows())
                    {
                        if (window != null && window.LocationURL != null)
                        {
                            string loc = window.LocationURL;
                            if (loc.Equals(targetUri, StringComparison.OrdinalIgnoreCase))
                            {
                                windowFound = true;

                                // Grab the file inside the folder view
                                dynamic folderView = window.Document;
                                dynamic fileToSelect = folderView.Folder.ParseName(Path.GetFileName(zipPath));

                                if (fileToSelect != null)
                                {
                                    // Selection Flags: 1 (Select) + 4 (Ensure Visible) + 8 (Focus) = 13
                                    folderView.SelectItem(fileToSelect, 13);
                                }

                                // Force the existing Explorer window to pop to the front of the screen
                                IntPtr hwnd = (IntPtr)window.HWND;
                                SetForegroundWindow(hwnd);
                                break;
                            }
                        }
                    }

                    // If no existing tab had that folder open, safely spawn a new one
                    if (!windowFound)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Highlight error: {ex.Message}");
                    // Safe fallback just in case the COM object fails
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
                }
            }
            // Close the pocket if the user enabled the Settings
            if (App.CloseWhenCompress)
            {
                if (ExpandButton != null) ExpandButton.IsChecked = false; // Collapse the popup menu
                ForceClose(); // Safely animate window close and free memory
            }
        }

        // Menu action: Clear all
        private void Menu_ClearItems_Click(object sender, RoutedEventArgs e)
        {
            // Clean up temp files before clearing
            foreach (var item in PocketedItems)
            {
                CleanupTempFile(item.FilePath);
            }

            PocketedItems.Clear();
            StackContainer.Children.Clear();
            UpdateItemCountDisplay(0);
            ResetIdleMemoryTimer();

            if (SelectAllCheckBox != null)
                SelectAllCheckBox.IsChecked = false;

        }

        // Menu action: Settings
        private void Menu_Settings_Click(object sender, RoutedEventArgs e)
        {
            // Prevent duplicate settings window from opening
            var existingSettings = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

            if (existingSettings != null)
            {
                existingSettings.Activate(); // Bring it to the front
            }
            else
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.Show();
                settingsWindow.Activate();
            }
        }

        // Smart popup placement
        private CustomPopupPlacement[] Popup_PlacementCallback(Size popupSize, Size targetSize, Point offset)
        {
            // 1. Perfectly center X relative to the MainContainer
            double xNudge = -18;
            double xOffset = ((targetSize.Width - popupSize.Width) / 2.0) + xNudge;

            // 2. Plan A: Spawn BELOW the app
            double yBelow = targetSize.Height + 0;

            // 3. Plan B: Spawn ABOVE the app
            // Increase bottom margin to clear invisible border and drop shadow
            double yAbove = -popupSize.Height - 40;

            return new[]
            {
                new CustomPopupPlacement(new Point(xOffset, yBelow), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(xOffset, yAbove), PopupPrimaryAxis.Horizontal)
            };
        }

        // Cleanup when popup closes
        private void Popup_Closed(object sender, EventArgs e)
        {
            // 1. Unselect all files so they don't stay highlighted
            ItemsListBox.UnselectAll();

            // 2. Uncheck toggle button when popup closed by clicking outside
            ExpandButton.IsChecked = false;
        }

        // Closing the window — clears items and hides
        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            // 1. Log all current items to the Global History
            foreach (var item in PocketedItems)
            {
                if (!App.SessionHistory.Exists(x => x.FilePath == item.FilePath))
                {
                    App.SessionHistory.Add(item);
                }
            }

            // 2. Ping the My Pockets window to update in real-time
            var openHistoryWindow = Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();
            if (openHistoryWindow != null)
            {
                openHistoryWindow.RefreshHistory();
            }

            // 3. Play the animation, then let it close itself
            bool isLastWindow = Application.Current.Windows.OfType<MainWindow>().Count() <= 1;
            HidePocketDrop(!isLastWindow);
        }


        // ================================================ //
        // 7. UTILITY HELPERS
        // ================================================ //

        // Global mouse hook callback
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                if (msg == WM_LBUTTONDOWN)
                {
                    _leftButtonHeld = true;
                    _hasSpawnedPocketThisDrag = false;
                }
                else if (msg == WM_LBUTTONUP)
                {
                    _leftButtonHeld = false;
                }
                else if (msg == WM_MOUSEMOVE && _leftButtonHeld)
                {
                    // 1. Check all user settings before doing math
                    if (!App.EnableMouseShake) goto done;
                    if (App.DisableInGameMode && AppHelpers.IsGameModeActive()) goto done;
                    if (AppHelpers.IsForegroundAppExcluded()) goto done;
                    if (_hasSpawnedPocketThisDrag) goto done; // Don't spawn duplicates

                    // 2. Pass raw coordinates to decoupled placement logic
                    bool isShaking = _shakeDetector.CheckForShake(
                        currentMouseX: hookStruct.pt.X,
                        currentTimestampMs: Environment.TickCount64,
                        minDistancePx: App.ShakeMinimumDistance,
                        maxTimeMs: 1000,
                        requiredSwings: 3
                    );

                    // 3. If the math detects a shake, spawn the UI
                    if (isShaking)
                    {
                        _hasSpawnedPocketThisDrag = true; // Lock it down until the users release the mouse

                        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() =>
                        {
                            var hiddenPocket = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => !w.IsHitTestVisible);

                            if (hiddenPocket != null)
                            {
                                hiddenPocket.ShowPocketDrop(hookStruct.pt.X, hookStruct.pt.Y);
                            }
                            else
                            {
                                var newPocket = new MainWindow();
                                newPocket.Show();
                                newPocket.ShowPocketDrop(hookStruct.pt.X, hookStruct.pt.Y);
                            }
                        }));
                    }
                }
            }
        done:
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // Building the share payload
        private async void ShareManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            // Add micro-deferral to allow WinRT Storage API to resolve files
            DataRequestDeferral deferral = args.Request.GetDeferral();

            try
            {
                if (_filesToSharePaths == null || _filesToSharePaths.Count == 0) return;

                string singleTitle = (string)Application.Current.Resources["Text_ShareTitleSingle"];
                string multiTitleTemplate = (string)Application.Current.Resources["Text_ShareTitleMultiple"];

                args.Request.Data.Properties.Title = _filesToSharePaths.Count == 1
                    ? singleTitle
                    : string.Format(multiTitleTemplate, _filesToSharePaths.Count);

                // Map file paths to Windows Storage File objects
                List<IStorageItem> storageItems = new List<IStorageItem>();
                foreach (string path in _filesToSharePaths)
                {
                    if (File.Exists(path))
                    {
                        storageItems.Add(await StorageFile.GetFileFromPathAsync(path));
                    }
                }

                args.Request.Data.SetStorageItems(storageItems);
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    string errorTitle = (string)Application.Current.Resources["Text_ShareErrorTitle"];
                    string errorDesc = (string)Application.Current.Resources["Text_ShareErrorDesc"];
                    string reasonText = (string)Application.Current.Resources["Text_Reason"];

                    MessageBox.Show($"{errorDesc}\n\n{reasonText} {ex.Message}", errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                });

                string failText = (string)Application.Current.Resources["Text_ShareFailText"];
                args.Request.FailWithDisplayText($"{failText} {ex.Message}");
            }
            finally
            {
                // Unhook event and complete deferral to prevent ghost fires
                if (_shareManager != null)
                {
                    _shareManager.DataRequested -= ShareManager_DataRequested;
                }
                deferral.Complete();
            }
        }

        // Clean up leftover ZIPs from previous share sessions
        private void CleanupOldShareZips()
        {
            try
            {
                string tempPath = Path.GetTempPath();

                // Find all custom ZIP files in the temp folder
                string[] oldZips = Directory.GetFiles(tempPath, "PocketDrop_Share_*.zip");

                foreach (string zip in oldZips)
                {
                    // Only delete temp ZIPs older than one hour to avoid deleting active shares
                    FileInfo fi = new FileInfo(zip);
                    if (fi.CreationTime < DateTime.Now.AddHours(-1))
                    {
                        File.Delete(zip);
                    }
                }
            }
            catch
            {
                // Silently ignore any locked files.
            }
        }

        // Recursively add folder contents to the ZIP
        private void AddDirectoryToZip(System.IO.Compression.ZipArchive archive, string sourceDir, string entryRootName)
        {
            string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                // Calculate the relative path so the folder structure is preserved inside the ZIP
                string relativePath = file.Substring(sourceDir.Length).TrimStart('\\', '/');
                string zipEntryName = Path.Combine(entryRootName, relativePath).Replace('\\', '/');

                archive.CreateEntryFromFile(file, zipEntryName);
            }
        }

        // Silently compress mixed items into a temp ZIP
        private async System.Threading.Tasks.Task<string> CreateTempZipFromItemsAsync(List<PocketItem> items)
        {
            // Create a unique zip file in the Windows Temp directory
            string zipName = $"PocketDrop_Share_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            string zipPath = Path.Combine(Path.GetTempPath(), zipName);

            // Run the heavy compression on a background thread
            await System.Threading.Tasks.Task.Run(() =>
            {
                using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var item in items)
                    {
                        if (Directory.Exists(item.FilePath))
                        {
                            // Folder: Add the folder recursively
                            AddDirectoryToZip(archive, item.FilePath, item.FileName);
                        }
                        else if (File.Exists(item.FilePath))
                        {
                            // Standard file: Add it to the root of the zip
                            archive.CreateEntryFromFile(item.FilePath, item.FileName);
                        }
                    }
                }
            });

            return zipPath;
        }

        // Safely delete temporary files
        private void CleanupTempFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    // Only delete the file if path is within temp folder
                    string tempFolder = Path.GetTempPath();
                    if (filePath.StartsWith(tempFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not clean up temp file: {ex.Message}");
            }
        }

        // Syncs the Pocket UI with the global history
        public void RefreshPocketUI()
        {
            // 1. Find items in this Pocket that no longer exist in the global history
            var itemsToRemove = PocketedItems.Where(p => !App.SessionHistory.Exists(h => h.FilePath == p.FilePath)).ToList();

            if (itemsToRemove.Count == 0) return; // Nothing to sync

            // 2. Remove the deleted files from this Pocket's local memory
            foreach (var item in itemsToRemove)
            {
                PocketedItems.Remove(item);
            }

            // 3. Update the UI based on what is left
            if (PocketedItems.Count == 0)
            {
                // If deleting those files emptied the pocket completely
                if (App.CloseWhenEmptied)
                {
                    ForceClose(); // Safely animate and close
                }
                else
                {
                    // Or just reset it to the "Drop files here" state
                    StatusText.Visibility = Visibility.Visible;
                    FileIconContainer.Visibility = Visibility.Collapsed;
                    StackContainer.Children.Clear();
                    if (ExpandButton != null) ExpandButton.IsChecked = false;
                    UpdateItemCountDisplay(0);
                }
            }
            else
            {
                // If there are still files left, just redraw the card stack
                UpdateStackPreview();
                UpdateItemCountDisplay(PocketedItems.Count);
            }
        }

        // Updates text and translates dynamically
        private void UpdateItemCountDisplay(int count)
        {
            string translatedItemWord = count == 1
                ? (string)Application.Current.Resources["Text_Item"]
                : (string)Application.Current.Resources["Text_Items"];

            string displayText = $"{count} {translatedItemWord}";

            if (CountText != null) CountText.Text = displayText;
            if (PopupCountText != null) PopupCountText.Text = displayText;

            if (count == 0 && StatusText != null)
            {
                StatusText.Text = (string)Application.Current.Resources["Text_DropItemsHere"];
                StatusText.Visibility = Visibility.Visible;
                FileIconContainer.Visibility = Visibility.Collapsed;
            }
        }

        // Just-In-Time check for missing files before actions
        private bool CheckForMissingFiles()
        {
            if (AppHelpers.RemoveDeadFiles(PocketedItems))
            {
                string warningTitle = (string)Application.Current.Resources["Text_FilesMissingTitle"];
                string warningDesc = (string)Application.Current.Resources["Text_FilesMissingDesc"];
                MessageBox.Show(warningDesc, warningTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }
            return false;
        }

        private async System.Threading.Tasks.Task<System.Windows.Media.ImageSource> LoadFileIconAsync(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            string[] imageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
            string[] uniqueIconExts = { ".exe", ".ico", ".lnk", ".pdf",
                                        ".mp4", ".avi", ".mov", ".wmv", ".mkv",
                                        ".mp3", ".flac", ".m4a"};

            bool isImage = Array.Exists(imageExts, x => x == ext);
            bool isUnique = Array.Exists(uniqueIconExts, x => x == ext);
            bool isDirectory = Directory.Exists(filePath);

            // Use a special key for folders, otherwise use the file extension
            string cacheKey = isDirectory ? "folder_icon" : ext;

            // 1. CHECK THE CACHE FIRST (Skip for actual images and unique executables)
            if (!isImage && !isUnique)
            {
                if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
                {
                    return cachedIcon; // Instant return, 0 CPU used!
                }
            }

            // 2. IF NOT IN CACHE, ASK WINDOWS TO EXTRACT IT
            return await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (isImage)
                    {
                        BitmapImage img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        img.UriSource = new Uri(filePath);
                        img.DecodePixelWidth = 120;
                        img.EndInit();
                        img.Freeze();
                        return (System.Windows.Media.ImageSource)img;
                    }
                    else
                    {
                        ShellObject shellObj = ShellObject.FromParsingName(filePath);
                        var thumb = shellObj.Thumbnail.LargeBitmapSource;

                        // Freezing the thumbnail allows it to be safely shared across the UI
                        thumb.Freeze();

                        // 3. SAVE TO CACHE FOR NEXT TIME
                        if (!isUnique)
                        {
                            _iconCache.TryAdd(cacheKey, thumb);
                        }

                        return (System.Windows.Media.ImageSource)thumb;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Icon error: {ex.Message}");
                    return null;
                }
            });
        }

        // Call whenever the user performs an action to reset the 10-second countdown
        private void ResetIdleMemoryTimer()
        {
            _idleMemoryTimer.Stop();
            _idleMemoryTimer.Start();
        }
    }

    // --- The blueprint for a dropped item ---
    public class PocketItem
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public System.Windows.Media.ImageSource Icon { get; set; }
        public bool IsPinned { get; set; }
    }
}