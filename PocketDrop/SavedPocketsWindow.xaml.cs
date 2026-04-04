using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PocketDrop
{
    public partial class SavedPocketsWindow : Window
    {
        public SavedPocketsWindow()
        {
            InitializeComponent();
            RefreshHistory();
        }

        // --- Snap seamlessly to the taskbar! ---
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // ✨ Fetch the decoupled math!
            Point snapPos = AppHelpers.CalculateTaskbarSnapPosition(
                this.Width, this.Height,
                SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height,
                shadowMargin: 13);

            // Apply the coordinates
            this.Left = snapPos.X;
            this.Top = snapPos.Y;

            // ✨ THE ENTRANCE ANIMATION
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var slideIn = new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };

            this.BeginAnimation(OpacityProperty, fadeIn);
            WindowTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        }

        // --- THE CUSTOM CLOSE ENGINE ---
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

        // --- LIGHT DISMISS: Close automatically if the user clicks away! ---
        private void Window_Deactivated(object sender, EventArgs e) => AnimateClose();
        private void Close_Click(object sender, RoutedEventArgs e) => AnimateClose();

        // --- Checks the RAM and updates the UI ---
        public void RefreshHistory()
        {
            if (App.SessionHistory.Count > 0)
            {
                EmptyStateText.Visibility = Visibility.Collapsed;
                HistoryListBox.Visibility = Visibility.Visible;
                SelectAllBar.Visibility = Visibility.Visible;

                HistoryListBox.ItemsSource = null;
                HistoryListBox.ItemsSource = App.SessionHistory;
            }
            else
            {
                EmptyStateText.Visibility = Visibility.Visible;
                HistoryListBox.Visibility = Visibility.Collapsed;
                SelectAllBar.Visibility = Visibility.Collapsed;
            }
        }

        // --- BOTTOM BUTTON: '+' (Spawns a new Pocket) ---
        private void AddPocket_Click(object sender, RoutedEventArgs e)
        {
            var newPocket = new MainWindow();
            newPocket.Show();
            newPocket.Opacity = 1;
            newPocket.IsHitTestVisible = true;
            newPocket.Activate();
        }

        // --- BOTTOM BUTTON: 'Trash' (Opens the popup!) ---
        private void Trash_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmPopup.PlacementTarget = TrashButton;
            DeleteConfirmPopup.IsOpen = true;
        }

        // --- THE ACTUAL DELETE COMMAND ---
        private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            // 1. Hide the popup
            DeleteConfirmPopup.IsOpen = false;

            // 2. Find ONLY the items that are NOT pinned, and remove them from the RAM
            // (Note: Replace 'PocketItem' with the actual name of your data class if it's different!)
            var itemsToDelete = App.SessionHistory.Cast<PocketItem>().Where(p => !p.IsPinned).ToList();

            foreach (var item in itemsToDelete)
            {
                App.SessionHistory.Remove(item);
            }

            // 3. Refresh the UI to reflect the remaining items
            RefreshHistory();

            // 4. Only close pockets that are actually visible on screen.
            var openPockets = Application.Current.Windows.OfType<MainWindow>().ToList();
            foreach (var pocket in openPockets)
            {
                if (pocket.IsLoaded && pocket.Visibility == Visibility.Visible && pocket.Opacity >= 0.99)
                {
                    pocket.ForceClose();
                }
            }
        }

        // --- MAKES THE WINDOW DRAGGABLE ---
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            // Allow dragging the window anywhere you click!
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // --- POPUP CANCEL: Just hides the confirmation box ---
        private void CloseDeletePopup_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmPopup.IsOpen = false;
        }

        // --- BOTTOM BUTTON: 'Gear' (Opens Settings) ---
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            // 1. Check if a Settings window is already open anywhere in the app
            var existingSettings = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

            if (existingSettings != null)
            {
                // If it is minimized, restore it
                if (existingSettings.WindowState == WindowState.Minimized)
                {
                    existingSettings.WindowState = WindowState.Normal;
                }

                // Bring the existing window to the absolute front!
                existingSettings.Activate();
            }
            else
            {
                // If one doesn't exist, create a brand new one
                var settingsWindow = new SettingsWindow();
                settingsWindow.Show();
                settingsWindow.Activate();
            }

            // Close the Saved Pockets popup so it gets out of the way
            this.Close();
        }

        private void TabHome_Click(object sender, RoutedEventArgs e)
        {
            // 1. Get the current view of your ListBox
            ICollectionView view = CollectionViewSource.GetDefaultView(HistoryListBox.ItemsSource);

            if (view != null)
            {
                // 2. Clear the filter to show EVERYTHING
                view.Filter = null;
            }
        }

        private void TabPinned_Click(object sender, RoutedEventArgs e)
        {
            // 1. Get the current view of your ListBox
            ICollectionView view = CollectionViewSource.GetDefaultView(HistoryListBox.ItemsSource);

            if (view != null)
            {
                // 2. Apply a filter to only show items where IsPinned == true
                view.Filter = item =>
                {
                    // Note: Change 'PocketItem' to whatever your actual data class is called!
                    if (item is PocketItem pocket)
                    {
                        return pocket.IsPinned;
                    }
                    return false;
                };
            }
        }

        // Variables to track drag-and-drop math and multi-select snapshots
        private Point? _listDragStart = null;
        private List<PocketItem> _dragCandidates = null;

        // 1. Record the exact pixel the mouse clicked down on
        private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. Check if it was a DOUBLE CLICK
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
                        MessageBox.Show("Could not open file: " + ex.Message, "Error");
                    }
                }
                e.Handled = true;
                return;
            }

            // 2. It's a SINGLE CLICK. Check if they clicked an item that is ALREADY highlighted.
            if (sender is FrameworkElement frameworkElement && frameworkElement.DataContext is PocketItem clickedItem)
            {
                if (HistoryListBox.SelectedItems.Contains(clickedItem))
                {
                    // Snapshot the selection and FREEZE the ListBox so it doesn't deselect the others!
                    _dragCandidates = HistoryListBox.SelectedItems.Cast<PocketItem>().ToList();
                    _listDragStart = e.GetPosition(null);
                    e.Handled = true;
                }
                else
                {
                    // If they clicked an unselected item, clear the snapshot and let the ListBox select it normally.
                    _dragCandidates = null;
                    _listDragStart = e.GetPosition(null);
                }
            }
        }

        // 2. If they hold the click and move the mouse, initiate the Drag!
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
                    // Use the multi-select snapshot if we have one, otherwise just use the single clicked item
                    var selectedItems = _dragCandidates ?? new List<PocketItem> { clickedItem };

                    // Gather all the file paths safely
                    string[] filePaths = selectedItems
                        .Where(p => !string.IsNullOrEmpty(p.FilePath))
                        .Select(p => p.FilePath)
                        .ToArray();

                    if (filePaths.Length > 0)
                    {
                        DataObject dragData = new DataObject(DataFormats.FileDrop, filePaths);

                        // Reset the trackers before the drag locks the thread
                        _listDragStart = null;
                        _dragCandidates = null;

                        DragDrop.DoDragDrop(element, dragData, DragDropEffects.Copy);
                    }
                }
            }
        }

        // --- NAVIGATION: SELECT ALL LOGIC ---
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

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            // 1. Check if they actually selected anything
            if (HistoryListBox.SelectedItems.Count == 0) return;

            // 2. Grab a snapshot of the highlighted items
            var itemsToDelete = HistoryListBox.SelectedItems.Cast<PocketItem>().ToList();

            // 3. Remove them from the global history
            foreach (var item in itemsToDelete)
            {
                App.SessionHistory.Remove(item);
            }

            // 4. Uncheck the "Select all" box since those files are gone now
            if (SelectAllCheckBox != null)
            {
                SelectAllCheckBox.IsChecked = false;
            }

            // 5. Refresh the UI!
            RefreshHistory();

            // Note: We completely removed the logic that closes MainWindow pockets.
            // Your drop zones will now stay safely open even if the history is empty!

            // Tell any open floating pockets to refresh their visuals!
            var openPockets = Application.Current.Windows.OfType<MainWindow>().ToList();
            foreach (var pocket in openPockets)
            {
                if (pocket.IsLoaded)
                {
                    // We will trigger a public method inside MainWindow
                    pocket.RefreshPocketUI();
                }
            }
        }

        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Make sure the button has loaded before we try to modify it
            if (DeleteSelectedBtn != null && HistoryListBox != null)
            {
                // If 1 or more items are highlighted, show the Delete button
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

        private void Item_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_listDragStart != null)
            {
                if (sender is FrameworkElement element && element.DataContext is PocketItem clickedItem)
                {
                    // If we clicked a file that was ALREADY in the blue group...
                    if (_dragCandidates != null)
                    {
                        // Toggle it off
                        if (HistoryListBox.SelectedItems.Contains(clickedItem))
                        {
                            HistoryListBox.SelectedItems.Remove(clickedItem);
                        }
                        else
                        {
                            HistoryListBox.SelectedItems.Add(clickedItem);
                        }

                        // ✨ THE CRITICAL FIX: Tell WPF to stop processing this click!
                        // Without this, the native ListBox instantly re-selects the file the millisecond we remove it.
                        e.Handled = true;
                    }
                }

                // Clean up the trackers for the next click
                _listDragStart = null;
                _dragCandidates = null;
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Get the current view of the ListBox
            ICollectionView view = CollectionViewSource.GetDefaultView(HistoryListBox.ItemsSource);

            // 2. Force the view to re-evaluate its filter immediately!
            if (view != null)
            {
                view.Refresh();
            }
        }

        // 0 = Default, 1 = A-Z, 2 = Z-A
        private int _currentSortState = 0;

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            // 1. Move to the next state (Looping back to 0 if it hits 3)
            _currentSortState = (_currentSortState + 1) % 3;

            // 2. Grab the live view of your ListBox
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

        private void ShowToast(string message)
        {
            ToastText.Text = message;

            // Fade in quickly
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));

            // Wait 1.5 seconds, then fade out
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
    }
}