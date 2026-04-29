// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using Microsoft.WindowsAPICodePack.Shell;
using PdfSharp.Pdf.Content.Objects;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace PocketDrop
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // ================================================ //
        // 1. STATE & VARIABLES
        // ================================================ //

        private static DispatcherTimer _mouseTimer;

        // Mouse Tracking & Shake
        private static ShakeDetector _shakeDetector = new ShakeDetector();
        private static bool _hasSpawnedPocketThisDrag = false;
        private static POINT? _shakeAnchor = null;
        private static bool _shakeInvalidated = false;

        // Core Data
        public ObservableRangeCollection<PocketItem> PocketedItems { get; set; } = new ObservableRangeCollection<PocketItem>();
        public bool IsGhost { get; set; } = false;
        private bool _isDraggingFromApp = false;
        private Point? startPoint = null;

        // Share Lifecycle
        private List<string> _filesToSharePaths;
        private DataTransferManager _shareManager;

        // View Mode Binding
        private string _currentViewMode = "Grid";
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

        // Thread-safe icon cache with concurrency guard
        private static System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource> _iconCache = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Threading.SemaphoreSlim _iconThrottle = new System.Threading.SemaphoreSlim(4, 4);

        // UI State Locks
        private bool _isUpdatingSelectAll = false;
        private long _lastMoreMenuCloseTime = 0;

        // Drag & Drop Reorder Tracking
        private static List<PocketItem> _internalDragPayload = null;
        private bool _internalDropHandled = false;
        private ListBoxItem _lastHoveredItem = null;
        private bool _insertAbove = false;
        private DropLineAdorner _currentAdorner = null;

        // ================================================ //
        // 2. LIFECYCLE (STARTUP & SHUTDOWN)
        // ================================================ //
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            // Start hidden — shake to reveal
            this.Opacity = 0;
            this.IsHitTestVisible = false;

            // Start the hardware polling loop
            if (_mouseTimer == null)
            {
                _mouseTimer = new DispatcherTimer(DispatcherPriority.Background);
                _mouseTimer.Interval = TimeSpan.FromMilliseconds(16);
                _mouseTimer.Tick += MouseTimer_Tick;
                _mouseTimer.Start();
            }

            // Clean up heavy temp files from previous sessions
            System.Threading.Tasks.Task.Run(() => CleanupOldShareZips());

            // Capture menu close immediately, bypassing the fade-out delay
            if (MoreButton.ContextMenu != null)
            {
                var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ContextMenu.IsOpenProperty, typeof(ContextMenu));
                dpd.AddValueChanged(MoreButton.ContextMenu, (s, e) =>
                {
                    if (MoreButton.ContextMenu.IsOpen == false)
                    {
                        _lastMoreMenuCloseTime = Environment.TickCount64;
                    }
                });
            }
        }

        // Add safe external kill switch to clear and close window
        public void ForceClose()
        {
            IsGhost = true;

            bool isLastWindow = Application.Current.Windows.OfType<MainWindow>().Count() <= 1;
            HidePocketDrop(!isLastWindow);
        }


        // ================================================ //
        // 4. CORE DRAG & DROP LOGIC
        // ================================================ //

        // Drag hover effects
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (_isDraggingFromApp) return;

            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                DragGlowBorder.Visibility = Visibility.Visible;
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            DragGlowBorder.Visibility = Visibility.Collapsed;
        }

        // Handle file drop event
        private async void Window_Drop(object sender, DragEventArgs e)
        {

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
                    int warningThreshold = 500;
                    if (droppedFiles.Length > warningThreshold)
                    {
                        string titleTemplate = (string)Application.Current.TryFindResource("Text_LargeDropTitle") ?? "Large File Drop";
                        string messageTemplate = (string)Application.Current.TryFindResource("Text_LargeDropMsg") ?? "You are about to process {0} items. Do you want to continue?";

                        var dialog = new CustomDialog(string.Format(messageTemplate, droppedFiles.Length), titleTemplate) { Owner = this };
                        dialog.ShowDialog();

                        if (dialog.Result == MessageBoxResult.No) return;
                    }

                    var validItems = new List<PocketItem>();

                    // 1. Instantly process paths with a blank icon
                    foreach (string filePath in droppedFiles)
                    {
                        if (AppHelpers.IsDuplicate(PocketedItems, filePath)) continue;
                        try { if (!File.Exists(filePath) && !Directory.Exists(filePath)) continue; } catch { continue; }

                        string finalDisplayName = AppHelpers.GetSafeDisplayName(PocketedItems, filePath);
                        validItems.Add(new PocketItem { FileName = finalDisplayName, FilePath = filePath, Icon = null });
                    }

                    if (validItems.Count == 0) return;

                    // 2. Add to UI immediately for instant response
                    PocketedItems.AddRange(validItems);

                    foreach (var newItem in validItems)
                    {
                        if (!AppGlobals.SessionHistoryPaths.Contains(newItem.FilePath)) AppGlobals.SessionHistory.Add(newItem);
                    }

                    // Refresh UI
                    if (StatusContainer != null) StatusContainer.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;
                    UpdateStackPreview();

                    if (ItemsListBox == null || ItemsListBox.SelectedItems.Count == 0)
                    {
                        UpdateItemCountDisplay(PocketedItems.Count);
                    }

                    // 3. Fire and forget: load the icon in the background
                    foreach (var item in validItems)
                    {
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            var loadedIcon = await LoadFileIconAsync(item.FilePath);
                            if (loadedIcon != null)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    item.Icon = loadedIcon; // Instantly hot-swaps the UI image

                                    // Only trigger a stack redraw if this item is in the top 13 cards
                                    if (PocketedItems.IndexOf(item) >= PocketedItems.Count - 13)
                                    {
                                        UpdateStackPreview();
                                    }
                                });
                            }
                        });
                    }
                }
            }
            // 2. Handle URL drops from web browsers
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                string droppedText = (string)e.Data.GetData(DataFormats.Text);

                if (Uri.TryCreate(droppedText, UriKind.Absolute, out Uri uriResult) &&
                   (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    try
                    {
                        string domain = uriResult.Host.Replace("www.", "");
                        string finalDomainName = AppHelpers.GetSafeDisplayName(PocketedItems, domain);

                        string tempFolder = Path.GetTempPath();
                        string fileName = $"{domain} Link_{DateTime.Now.Ticks}.url";
                        string filePath = Path.Combine(tempFolder, fileName);

                        // Write the file to disk before requesting its icon from Windows
                        File.WriteAllText(filePath, $"[InternetShortcut]\nURL={uriResult.AbsoluteUri}");

                        // Get the native WPF BitmapSource to preserve transparency
                        using (ShellObject shellObj = ShellObject.FromParsingName(filePath))
                        {
                            var transparentIcon = shellObj.Thumbnail.LargeBitmapSource;
                            transparentIcon.Freeze();

                            var safeUrlItem = new PocketItem { FileName = finalDomainName, FilePath = filePath, Icon = transparentIcon };
                            PocketedItems.Add(safeUrlItem);

                            if (!AppGlobals.SessionHistoryPaths.Contains(safeUrlItem.FilePath))
                            {
                                AppGlobals.SessionHistory.Add(safeUrlItem);
                            }
                        }

                        if (StatusContainer != null) StatusContainer.Visibility = Visibility.Collapsed;
                        FileIconContainer.Visibility = Visibility.Visible;
                        UpdateStackPreview();

                        if (ItemsListBox == null || ItemsListBox.SelectedItems.Count == 0)
                        {
                            UpdateItemCountDisplay(PocketedItems.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        ex.Data.Add("PocketDrop Context", "Could not save URL");
                        SentrySdk.CaptureException(ex);
                    }
                }
                else
                {
                    // Handle raw snippets of text
                    try
                    {
                        string tempFolder = Path.GetTempPath();

                        // Create a preview for the filename (first 20 chars)
                        string preview = droppedText.Length > 20 ? droppedText.Substring(0, 20).Trim() : droppedText;
                        preview = string.Join("_", preview.Split(Path.GetInvalidFileNameChars()));

                        string fileName = $"Snippet_{preview}_{Guid.NewGuid().ToString("N").Substring(0, 4)}.txt";
                        string filePath = Path.Combine(tempFolder, fileName);

                        File.WriteAllText(filePath, droppedText);

                        var textIcon = await LoadFileIconAsync(filePath);

                        var textItem = new PocketItem
                        {
                            FileName = fileName,
                            FilePath = filePath,
                            Icon = textIcon,
                            IsSnippet = true // Mark the item as plain text
                        };

                        PocketedItems.Add(textItem);

                        if (!AppGlobals.SessionHistoryPaths.Contains(textItem.FilePath))
                            AppGlobals.SessionHistory.Add(textItem);

                        if (StatusContainer != null) StatusContainer.Visibility = Visibility.Collapsed;
                        FileIconContainer.Visibility = Visibility.Visible;
                        UpdateStackPreview();
                        UpdateItemCountDisplay(PocketedItems.Count);
                    }
                    catch (Exception ex)
                    {
                        ex.Data.Add("PocketDrop Context", "Could not save text snippet");
                        SentrySdk.CaptureException(ex);
                    }
                }
            }
        }

        // Centralized Drag Payload Builder
        private DataObject BuildDragPayload(List<PocketItem> itemsToDrag, System.IO.MemoryStream dropEffectStream)
        {
            DataObject dragData = new DataObject();
            bool allSnippets = itemsToDrag.All(i => i.IsSnippet);

            // Provide both native and web-compatible clipboard formats
            if (allSnippets)
            {
                try
                {
                    string combinedText = string.Join(Environment.NewLine + Environment.NewLine, itemsToDrag.Select(i => File.ReadAllText(i.FilePath)));

                    dragData.SetData(DataFormats.UnicodeText, combinedText);
                    dragData.SetData(DataFormats.Text, combinedText);
                    dragData.SetData(DataFormats.StringFormat, combinedText);

                    dragData.SetData("text/plain", combinedText);
                }
                catch { }
            }
            else
            {
                // Use FileDrop for mixed items or physical files
                string[] paths = itemsToDrag.Select(i => i.FilePath).ToArray();
                dragData.SetData(DataFormats.FileDrop, paths);

                // Fallback: also attach text content if the item is a single .txt file
                if (paths.Length == 1 && Path.GetExtension(paths[0]).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var fi = new FileInfo(paths[0]);
                        if (fi.Length < 1024 * 1024)
                        {
                            dragData.SetText(File.ReadAllText(paths[0]));
                        }
                    }
                    catch { }
                }

                dragData.SetData("Preferred DropEffect", dropEffectStream);
            }

            return dragData;
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

                    int warningThreshold = 500;
                    if (fileArray.Length > warningThreshold)
                    {
                        string titleTemplate = (string)Application.Current.TryFindResource("Text_LargePasteTitle") ?? "Large Paste Operation";
                        string messageTemplate = (string)Application.Current.TryFindResource("Text_LargePasteMsg") ?? "You are about to paste {0} items.\n\nThis may take a moment to load. Do you want to continue?";

                        string finalMessage = string.Format(messageTemplate, fileArray.Length);

                        var dialog = new CustomDialog(finalMessage, titleTemplate);
                        dialog.Owner = this;
                        dialog.ShowDialog();

                        if (dialog.Result == MessageBoxResult.No) return;
                    }

                    // Process all files
                    var processingTasks = fileArray.Select(async filePath =>
                    {
                        string fileName = Path.GetFileName(filePath);
                        System.Windows.Media.ImageSource fileIcon = await LoadFileIconAsync(filePath);
                        return new PocketItem { FileName = fileName, FilePath = filePath, Icon = fileIcon };
                    });

                    // Wait for the Bouncer to process them all
                    var processedItems = await System.Threading.Tasks.Task.WhenAll(processingTasks);

                    // 1. Sync to the background global history
                    foreach (var newItem in processedItems)
                    {
                        if (!AppGlobals.SessionHistoryPaths.Contains(newItem.FilePath))
                        {
                            AppGlobals.SessionHistory.Add(newItem);
                        }
                    }

                    PocketedItems.AddRange(processedItems);

                    if (StatusContainer != null) StatusContainer.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;
                    UpdateStackPreview();

                    UpdateItemCountDisplay(PocketedItems.Count);

                    // Notify My Pockets window to refresh in real-time
                    AppGlobals.TriggerHistoryRefresh();
                }
                // Catch Text and URLs from the clipboard
                else if (System.Windows.Clipboard.ContainsText())
                {
                    string pastedText = System.Windows.Clipboard.GetText();

                    // 1. Check if the item is a web URL
                    if (Uri.TryCreate(pastedText, UriKind.Absolute, out Uri uriResult) &&
                       (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                    {
                        string domain = uriResult.Host.Replace("www.", "");
                        string finalDomainName = AppHelpers.GetSafeDisplayName(PocketedItems, domain);

                        string tempFolder = Path.GetTempPath();
                        string fileName = $"{domain} Link_{DateTime.Now.Ticks}.url";
                        string filePath = Path.Combine(tempFolder, fileName);

                        File.WriteAllText(filePath, $"[InternetShortcut]\nURL={uriResult.AbsoluteUri}");

                        using (ShellObject shellObj = ShellObject.FromParsingName(filePath))
                        {
                            var transparentIcon = shellObj.Thumbnail.LargeBitmapSource;
                            transparentIcon.Freeze();

                            var safeUrlItem = new PocketItem { FileName = finalDomainName, FilePath = filePath, Icon = transparentIcon };
                            PocketedItems.Add(safeUrlItem);

                            if (!AppGlobals.SessionHistoryPaths.Contains(safeUrlItem.FilePath))
                                AppGlobals.SessionHistory.Add(safeUrlItem);
                        }
                    }
                    // 2. Standard text snippet
                    else
                    {
                        string tempFolder = Path.GetTempPath();
                        string preview = pastedText.Length > 20 ? pastedText.Substring(0, 20).Trim() : pastedText;
                        preview = string.Join("_", preview.Split(Path.GetInvalidFileNameChars()));

                        string fileName = $"Snippet_{preview}_{Guid.NewGuid().ToString("N").Substring(0, 4)}.txt";
                        string filePath = Path.Combine(tempFolder, fileName);

                        File.WriteAllText(filePath, pastedText);
                        var textIcon = await LoadFileIconAsync(filePath);

                        var textItem = new PocketItem
                        {
                            FileName = fileName,
                            FilePath = filePath,
                            Icon = textIcon,
                            IsSnippet = true
                        };

                        PocketedItems.Add(textItem);

                        if (!AppGlobals.SessionHistoryPaths.Contains(textItem.FilePath))
                            AppGlobals.SessionHistory.Add(textItem);
                    }

                    // Refresh UI
                    if (StatusContainer != null) StatusContainer.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;
                    UpdateStackPreview();
                    UpdateItemCountDisplay(PocketedItems.Count);

                    // Notify My Pockets window to refresh in real-time
                    AppGlobals.TriggerHistoryRefresh();
                }
                else
                {
                    // Show warning when clipboard has no files or text
                    string emptyDesc = (string)Application.Current.Resources["Text_ClipboardEmpty"];
                    string emptyTitle = (string)Application.Current.Resources["Text_ClipboardEmptyTitle"];
                    MessageBox.Show(emptyDesc, emptyTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ex.Data.Add("PocketDrop Context", "Clipboard error");
                SentrySdk.CaptureException(ex);
            }
        }

        // Track click to prepare file drag-out
        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _hasSpawnedPocketThisDrag = true; // Tell the Global Mouse Hook to abort shake detection

            Point pos = e.GetPosition(this); // Get exact mouse coordinates relative to the window

            // 1. More button logic
            if (IsPointOver(MoreButton, pos))
            {
                // 1. Close expand Detail view if it's open
                if (ExpandButton != null && ExpandButton.IsChecked == true)
                {
                    ExpandButton.IsChecked = false;
                }

                // 2. If the menu closed less than 200 milliseconds ago, ignore this click
                if (Environment.TickCount64 - _lastMoreMenuCloseTime < 200)
                {
                    e.Handled = true;
                    return;
                }

                // 3. Manually toggle the More Menu
                if (MoreButton.ContextMenu != null)
                {
                    if (MoreButton.ContextMenu.IsOpen)
                    {
                        MoreButton.ContextMenu.IsOpen = false;
                    }
                    else
                    {
                        MoreButton_Click(MoreButton, new RoutedEventArgs());
                    }
                }

                e.Handled = true;
                return;
            }

            // 2. Expand Detail view logic
            if (IsPointOver(ExpandButton, pos))
            {
                // If the Expand popup is already open, close it and swallow the click
                if (ExpandButton.IsChecked == true)
                {
                    ExpandButton.IsChecked = false;
                    e.Handled = true;
                    return;
                }

                return; // Abort drag preparation
            }

            // 3. Other UI elements
            if (IsPointOver(CloseButton, pos) ||
                IsPointOver(TopBar, pos) ||
                IsPointOver(DragHandle, pos))
            {
                return; // Abort drag preparation
            }

            // 4. Fallback: Check the visual tree for the Delete button (inside the popup)
            DependencyObject hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                // 1. Ignore Delete, Select All Checkbox, and Scrollbars
                if (hit == DeleteSelectedButton || hit == SelectAllCheckBox || hit is System.Windows.Controls.Primitives.ScrollBar)
                    return;

                // 2. Ignore the List and Grid buttons to prevent memory spikes
                if (hit is Border border && (border.Tag?.ToString() == "List" || border.Tag?.ToString() == "Grid"))
                    return;

                hit = (hit is Visual || hit is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(hit)
                    : LogicalTreeHelper.GetParent(hit);
            }

            // If all checks pass, the click is on empty space or a file — safe to drag
            startPoint = pos;
        }

        // Helper to check if the mouse is within a UI element's bounding box
        private bool IsPointOver(FrameworkElement element, Point windowPos)
        {
            if (element == null || !element.IsVisible) return false;

            try
            {
                // Calculate the exact mathematical bounding box of the element on the window
                GeneralTransform transform = element.TransformToAncestor(this);
                Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                return bounds.Contains(windowPos);
            }
            catch
            {
                return false; // Safely fail if the element is inside a Popup
            }
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
                startPoint = null;
                return;
            }

            Point mousePos = e.GetPosition(null);
            Vector diff = (Point)startPoint - mousePos;

            // Only start if the mouse has moved significantly (Drag threshold)
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var itemsToDrag = PocketedItems.ToList();

                // Match standard Windows drag behavior!
                bool isShiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                bool isCtrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

                // Start with the user's default setting
                bool isCopy = AppGlobals.CopyItemToDestination;

                // Apply Windows overrides
                if (isShiftDown) isCopy = false;      // Shift forces a Move
                else if (isCtrlDown) isCopy = true;   // Ctrl forces a Copy

                using var dropEffectStream = new System.IO.MemoryStream(new byte[] { (byte)(AppGlobals.CopyItemToDestination ? 1 : 2), 0, 0, 0 });
                DataObject dragData = BuildDragPayload(itemsToDrag, dropEffectStream);

                Point tempStart = (Point)startPoint;
                startPoint = null;

                _isDraggingFromApp = true;

                DragDropEffects result = DragDrop.DoDragDrop((DependencyObject)sender, dragData, DragDropEffects.All);
                _isDraggingFromApp = false;

                // Detect if the drop target is a browser
                bool forceClear = false;
                if (result == DragDropEffects.None && Mouse.LeftButton == MouseButtonState.Released)
                {
                    if (itemsToDrag.All(i => i.IsSnippet || i.FileName.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
                    {
                        forceClear = true;
                    }
                }

                // Clear the Pocket after a successful drop or a forced web clear
                if (result != DragDropEffects.None || forceClear)
                {
                    foreach (var item in PocketedItems)
                    {
                        CleanupTempFile(item.FilePath);

                        if (!AppGlobals.CopyItemToDestination && item.OriginalFilePath != item.FilePath)
                        {
                            try { if (File.Exists(item.OriginalFilePath)) File.Delete(item.OriginalFilePath); } catch { }
                        }
                    }

                    PocketedItems.Clear();

                    if (AppGlobals.CloseWhenEmptied)
                    {
                        ExpandButton.IsChecked = false;
                        ForceClose();
                    }
                    else
                    {
                        StackContainer.Children.Clear();
                        ExpandButton.IsChecked = false;
                        UpdateItemCountDisplay(0);

                        if (SelectAllCheckBox != null) SelectAllCheckBox.IsChecked = false;
                    }
                }
            }

            // Notify My Pockets window to refresh in real-time
            var openHistoryWindow = Application.Current.Windows.OfType<MyPocketsWindow>().FirstOrDefault();
            if (openHistoryWindow != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    openHistoryWindow.RefreshHistory();
                }));
            }
        }

        private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (CheckForMissingFiles()) return;

            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && !(hit is ListBoxItem) && !(hit is ListBox))
                hit = VisualTreeHelper.GetParent(hit);

            if (!(hit is ListBoxItem lbi)) return;

            var draggedItem = lbi.DataContext as PocketItem;
            if (draggedItem == null) return;

            if (!ItemsListBox.SelectedItems.Contains(draggedItem))
            {
                ItemsListBox.SelectedItems.Add(draggedItem);
            }

            var selectedItems = ItemsListBox.SelectedItems.Cast<PocketItem>().ToList();
            if (selectedItems.Count == 0) return;

            // Match standard Windows drag behavior
            bool isShiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool isCtrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            // Start with the user's default setting
            bool isCopy = AppGlobals.CopyItemToDestination;

            // Apply Windows overrides
            if (isShiftDown) isCopy = false;      // Shift forces a Move
            else if (isCtrlDown) isCopy = true;   // Ctrl forces a Copy

            using var dropEffectStream = new System.IO.MemoryStream(new byte[] { (byte)(AppGlobals.CopyItemToDestination ? 1 : 2), 0, 0, 0 });
            DataObject dragData = BuildDragPayload(selectedItems, dropEffectStream);

            _internalDragPayload = selectedItems.ToList();
            _internalDropHandled = false;
            _isDraggingFromApp = true;

            DragDropEffects result = DragDrop.DoDragDrop(ItemsListBox, dragData, DragDropEffects.All);

            _isDraggingFromApp = false;

            // Detect if the drop target is a browser
            bool forceClear = false;
            if (result == DragDropEffects.None && Mouse.LeftButton == MouseButtonState.Released)
            {
                if (selectedItems.All(i => i.IsSnippet || i.FileName.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
                {
                    forceClear = true;
                }
            }

            // Proceed if the drop was valid or forced by a web clear
            if (!_internalDropHandled && (result != DragDropEffects.None || forceClear))
            {
                foreach (var item in selectedItems)
                {
                    CleanupTempFile(item.FilePath);

                    if (!AppGlobals.CopyItemToDestination && item.OriginalFilePath != item.FilePath)
                    {
                        try { if (File.Exists(item.OriginalFilePath)) File.Delete(item.OriginalFilePath); } catch { }
                    }

                    PocketedItems.Remove(item);
                }

                if (PocketedItems.Count == 0)
                {
                    if (AppGlobals.CloseWhenEmptied)
                    {
                        ExpandButton.IsChecked = false;
                        ForceClose();
                    }
                    else
                    {
                        if (StatusContainer != null)
                        {
                            StatusContainer.Visibility = Visibility.Visible;
                            StatusProgressBar.Visibility = Visibility.Collapsed;
                        }
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

            _internalDragPayload = null;
        }


        // Dragging the window
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent window drag when clicking the close button
            if (e.OriginalSource == CloseButton || e.Source == MoreButton)
                return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // 1. Animate the handle expanding
                var expandAnim = new DoubleAnimation(50, TimeSpan.FromMilliseconds(100)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var opacityAnim = new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(100));
                DragHandle.BeginAnimation(WidthProperty, expandAnim);
                DragHandle.BeginAnimation(OpacityProperty, opacityAnim);

                AppHelpers.ReleaseCapture();
                var hwnd = new WindowInteropHelper(this).Handle;

                AppHelpers.SendMessage(hwnd, WM_NCLBUTTONDOWN, HT_CAPTION, 0);

                // 2. Animate the handle back to 40px on window drop
                var shrinkAnim = new DoubleAnimation(30, TimeSpan.FromMilliseconds(100)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var fadeAnim = new DoubleAnimation(0.5, TimeSpan.FromMilliseconds(100));
                DragHandle.BeginAnimation(WidthProperty, shrinkAnim);
                DragHandle.BeginAnimation(OpacityProperty, fadeAnim);
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
            this.CurrentViewMode = AppGlobals.ItemsLayoutMode == 1 ? "List" : "Grid";

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
                AppGlobals.PocketPlacement,
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
                foreach (var item in PocketedItems)
                {
                    CleanupTempFile(item.FilePath);
                }

                PocketedItems.Clear();
                StackContainer.Children.Clear();
                UpdateItemCountDisplay(0);

                if (SelectAllCheckBox != null) SelectAllCheckBox.IsChecked = false;

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

            double[] angles = { -11, 8, -7, 6, -5, 4, -4, 3, -3, 2, -2, 1, -1 };
            double[] offsetsX = { -7, 6, -5, 4, -4, 3, -3, 2, -2, 1, -1, 1, 0 };
            double[] offsetsY = { 5, 4, 4, 3, 3, 2, 2, 2, 1, 1, 1, 0, 0 };

            // Only draw the top 13 cards
            int maxCardsToShow = angles.Length;
            int startIndex = Math.Max(0, count - maxCardsToShow);

            for (int i = startIndex; i < count; i++)
            {
                int patternIndex = count - 1 - i;
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
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);

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
                startPoint = null;
            }
        }

        // Select-all logic
        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectAll) return;

            if (ItemsListBox != null)
                ItemsListBox.SelectAll();
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectAll) return;

            if (ItemsListBox != null)
                ItemsListBox.UnselectAll();
        }

        // Update header text on selection change
        private void ItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int selectedCount = ItemsListBox.SelectedItems.Count;
            int totalCount = PocketedItems.Count;

            if (SelectAllCheckBox != null)
            {
                _isUpdatingSelectAll = true;

                if (selectedCount == 0 || selectedCount < totalCount)
                {
                    SelectAllCheckBox.IsChecked = false; // Uncheck when not all items are selected
                }
                else if (selectedCount == totalCount && totalCount > 0)
                {
                    SelectAllCheckBox.IsChecked = true; // Check if all items are manually selected
                }

                _isUpdatingSelectAll = false;
            }
            
            if (selectedCount > 0)
            {
                long totalBytes = 0;
                foreach (PocketItem item in ItemsListBox.SelectedItems)
                {
                    if (File.Exists(item.FilePath))
                        totalBytes += new FileInfo(item.FilePath).Length;
                }

                if (PopupCountNumberText != null) PopupCountNumberText.Text = selectedCount.ToString();
                if (PopupCountSizeText != null) PopupCountSizeText.Text = $"({AppHelpers.FormatBytes(totalBytes)})";

                string resourceKey = selectedCount == 1 ? "Text_FileSelected" : "Text_FilesSelected";
                if (PopupCountLabelText != null) PopupCountLabelText.SetResourceReference(TextBlock.TextProperty, resourceKey);
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
                CleanupTempFile(item.FilePath);
                AppGlobals.SessionHistory.Remove(item);
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

            UpdateItemCountDisplay(PocketedItems.Count);
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

                // 2. Dynamically link to the translation resources
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

            // 1. Collect selected files, or all files if none are selected
            var itemsToOpen = (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
                ? ItemsListBox.SelectedItems.Cast<PocketItem>().ToList()
                : PocketedItems.ToList();

            if (itemsToOpen.Count == 0) return;

            // 2. Scenario A: Handle folders and files differently for single selections
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
                        ex.Data.Add("PocketDrop Context", "Could not open folder");
                        SentrySdk.CaptureException(ex);
                    }
                }
                // Open the native open-with dialog for file selection
                else if (!string.IsNullOrEmpty(targetFilePath) && File.Exists(targetFilePath))
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            AppHelpers.OPENASINFO info = new AppHelpers.OPENASINFO();
                            info.pcszFile = targetFilePath;
                            info.pcszClass = null;
                            info.oaUIAction = 7;
                            AppHelpers.SHOpenWithDialog(IntPtr.Zero, ref info);
                        }
                        catch (Exception ex)
                        {
                            ex.Data.Add("PocketDrop Context", "Could not open file picker");
                            SentrySdk.CaptureException(ex);
                        }
                    });
                }
            }
            // 3. Scenario B: Open all items when multiple are selected
            else
            {
                foreach (var item in itemsToOpen)
                {
                    // Verify both the directory and file exist before opening
                    if (Directory.Exists(item.FilePath) || File.Exists(item.FilePath))
                    {
                        try
                        {
                            // Use ShellExecute to open items
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = item.FilePath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            ex.Data.Add("PocketDrop Context", $"Could not open {item.FileName}");
                            SentrySdk.CaptureException(ex);
                        }
                    }
                }
            }

            // 4. Close the Pocket after opening if the setting is enabled
            if (AppGlobals.CloseWhenOpenWith)
            {
                if (ExpandButton != null) ExpandButton.IsChecked = false; 
                ForceClose(); 
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
            if (ExpandButton != null) ExpandButton.IsChecked = false;

            bool containsFolders = itemsToShare.Any(item => Directory.Exists(item.FilePath));

            // 1. Handle folders and zipping first
            if (containsFolders)
            {
                if (AppGlobals.AutoCompressFoldersShare)
                {
                    try
                    {
                        // Show loading UI
                        if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
                        if (StatusContainer != null)
                        {
                            StatusText.Text = (string)Application.Current.TryFindResource("Text_CompressingShare") ?? "Compressing...";
                            StatusProgressBar.Visibility = Visibility.Visible;
                            StatusProgressBar.IsIndeterminate = false;
                            StatusProgressBar.Maximum = itemsToShare.Count;
                            StatusProgressBar.Value = 0;
                            StatusContainer.Visibility = Visibility.Visible;
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
                        if (StatusContainer != null)
                        {
                            StatusContainer.Visibility = Visibility.Collapsed;
                            StatusProgressBar.Visibility = Visibility.Collapsed;
                        }
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
                // Use standard file paths if no folders are in the selection
                _filesToSharePaths = itemsToShare.Select(item => item.FilePath).ToList();
            }

            // 2. Trigger the share UI
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                var factory = WinRT.ActivationFactory.Get("Windows.ApplicationModel.DataTransfer.DataTransferManager");
                var interop = (AppHelpers.IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);

                Guid guid = Guid.Parse("a5caee9b-8708-49d1-8d36-67d25a8da00c");
                IntPtr ptr = interop.GetForWindow(hwnd, ref guid);
                _shareManager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);

                // Hook the event and show the UI
                _shareManager.DataRequested -= ShareManager_DataRequested;
                _shareManager.DataRequested += ShareManager_DataRequested;
                interop.ShowShareUIForWindow(hwnd);

                // Ensure ZIP finishes and share UI opens before closing pocket
                if (AppGlobals.CloseWhenShare)
                {
                    ForceClose();
                }
            }
            catch (Exception ex)
            {
                ex.Data.Add("PocketDrop Context", "Share error");
                SentrySdk.CaptureException(ex);
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
                if (ExpandButton != null) ExpandButton.IsChecked = false;

                if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
                if (StatusContainer != null)
                {
                    StatusText.Text = (string)Application.Current.TryFindResource("Text_CompressingShare") ?? "Compressing files...";
                    StatusProgressBar.Visibility = Visibility.Visible;
                    StatusProgressBar.IsIndeterminate = false;
                    StatusProgressBar.Maximum = itemsToCompress.Count;
                    StatusProgressBar.Value = 0;
                    StatusContainer.Visibility = Visibility.Visible;
                }

                // 3. Compress in the background
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // Delete existing ZIP before creating new one
                        if (File.Exists(zipPath)) File.Delete(zipPath);

                        int processedCount = 0;

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

                                // Update the progress bar
                                int currentProgress = System.Threading.Interlocked.Increment(ref processedCount);
                                Application.Current.Dispatcher.Invoke(() => StatusProgressBar.Value = currentProgress);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ex.Data.Add("PocketDrop Context", "Could not create ZIP");
                        SentrySdk.CaptureException(ex);
                    }
                });

                // Restore the UI if the Pocket stays open after compression
                if (!AppGlobals.CloseWhenCompress)
                {
                    if (StatusContainer != null)
                    {
                        StatusContainer.Visibility = Visibility.Collapsed;
                        StatusProgressBar.Visibility = Visibility.Collapsed;
                    }
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

                    if (!windowFound)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
                    }
                }
                catch (Exception ex)
                {
                    ex.Data.Add("PocketDrop Context", "Highlight error");
                    SentrySdk.CaptureException(ex);
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
                }
            }
            // Close the Pocket after compress if the setting is enabled
            if (AppGlobals.CloseWhenCompress)
            {
                if (ExpandButton != null) ExpandButton.IsChecked = false;
                ForceClose();
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

            if (SelectAllCheckBox != null)
                SelectAllCheckBox.IsChecked = false;

        }

        // Menu action: Image - Remove Metadata
        private async void Menu_ImageRemoveMetadata_Click(object sender, RoutedEventArgs e)
        {
            var imagesToProcess = GetOnlyValidImages();
            if (imagesToProcess.Count == 0) return;

            // 1. Show loading status
            if (ExpandButton != null) ExpandButton.IsChecked = false;
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
            if (StatusContainer != null)
            {
                StatusText.Text = (string)Application.Current.TryFindResource("Text_StrippingMetadata") ?? "Cleaning images...";
                StatusProgressBar.Visibility = Visibility.Visible;
                StatusProgressBar.IsIndeterminate = false;
                StatusProgressBar.Maximum = imagesToProcess.Count;
                StatusProgressBar.Value = 0;
                StatusContainer.Visibility = Visibility.Visible;
            }

            int successCount = 0;
            int processedCount = 0;

            // 2. Run on a background thread so the UI doesn't freeze
            await System.Threading.Tasks.Task.Run(() =>
            {
                string tempFolder = Path.GetTempPath();

                foreach (var img in imagesToProcess)
                {
                    try
                    {
                        string newPath = ImageProcessor.StripMetadata(img.FilePath, tempFolder);
                        if (newPath == null) continue;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            img.FilePath = newPath;
                            img.FileName = Path.GetFileName(newPath);
                        });

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        ex.Data.Add("PocketDrop Context", $"Could not strip EXIF from {img.FileName}");
                        SentrySdk.CaptureException(ex);
                    }
                    finally
                    {
                        int currentProgress = System.Threading.Interlocked.Increment(ref processedCount);
                        Application.Current.Dispatcher.Invoke(() => StatusProgressBar.Value = currentProgress);
                    }
                }
            });

            // 3. Restore UI
            if (StatusContainer != null)
            {
                StatusContainer.Visibility = Visibility.Collapsed;
                StatusProgressBar.Visibility = Visibility.Collapsed;
            }
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Visible;

            // 4. Update the success message
            string title = (string)Application.Current.TryFindResource("Text_MetadataSuccessTitle") ?? "Success";
            string msgTemplate = (string)Application.Current.TryFindResource("Text_MetadataSuccessMsg") ?? "Successfully cleaned {0} images!\n\nThey are now ready in your Pocket. Simply drag them out to save them.";

            MessageBox.Show(string.Format(msgTemplate, successCount), title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Menu action: Image - Convert Format
        private async void Menu_ImageConvertAction_Click(object sender, RoutedEventArgs e)
        {
            var imagesToProcess = GetOnlyValidImages();
            if (imagesToProcess.Count == 0) return;

            MenuItem clickedItem = sender as MenuItem;
            string targetExt = clickedItem?.Tag?.ToString();
            if (string.IsNullOrEmpty(targetExt)) return;

            string extName = targetExt.ToUpper().Replace(".", "");

            if (ExpandButton != null) ExpandButton.IsChecked = false;
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
            if (StatusContainer != null)
            {
                string statusTemplate = (string)Application.Current.TryFindResource("Text_ConvertingFormat") ?? "Converting to {0}...";
                StatusText.Text = string.Format(statusTemplate, extName);

                // Set up the progress bar for real-time tracking
                StatusProgressBar.Visibility = Visibility.Visible;
                StatusProgressBar.IsIndeterminate = false;
                StatusProgressBar.Maximum = imagesToProcess.Count;
                StatusProgressBar.Value = 0;

                StatusContainer.Visibility = Visibility.Visible;
            }

            int successCount = 0;
            int processedCount = 0;

            // Processing in parallel
            await System.Threading.Tasks.Task.Run(() =>
            {
                // Register PDFsharp font resolver once for proper font handling in .NET
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                string tempFolder = System.IO.Path.GetTempPath();

                System.Threading.Tasks.Parallel.ForEach(imagesToProcess, img =>
                {
                    try
                    {
                        string newPath = ImageProcessor.ConvertFormat(img.FilePath, targetExt, tempFolder);
                        if (newPath == null) return; // Skip conversion if already in the correct format
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            img.FilePath = newPath;
                            img.FileName = System.IO.Path.GetFileName(newPath);
                            img.Icon = null;
                        });
                        System.Threading.Interlocked.Increment(ref successCount);
                    }
                    catch (System.Exception ex)
                    {
                        ex.Data.Add("PocketDrop Context", $"Could not convert {img.FileName}");
                        SentrySdk.CaptureException(ex);
                    }
                    finally
                    {
                        // Update the progress bar safely on the UI thread after every single image
                        int currentProgress = System.Threading.Interlocked.Increment(ref processedCount);
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            StatusProgressBar.Value = currentProgress;
                        });
                    }
                });
            });

            foreach (var item in imagesToProcess)
            {
                if (item.Icon == null)
                {
                    _ = LoadFileIconAsync(item.FilePath).ContinueWith(t =>
                    {
                        if (t.Result != null)
                        {
                            Application.Current.Dispatcher.Invoke(() => item.Icon = t.Result);
                        }
                    });
                }
            }

            if (StatusContainer != null)
            {
                StatusContainer.Visibility = Visibility.Collapsed;
                StatusProgressBar.Visibility = Visibility.Collapsed;
            }
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Visible;

            if (successCount > 0)
            {
                string title = (string)Application.Current.TryFindResource("Text_FormatSuccessTitle") ?? "Success";
                string msgTemplate = (string)Application.Current.TryFindResource("Text_FormatSuccessMsg") ?? "Successfully converted {0} images to {1}!\n\nThey are ready in your Pocket.";

                MessageBox.Show(string.Format(msgTemplate, successCount, extName), title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Menu action: Image - Rotate
        private async void Menu_ImageRotateAction_Click(object sender, RoutedEventArgs e)
        {
            var imagesToProcess = GetOnlyValidImages();
            if (imagesToProcess.Count == 0) return;

            MenuItem clickedItem = sender as MenuItem;
            if (!int.TryParse(clickedItem?.Tag?.ToString(), out int userDegrees)) return;

            if (ExpandButton != null) ExpandButton.IsChecked = false;
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
            if (StatusContainer != null)
            {
                StatusText.Text = (string)Application.Current.TryFindResource("Text_RotatingImages") ?? "Rotating images...";
                StatusProgressBar.Visibility = Visibility.Visible;
                StatusProgressBar.IsIndeterminate = false;
                StatusProgressBar.Maximum = imagesToProcess.Count;
                StatusProgressBar.Value = 0;
                StatusContainer.Visibility = Visibility.Visible;
            }

            string rotatedSuffix = (string)Application.Current.TryFindResource("Text_RotatedSuffix") ?? "_Rotated";
            int successCount = 0;
            int processedCount = 0;

            // Processing in parallel
            await System.Threading.Tasks.Task.Run(() =>
            {
                string tempFolder = System.IO.Path.GetTempPath();

                System.Threading.Tasks.Parallel.ForEach(imagesToProcess, img =>
                {
                    try
                    {
                        string newPath = ImageProcessor.RotateImage(img.FilePath, userDegrees, rotatedSuffix, tempFolder);
                        if (newPath == null) return;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            img.FilePath = newPath;
                            img.FileName = System.IO.Path.GetFileName(newPath);
                            img.Icon = null;
                        });
                        System.Threading.Interlocked.Increment(ref successCount);
                    }
                    catch (System.Exception ex)
                    {
                        ex.Data.Add("PocketDrop Context", $"Could not rotate {img.FileName}");
                        SentrySdk.CaptureException(ex);
                    }
                    finally
                    {
                        int currentProgress = System.Threading.Interlocked.Increment(ref processedCount);
                        Application.Current.Dispatcher.InvokeAsync(() => StatusProgressBar.Value = currentProgress);
                    }
                });
            });

            foreach (var item in imagesToProcess)
            {
                if (item.Icon == null)
                {
                    _ = LoadFileIconAsync(item.FilePath).ContinueWith(t =>
                    {
                        if (t.Result != null)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                item.Icon = t.Result;
                                UpdateStackPreview();
                            });
                        }
                    });
                }
            }

            if (StatusContainer != null)
            {
                StatusContainer.Visibility = Visibility.Collapsed;
                StatusProgressBar.Visibility = Visibility.Collapsed;
            }
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Visible;

            if (successCount > 0)
            {
                string title = (string)Application.Current.TryFindResource("Text_RotateSuccessTitle") ?? "Success";
                string msgTemplate = (string)Application.Current.TryFindResource("Text_RotateSuccessMsg") ?? "Successfully rotated {0} images!\n\nThey are ready in your Pocket.";

                MessageBox.Show(string.Format(msgTemplate, successCount), title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Menu action: Image - Resize
        private async void Menu_ImageResize_Click(object sender, RoutedEventArgs e)
        {
            var imagesToProcess = GetOnlyValidImages();
            if (imagesToProcess.Count == 0) return;

            // 1. Pop the custom input window
            var resizeDialog = new ResizeWindow { Owner = this };
            if (resizeDialog.ShowDialog() != true) return;

            double inputW = resizeDialog.TargetWidth;
            double inputH = resizeDialog.TargetHeight;
            ImageResizeMode mode = resizeDialog.SelectedMode;
            ImageResizeUnit unit = resizeDialog.SelectedUnit;

            // 2. Prepare the UI
            if (ExpandButton != null) ExpandButton.IsChecked = false;
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
            if (StatusContainer != null)
            {
                StatusText.Text = (string)Application.Current.TryFindResource("Text_ResizingImages") ?? "Resizing images...";
                StatusProgressBar.Visibility = Visibility.Visible;
                StatusProgressBar.IsIndeterminate = false;
                StatusProgressBar.Maximum = imagesToProcess.Count;
                StatusProgressBar.Value = 0;
                StatusContainer.Visibility = Visibility.Visible;
            }

            string resizedSuffix = (string)Application.Current.TryFindResource("Text_ResizedSuffix") ?? "_Resized";

            int successCount = 0;
            int processedCount = 0;

            // 3. Process the images in parallel
            await System.Threading.Tasks.Task.Run(() =>
            {
                string tempFolder = System.IO.Path.GetTempPath();
                double standardDpi = 96.0; // Standard screen DPI for CM conversion

                // Parallel.ForEach spins up multiple threads instantly
                System.Threading.Tasks.Parallel.ForEach(imagesToProcess, img =>
                {
                    try
                    {
                        string newPath = ImageProcessor.ResizeImage(img.FilePath, inputW, inputH, mode, unit, resizedSuffix, tempFolder);
                        if (newPath == null) return;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            img.FilePath = newPath;
                            img.FileName = System.IO.Path.GetFileName(newPath);
                            img.Icon = null;
                        });
                        System.Threading.Interlocked.Increment(ref successCount);
                    }
                    catch (System.Exception ex)
                    {
                        ex.Data.Add("PocketDrop Context", $"Could not resize {img.FileName}");
                        SentrySdk.CaptureException(ex);
                    }
                    finally
                    {
                        int currentProgress = System.Threading.Interlocked.Increment(ref processedCount);
                        Application.Current.Dispatcher.InvokeAsync(() => StatusProgressBar.Value = currentProgress);
                    }
                });
            });

            // 4. Force the UI to refresh the icons that were just cleared
            foreach (var item in imagesToProcess)
            {
                if (item.Icon == null)
                {
                    _ = LoadFileIconAsync(item.FilePath).ContinueWith(t =>
                    {
                        if (t.Result != null) Application.Current.Dispatcher.Invoke(() => item.Icon = t.Result);
                    });
                }
            }

            if (StatusContainer != null)
            {
                StatusContainer.Visibility = Visibility.Collapsed;
                StatusProgressBar.Visibility = Visibility.Collapsed;
            }
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Visible;

            if (successCount > 0)
            {
                string title = (string)Application.Current.TryFindResource("Text_ResizeSuccessTitle") ?? "Success";
                string msgTemplate = (string)Application.Current.TryFindResource("Text_ResizeSuccessMsg") ?? "Successfully resized {0} images!\n\nThey are ready in your Pocket.";

                MessageBox.Show(string.Format(msgTemplate, successCount), title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Menu action: Image - Create PDF
        private async void Menu_ImageCreatePdf_Click(object sender, RoutedEventArgs e)
        {
            var imagesToProcess = GetOnlyValidImages();
            if (imagesToProcess.Count == 0) return;

            if (ExpandButton != null) ExpandButton.IsChecked = false;
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Collapsed;
            if (StatusContainer != null)
            {
                StatusText.Text = (string)Application.Current.TryFindResource("Text_CreatingPdf") ?? "Creating PDF document...";
                StatusProgressBar.Visibility = Visibility.Visible;
                StatusProgressBar.IsIndeterminate = true; // Slides back and forth
                StatusContainer.Visibility = Visibility.Visible;
            }

            string baseFileName = (string)Application.Current.TryFindResource("Text_DefaultPdfName") ?? "MergedDocument";

            string tempFolder = Path.GetTempPath();
            string targetFilePath = Path.Combine(tempFolder, $"PocketDrop_Doc_{Guid.NewGuid().ToString("N").Substring(0, 8)}_{baseFileName}.pdf");
            bool success = false;

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var imagePaths = imagesToProcess.Select(img => img.FilePath).ToList();
                    ImageProcessor.CreatePdfFromImages(imagePaths, targetFilePath);
                    success = true;
                }
                catch (Exception ex)
                {
                    ex.Data.Add("PocketDrop Context", "Could not create PDF");
                    SentrySdk.CaptureException(ex);
                }
            });

            if (success)
            {
                var newPdfItem = new PocketItem
                {
                    FileName = $"{baseFileName}.pdf",
                    FilePath = targetFilePath,
                    IsPinned = false
                };

                PocketedItems.Add(newPdfItem);
                // Push the newly generated PDF to the global history list
                if (!AppGlobals.SessionHistoryPaths.Contains(newPdfItem.FilePath))
                {
                    AppGlobals.SessionHistory.Add(newPdfItem);
                }

                _ = LoadFileIconAsync(newPdfItem.FilePath).ContinueWith(t =>
                {
                    if (t.Result != null) Application.Current.Dispatcher.Invoke(() => newPdfItem.Icon = t.Result);
                });

                UpdateStackPreview();
                UpdateItemCountDisplay(PocketedItems.Count);

                string title = (string)Application.Current.TryFindResource("Text_PdfSuccessTitle") ?? "Success";
                string msgTemplate = (string)Application.Current.TryFindResource("Text_PdfSuccessMsg") ?? "Successfully created a {0}-page PDF document!\n\nIt has been added to your Pocket.";

                MessageBox.Show(string.Format(msgTemplate, imagesToProcess.Count), title, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            if (StatusContainer != null)
            {
                StatusContainer.Visibility = Visibility.Collapsed;
                StatusProgressBar.Visibility = Visibility.Collapsed;
            }
            if (FileIconContainer != null) FileIconContainer.Visibility = Visibility.Visible;
        }

        // Menu action: Settings
        private void Menu_Settings_Click(object sender, RoutedEventArgs e)
        {
            // Prevent duplicate settings window from opening
            var existingSettings = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

            if (existingSettings != null)
            {
                existingSettings.Activate();
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

            // 2. Plan A: Spawn below the app
            double yBelow = targetSize.Height + 0;

            // 3. Plan B: Spawn above the app - Increase bottom margin to clear invisible border and drop shadow
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

            // 2. Scroll back to the very top for the next time it opens
            var scrollViewer = GetScrollViewer(ItemsListBox);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToTop();
            }

            // 3. Uncheck toggle button when popup closed by clicking outside
            ExpandButton.IsChecked = false;
        }

        // Closing the window — clears items and hides
        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            // 1. Log all current items to the Global History
            foreach (var item in PocketedItems)
            {
                if (!AppGlobals.SessionHistoryPaths.Contains(item.FilePath))
                {
                    AppGlobals.SessionHistory.Add(item);
                }
            }

            // 2. Ping the My Pockets window to update in real-time
            var openHistoryWindow = Application.Current.Windows.OfType<MyPocketsWindow>().FirstOrDefault();
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

        private static void MouseTimer_Tick(object sender, EventArgs e)
        {
            // 1. Physically check the hardware switch on the left mouse button
            bool isLeftClickHeld = (AppHelpers.GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

            if (isLeftClickHeld)
            {
                if (!AppGlobals.EnableMouseShake) return;

                // Abort if a pocket was already spawned or the shake was marked invalid
                if (_hasSpawnedPocketThisDrag || _shakeInvalidated) return;

                // 2. Physically interrogate the cursor position
                GetCursorPos(out POINT pt);

                // Create a strict "Anchor Box" to separate Shakes from Drags
                if (_shakeAnchor == null)
                {
                    _shakeAnchor = pt; // Record the exact pixel the click started on
                }
                else
                {
                    // Calculate how far the mouse has drifted from the initial click
                    int driftX = Math.Abs(pt.X - _shakeAnchor.Value.X);
                    int driftY = Math.Abs(pt.Y - _shakeAnchor.Value.Y);

                    // If the mouse drifts more than the designated threshold, treat it as a drag or selection and invalidate the shake
                    if (driftX > 280 || driftY > 200)
                    {
                        _shakeInvalidated = true;
                        return;
                    }
                }

                // 3. Run the math (only runs if the pointer stays within the anchor box)
                bool isShaking = _shakeDetector.CheckForShake(
                    currentMouseX: pt.X,
                    currentTimestampMs: Environment.TickCount64,
                    minDistancePx: AppGlobals.ShakeMinimumDistance,
                    maxTimeMs: 500,
                    requiredSwings: 3
                );

                // 4. Spawn the pocket
                if (isShaking)
                {
                    try
                    {
                        if (AppGlobals.DisableInGameMode && AppHelpers.IsGameModeActive()) return;
                        if (AppHelpers.IsForegroundAppExcluded()) return;
                    }
                    catch { /* Ignore helper errors and spawn anyway */ }

                    _hasSpawnedPocketThisDrag = true;

                    if (Application.Current != null && Application.Current.Dispatcher != null)
                    {
                        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() =>
                        {
                            var hiddenPocket = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => !w.IsHitTestVisible);

                            if (hiddenPocket != null)
                            {
                                hiddenPocket.ShowPocketDrop(pt.X, pt.Y);
                            }
                            else
                            {
                                var newPocket = new MainWindow();
                                newPocket.Show();
                                newPocket.ShowPocketDrop(pt.X, pt.Y);
                            }
                        }));
                    }
                }
            }
            else
            {
                // Reset all safety flags when the mouse button is released
                _hasSpawnedPocketThisDrag = false;
                _shakeAnchor = null;
                _shakeInvalidated = false;
            }
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

        // Clean up leftover ZIPs and Update Installers from previous sessions
        private void CleanupOldShareZips()
        {
            try
            {
                string tempPath = Path.GetTempPath();

                // 1. Find all custom ZIP files in the temp folder
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

                // 2. Sweep old Update Installers
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string updateFolder = Path.Combine(localAppData, "PocketDrop", "Updates");

                if (Directory.Exists(updateFolder))
                {
                    // Find any leftover .exe files in the Updates folder
                    string[] oldInstallers = Directory.GetFiles(updateFolder, "*.exe");
                    foreach (string installer in oldInstallers)
                    {
                        try
                        {
                            File.Delete(installer);
                        }
                        catch
                        {
                            // If the file is locked for some reason, ignore it and try again next time
                        }
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
                int processedCount = 0;

                using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var item in items)
                    {
                        if (Directory.Exists(item.FilePath)) AddDirectoryToZip(archive, item.FilePath, item.FileName);
                        else if (File.Exists(item.FilePath)) archive.CreateEntryFromFile(item.FilePath, item.FileName);

                        // Update the progress bar
                        int currentProgress = System.Threading.Interlocked.Increment(ref processedCount);
                        Application.Current.Dispatcher.InvokeAsync(() => StatusProgressBar.Value = currentProgress);
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
                // Never delete a temp file if it's still saved in My Pockets
                if (AppGlobals.SessionHistoryPaths.Contains(filePath)) return;

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
                ex.Data.Add("PocketDrop Context", "Could not clean up temp file");
                SentrySdk.CaptureException(ex);
            }
        }

        // Syncs the Pocket UI with the global history
        public void RefreshPocketUI()
        {
            // 1. Find items in this Pocket that no longer exist in the global history
            var itemsToRemove = PocketedItems.Where(p => !AppGlobals.SessionHistoryPaths.Contains(p.FilePath)).ToList();

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
                if (AppGlobals.CloseWhenEmptied)
                {
                    ForceClose();
                }
                else
                {
                    // Reset it to the "Drop files here" state
                    if (StatusContainer != null)
                    {
                        StatusContainer.Visibility = Visibility.Visible;
                        StatusProgressBar.Visibility = Visibility.Collapsed;
                    }
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
            // Update the raw numbers
            if (CountNumberText != null) CountNumberText.Text = count.ToString();
            if (PopupCountNumberText != null) PopupCountNumberText.Text = count.ToString();
            if (PopupCountSizeText != null) PopupCountSizeText.Text = ""; // Clear file size

            // Use SetResourceReference to change the word while keeping the live language binding
            string resourceKey = count == 1 ? "Text_Item" : "Text_Items";
            if (CountLabelText != null) CountLabelText.SetResourceReference(TextBlock.TextProperty, resourceKey);
            if (PopupCountLabelText != null) PopupCountLabelText.SetResourceReference(TextBlock.TextProperty, resourceKey);

            // Safely reset the empty state
            if (count == 0 && StatusContainer != null)
            {
                StatusText.SetResourceReference(TextBlock.TextProperty, "Text_DropItemsHere");

                // Show the container but hide the progress bar
                StatusContainer.Visibility = Visibility.Visible;
                StatusProgressBar.Visibility = Visibility.Collapsed;

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

        // Load file icon
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

            string cacheKey = isDirectory ? "folder_icon" : ext;

            // 1. Check the cache first for an instant return, bypassing the lookup
            if (!isImage && !isUnique)
            {
                if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
                {
                    return cachedIcon;
                }
            }

            // 2. Limit concurrent icon requests to 4 at a time
            await _iconThrottle.WaitAsync();

            try
            {
                // 3. Request the icon from Windows when ready
                return await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        if (isImage)
                        {
                            // For images, Windows extracts the embedded EXIF thumbnail directly, bypassing WPF's full 4K decoding overhead
                            using (ShellObject shellObj = ShellObject.FromParsingName(filePath))
                            {
                                var wpfBmp = new BitmapImage();

                                using (var drawingBmp = shellObj.Thumbnail.Bitmap)
                                {
                                    using (var ms = new System.IO.MemoryStream())
                                    {
                                        // Bounce the tiny OS thumbnail through RAM
                                        drawingBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                        ms.Position = 0;

                                        wpfBmp.BeginInit();
                                        wpfBmp.CacheOption = BitmapCacheOption.OnLoad;
                                        wpfBmp.StreamSource = ms;
                                        wpfBmp.EndInit();
                                    }
                                }

                                wpfBmp.Freeze();
                                return (System.Windows.Media.ImageSource)wpfBmp;
                            }
                        }
                        else
                        {
                            // Wrapped the native COM object in a using statement so it gets destroyed
                            using (ShellObject shellObj = ShellObject.FromParsingName(filePath))
                            {
                                var unmanagedThumb = shellObj.Thumbnail.LargeBitmapSource;

                                // ? THE FATAL FLAW FIX: 
                                // Deep-copy the unmanaged COM image into safe, managed WPF memory
                                var wpfBmp = new BitmapImage();
                                using (var ms = new System.IO.MemoryStream())
                                {
                                    // Encode the unmanaged source to a PNG stream
                                    var encoder = new PngBitmapEncoder();
                                    encoder.Frames.Add(BitmapFrame.Create(unmanagedThumb));
                                    encoder.Save(ms);

                                    ms.Position = 0;

                                    // Load it back into a purely managed BitmapImage
                                    wpfBmp.BeginInit();
                                    wpfBmp.CacheOption = BitmapCacheOption.OnLoad; // CRITICAL: Copies pixels to RAM
                                    wpfBmp.StreamSource = ms;
                                    wpfBmp.EndInit();
                                }

                                wpfBmp.Freeze(); // Lock it so it can be shared across the UI thread

                                // Save to cache for next time
                                if (!isUnique)
                                {
                                    _iconCache.TryAdd(cacheKey, wpfBmp);
                                }

                                return (System.Windows.Media.ImageSource)wpfBmp;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 1. Tag it with the context
                        ex.Data.Add("PocketDrop Context", "Icon error");

                        // 2. Send it off to Sentry
                        SentrySdk.CaptureException(ex);
                        return null;
                    }
                });
            }
            finally
            {
                // 4. Always release the throttle so the next file can proceed
                _iconThrottle.Release();
            }
        }

        // Helper to safely locate the hidden ScrollViewer inside a WPF ListBox
        private ScrollViewer GetScrollViewer(DependencyObject depObj)
        {
            if (depObj == null) return null;
            if (depObj is ScrollViewer scrollViewer) return scrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        // Drag & drop adorner — draws the insertion line above the UI
        public class DropLineAdorner : System.Windows.Documents.Adorner
        {
            private bool _isTopOrLeft;
            private bool _isGrid;

            public DropLineAdorner(UIElement adornedElement, bool isTopOrLeft, bool isGrid) : base(adornedElement)
            {
                _isTopOrLeft = isTopOrLeft;
                _isGrid = isGrid;
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                var pen = new Pen(System.Windows.Media.Brushes.DodgerBlue, 3);
                pen.Freeze();

                double width = AdornedElement.RenderSize.Width;
                double height = AdornedElement.RenderSize.Height;

                if (_isGrid)
                {
                    double x = _isTopOrLeft ? 0 : width;
                    drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, height));
                }
                else
                {
                    double y = _isTopOrLeft ? 0 : height;
                    drawingContext.DrawLine(pen, new Point(0, y), new Point(width, y));
                }
            }
        }

        private void ItemsListBox_DragOver(object sender, DragEventArgs e)
        {
            if (_internalDragPayload == null) return;

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            // Auto-scroll logic
            ScrollViewer scrollViewer = GetScrollViewer(ItemsListBox);
            if (scrollViewer != null)
            {
                double tolerance = 40;
                double scrollSpeed = 8;
                Point mousePos = e.GetPosition(ItemsListBox);

                if (mousePos.Y < tolerance)
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollSpeed);
                else if (mousePos.Y > ItemsListBox.ActualHeight - tolerance)
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollSpeed);
            }

            var hit = e.OriginalSource as DependencyObject;
            var targetItem = FindAncestor<ListBoxItem>(hit);

            if (targetItem == null)
            {
                ClearDragHighlight();
                return;
            }

            bool isGrid = this.CurrentViewMode == "Grid";

            // Calculate if the mouse is on the first half or second half of the item
            Point pos = e.GetPosition(targetItem);
            bool insertBefore = isGrid
                ? pos.X < (targetItem.ActualWidth / 2)
                : pos.Y < (targetItem.ActualHeight / 2);

            // Only jump to the next item if it's on the same physical row
            if (!insertBefore)
            {
                int index = ItemsListBox.ItemContainerGenerator.IndexFromContainer(targetItem);

                if (index >= 0 && index < ItemsListBox.Items.Count - 1)
                {
                    var nextItem = (ListBoxItem)ItemsListBox.ItemContainerGenerator.ContainerFromIndex(index + 1);
                    if (nextItem != null)
                    {
                        if (isGrid)
                        {
                            try
                            {
                                // Check their actual vertical positions on the screen
                                Point targetUiPos = targetItem.TransformToAncestor(ItemsListBox).Transform(new Point(0, 0));
                                Point nextUiPos = nextItem.TransformToAncestor(ItemsListBox).Transform(new Point(0, 0));

                                if (Math.Abs(targetUiPos.Y - nextUiPos.Y) < 10)
                                {
                                    targetItem = nextItem;
                                    insertBefore = true;
                                }
                            }
                            catch { } // Failsafe: if UI isn't ready, just keep drawing on the right edge
                        }
                        else
                        {
                            // In standard vertical List view, always snap to the top of the next item
                            targetItem = nextItem;
                            insertBefore = true;
                        }
                    }
                }
            }

            if (_lastHoveredItem == targetItem && _insertAbove == insertBefore) return;

            ClearDragHighlight();

            _lastHoveredItem = targetItem;
            _insertAbove = insertBefore;

            var adornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(targetItem);
            if (adornerLayer != null)
            {
                _currentAdorner = new DropLineAdorner(targetItem, insertBefore, isGrid);
                adornerLayer.Add(_currentAdorner);
            }
        }

        private void ItemsListBox_DragLeave(object sender, DragEventArgs e)
        {
            ClearDragHighlight();
        }

        private void ClearDragHighlight()
        {
            if (_lastHoveredItem != null && _currentAdorner != null)
            {
                var adornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(_lastHoveredItem);
                adornerLayer?.Remove(_currentAdorner);

                _currentAdorner = null;
                _lastHoveredItem = null;
            }
        }
        private void ItemsListBox_Drop(object sender, DragEventArgs e)
        {
            // Capture the exact target where the blue line was drawn before clearing it
            var finalTargetItem = _lastHoveredItem;
            bool finalInsertAbove = _insertAbove;

            ClearDragHighlight();

            // Force-hide the main window glow
            DragGlowBorder.Visibility = Visibility.Collapsed;

            if (_internalDragPayload != null)
            {
                _internalDropHandled = true;
                e.Effects = DragDropEffects.Move;

                var draggedItems = _internalDragPayload;
                if (draggedItems.Count == 0) return;

                int insertIndex = PocketedItems.Count;

                if (finalTargetItem != null)
                {
                    var targetData = finalTargetItem.DataContext as PocketItem;
                    insertIndex = PocketedItems.IndexOf(targetData);

                    if (!finalInsertAbove) insertIndex++;

                    int originalIndex = PocketedItems.IndexOf(draggedItems[0]);
                    if (originalIndex < insertIndex) insertIndex--;
                }

                ItemsListBox.SelectionChanged -= ItemsListBox_SelectionChanged;

                foreach (var item in draggedItems) PocketedItems.Remove(item);
                if (insertIndex > PocketedItems.Count) insertIndex = PocketedItems.Count;
                for (int i = 0; i < draggedItems.Count; i++) PocketedItems.Insert(insertIndex + i, draggedItems[i]);

                ItemsListBox.SelectionChanged += ItemsListBox_SelectionChanged;
                ItemsListBox.UnselectAll();

                UpdateStackPreview();
                e.Handled = true;
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T ancestor) return ancestor;
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        // Smart filter: Extracts only valid images and handles empty or invalid selections
        private List<PocketItem> GetOnlyValidImages()
        {
            // 1. Get the current selection, or all items if nothing is selected
            var itemsToProcess = (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
                ? ItemsListBox.SelectedItems.Cast<PocketItem>().ToList()
                : PocketedItems.ToList();

            // 2. Define what counts as a valid image for these tools
            string[] validExts = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif", ".dng", ".heif", ".svg", ".avif", ".tiff", ".eps" };

            // 3. Filter the list, silently dropping non-image files such as .exe, .pdf, and folders
            var validImages = itemsToProcess.Where(item =>
                validExts.Contains(Path.GetExtension(item.FilePath).ToLower()) &&
                File.Exists(item.FilePath) &&
                new FileInfo(item.FilePath).Length > 0 // Explicitly reject 0-byte files
            ).ToList();

            // 4. Show a warning if no valid images remain
            if (validImages.Count == 0)
            {
                string warningTitle = (string)Application.Current.TryFindResource("Text_NoImagesTitle") ?? "Invalid Selection";
                string warningDesc = (string)Application.Current.TryFindResource("Text_NoImagesDesc") ?? "Please select at least one valid image file to use this tool.";

                MessageBox.Show(warningDesc, warningTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return validImages;
        }
    }
}