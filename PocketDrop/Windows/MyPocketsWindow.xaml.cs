// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PocketDrop
{
    public partial class MyPocketsWindow : Window
    {
        // ================================================ //
        // 1. STATE & VARIABLES
        // ================================================ //

        // Track drag-and-drop offsets and multi-select state
        private Point? _listDragStart = null;
        private List<PocketItem> _dragCandidates = null;

        // Sorting state (0 = Default, 1 = A-Z, 2 = Z-A)
        private int _currentSortState = 0;


        // ================================================ //
        // 2. WINDOW LIFECYCLE & ANIMATIONS
        // ================================================ //
        public MyPocketsWindow()
        {
            InitializeComponent();

            HistoryListBox.ItemsSource = AppGlobals.SessionHistory;
            RefreshHistory();
            // Tell this to auto run RefreshHistory() to background data changes
            AppGlobals.SessionHistory.CollectionChanged += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() => RefreshHistory());
            };
        }

        // Snap the window to the taskbar
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Point snapPos = AppHelpers.CalculateTaskbarSnapPosition(
                this.Width, this.Height,
                SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height,
                shadowMargin: 13);

            this.Left = snapPos.X;
            this.Top = snapPos.Y;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideIn = new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };

            this.BeginAnimation(OpacityProperty, fadeIn);
            WindowTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        }

        // The custom close engine
        private void AnimateClose()
        {
            // Prevent multiple closing triggers
            this.IsHitTestVisible = false;

            var fadeOut = new DoubleAnimation(this.Opacity, 0, TimeSpan.FromMilliseconds(250));
            var slideOut = new DoubleAnimation(0, 20, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, e) => this.Close();

            this.BeginAnimation(OpacityProperty, fadeOut);
            WindowTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
        }

        // Pin My Pockets
        private bool isPinned = false;

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (!isPinned)
            {
                AnimateClose();
            }
        }

        private void PinWindowBtn_Click(object sender, RoutedEventArgs e)
        {
            isPinned = !isPinned;
            this.Topmost = isPinned;
            PinWindowBtn.Opacity = isPinned ? 1.0 : 0.5;
        }

        // Enable window dragging
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void ShowToast(string message)
        {
            ToastText.Text = message;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = TimeSpan.FromSeconds(1.5)
            };

            var storyboard = new System.Windows.Media.Animation.Storyboard();
            storyboard.Children.Add(fadeIn);
            storyboard.Children.Add(fadeOut);

            System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, ToastPopup);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            System.Windows.Media.Animation.Storyboard.SetTarget(fadeOut, ToastPopup);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));

            storyboard.Begin();
        }


        // ================================================ //
        // 3. CORE DATA & UI SYNC
        // ================================================ //
        public void RefreshHistory()
        {
            if (AppGlobals.SessionHistory.Count > 0)
            {
                EmptyStateText.Visibility = Visibility.Collapsed;
                HistoryListBox.Visibility = Visibility.Visible;
                SelectAllBar.Visibility = Visibility.Visible;

            }
            else
            {
                EmptyStateText.Visibility = Visibility.Visible;
                HistoryListBox.Visibility = Visibility.Collapsed;
                SelectAllBar.Visibility = Visibility.Collapsed;
            }
        }


        // ================================================ //
        // 4. SIDEBAR ACTIONS & NAVIGATION
        // ================================================ //

        // Create a new Pocket
        private void AddPocket_Click(object sender, RoutedEventArgs e)
        {
            AppGlobals.TriggerNewPocket();
        }

        private void TabHome_Click(object sender, RoutedEventArgs e)
        {
            // 1. Get the current view of the ListBox
            ICollectionView view = CollectionViewSource.GetDefaultView(HistoryListBox.ItemsSource);

            if (view != null)
            {
                // 2. Clear the filter to show all items
                view.Filter = null;
            }
        }

        private void TabPinned_Click(object sender, RoutedEventArgs e)
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(HistoryListBox.ItemsSource);

            if (view != null)
            {
                // Filter to show only pinned items
                view.Filter = item =>
                {
                    if (item is PocketItem pocket)
                    {
                        return pocket.IsPinned;
                    }
                    return false;
                };
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(HistoryListBox.ItemsSource);

            // Refresh the view filter immediately
            if (view != null)
            {
                view.Refresh();
            }
        }

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            // 1. Move to the next state (Looping back to 0 if it hits 3)
            _currentSortState = (_currentSortState + 1) % 3;

            // 2. Grab the live view of the ListBox
            ICollectionView view = CollectionViewSource.GetDefaultView(HistoryListBox.ItemsSource);
            if (view == null) return;

            // 3. Clear any existing sorts to start fresh
            view.SortDescriptions.Clear();

            // 4. Apply the new sorting rule and update the UI text
            switch (_currentSortState)
            {
                case 0: // Default Order
                    SortText.Text = (string)Application.Current.Resources["Text_SortDefault"];
                    SortButton.ToolTip = (string)Application.Current.Resources["Text_SortDefaultTooltip"];
                    ShowToast((string)Application.Current.Resources["Text_ToastRestoredDefault"]);
                    break;

                case 1: // A to Z
                    SortText.Text = (string)Application.Current.Resources["Text_SortAtoZ"];
                    SortButton.ToolTip = (string)Application.Current.Resources["Text_SortAtoZTooltip"];
                    view.SortDescriptions.Add(new SortDescription("FileName", ListSortDirection.Ascending));
                    ShowToast((string)Application.Current.Resources["Text_ToastSortedAtoZ"]);
                    break;

                case 2: // Z to A
                    SortText.Text = (string)Application.Current.Resources["Text_SortZtoA"];
                    SortButton.ToolTip = (string)Application.Current.Resources["Text_SortZtoATooltip"];
                    view.SortDescriptions.Add(new SortDescription("FileName", ListSortDirection.Descending));
                    ShowToast((string)Application.Current.Resources["Text_ToastSortedZtoA"]);
                    break;
            }
        }

        // Open Settings
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var existingSettings = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

            if (existingSettings != null)
            {
                if (existingSettings.WindowState == WindowState.Minimized)
                {
                    existingSettings.WindowState = WindowState.Normal;
                }

                existingSettings.Activate();
            }
            else
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.Show();
                settingsWindow.Activate();
            }

            this.Close();
        }


        // ================================================ //
        // 5. DRAG, DROP & ITEM INTERACTION
        // ================================================ //

        // Variables to track drag-and-drop math and multi-select snapshots
        // 1. Record mouse-down pixel position for drag tracking
        private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. Check if it was a double click
            if (e.ClickCount == 2)
            {
                if (sender is FrameworkElement element && element.DataContext is PocketItem pocket)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                        {
                            FileName = pocket.FilePath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        string errorTitle = (string)Application.Current.Resources["Text_ErrorTitle"] ?? "Error";
                        string errorPrefix = (string)Application.Current.Resources["Text_OpenFileError"] ?? "Could not open file:";

                        MessageBox.Show($"{errorPrefix}\n\n{ex.Message}", errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                e.Handled = true;
                return;
            }

            // 2. Detect single click on already-selected item
            if (sender is FrameworkElement frameworkElement && frameworkElement.DataContext is PocketItem clickedItem)
            {
                if (HistoryListBox.SelectedItems.Contains(clickedItem))
                {
                    // Snapshot and freeze selection to prevent deselect on click
                    _dragCandidates = HistoryListBox.SelectedItems.Cast<PocketItem>().ToList();
                    _listDragStart = e.GetPosition(null);
                    e.Handled = true;
                }
                else
                {
                    // Clear snapshot and allow normal selection on unselected item click
                    _dragCandidates = null;
                    _listDragStart = e.GetPosition(null);
                }
            }
        }

        // 2. Initiate drag on click-hold and mouse move
        private void Item_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _listDragStart == null) return;

            Point mousePos = e.GetPosition(null);
            Vector diff = (Point)_listDragStart - mousePos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is FrameworkElement element && element.DataContext is PocketItem clickedItem)
                {
                    // Use multi-select snapshot or fall back to single item for drag
                    var selectedItems = _dragCandidates ?? new List<PocketItem> { clickedItem };

                    // Gather all the file paths
                    string[] filePaths = selectedItems
                        .Where(p => !string.IsNullOrEmpty(p.FilePath))
                        .Select(p => p.FilePath)
                        .ToArray();

                    if (filePaths.Length > 0)
                    {
                        // Match standard Windows drag behavior
                        bool isShiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                        bool isCtrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

                        // Initialize with the user's saved setting
                        bool isCopy = AppGlobals.CopyItemToDestination;

                        // Apply Windows overrides
                        if (isShiftDown) isCopy = false;      // Shift forces a Move
                        else if (isCtrlDown) isCopy = true;   // Ctrl forces a Copy

                        DataObject dragData = new DataObject(DataFormats.FileDrop, filePaths);

                        using (var dropEffectStream = new System.IO.MemoryStream(new byte[] { (byte)(isCopy ? 1 : 2), 0, 0, 0 }))
                        {
                            dragData.SetData("Preferred DropEffect", dropEffectStream);
                        }

                        // Reset drag trackers before starting the drag operation
                        _listDragStart = null;
                        _dragCandidates = null;

                        DragDropEffects result = DragDrop.DoDragDrop(element, dragData, DragDropEffects.All);

                        // On successful move, remove the source file and update history
                        if (result != DragDropEffects.None && !isCopy)
                        {
                            foreach (var item in selectedItems)
                            {
                                // If the source was a modified temp file, delete the original to complete the move.
                                if (item.OriginalFilePath != item.FilePath)
                                {
                                    try { if (System.IO.File.Exists(item.OriginalFilePath)) System.IO.File.Delete(item.OriginalFilePath); } catch { }
                                }

                                AppGlobals.SessionHistory.Remove(item);
                            }

                            // Remove the file from the UI immediately
                            RefreshHistory();
                            AppGlobals.TriggerPocketsRefresh();
                        }
                    }
                }
            }
        }

        private void Item_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_listDragStart != null)
            {
                if (sender is FrameworkElement element && element.DataContext is PocketItem clickedItem)
                {
                    if (_dragCandidates != null)
                    {
                        if (HistoryListBox.SelectedItems.Contains(clickedItem))
                        {
                            HistoryListBox.SelectedItems.Remove(clickedItem);
                        }
                        else
                        {
                            HistoryListBox.SelectedItems.Add(clickedItem);
                        }

                        e.Handled = true;
                    }
                }

                _listDragStart = null;
                _dragCandidates = null;
            }
        }


        // ================================================ //
        // 6. MULTI-SELECT & DELETION LOGIC
        // ================================================ //

        // Select all logic
        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            HistoryListBox?.SelectAll();
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            HistoryListBox?.UnselectAll();
        }

        private void SelectAllBar_Click(object sender, MouseButtonEventArgs e)
        {
            if (SelectAllCheckBox != null)
            {
                SelectAllCheckBox.IsChecked = !SelectAllCheckBox.IsChecked;
            }
        }

        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Make sure the button has loaded before modify it
            if (DeleteSelectedBtn != null && HistoryListBox != null)
            {
                // If 1 or more items are highlighted, show the Delete selected button
                if (HistoryListBox.SelectedItems.Count > 0)
                {
                    DeleteSelectedBtn.Visibility = Visibility.Visible;
                }
                // If nothing is highlighted, hide it to prevent accidental clicks
                else
                {
                    DeleteSelectedBtn.Visibility = Visibility.Collapsed;
                }
            }
        }

        // Trash icon
        private void Trash_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmPopup.PlacementTarget = TrashButton;
            DeleteConfirmPopup.IsOpen = true;
        }

        // Popup cancel: Confirmation box
        private void CloseDeletePopup_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmPopup.IsOpen = false;
        }

        private void DeletePocketItems(List<PocketItem> itemsToDelete)
        {
            string tempFolder = System.IO.Path.GetTempPath();

            foreach (var item in itemsToDelete)
            {
                try
                {
                    // Delete the physical file only if it lives inside the Temp folder
                    if (!string.IsNullOrEmpty(item.FilePath) && item.FilePath.StartsWith(tempFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        if (System.IO.File.Exists(item.FilePath)) System.IO.File.Delete(item.FilePath);
                    }
                }
                catch { }

                // Remove it from the UI list and global RAM
                AppGlobals.SessionHistory.Remove(item);
            }
        }

        // Delete command: Confirmation box
        private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            // 1. Hide the popup
            DeleteConfirmPopup.IsOpen = false;

            // 2. Remove only unpinned items from memory on clear
            var itemsToDelete = AppGlobals.SessionHistory.Cast<PocketItem>().Where(p => !p.IsPinned).ToList();

            DeletePocketItems(itemsToDelete);

            // 3. Refresh the UI to reflect the remaining items
            RefreshHistory();

            // 4. Only close Pockets that are visible on screen.
            AppGlobals.TriggerPocketsForceClose();
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            // 1. Check if the user actually selected anything
            if (HistoryListBox.SelectedItems.Count == 0) return;

            // 2. Grab a snapshot of the highlighted items
            var itemsToDelete = HistoryListBox.SelectedItems.Cast<PocketItem>().ToList();

            DeletePocketItems(itemsToDelete);

            // 4. Uncheck the "Select all" box
            if (SelectAllCheckBox != null)
            {
                SelectAllCheckBox.IsChecked = false;
            }

            // 5. Refresh the UI
            RefreshHistory();

            // Notify any open floating Pockets to refresh visuals
            AppGlobals.TriggerPocketsRefresh();
        }
    }
}