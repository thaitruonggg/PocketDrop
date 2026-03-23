using System.Windows;
using System.Windows.Input;

namespace PocketDrop
{
    public partial class SavedPocketsWindow : Window
    {
        public SavedPocketsWindow()
        {
            InitializeComponent();
            RefreshHistory();
        }

        // --- NEW: Snap to the bottom right of the screen! ---
        // --- Snap seamlessly to the taskbar! ---
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            double workAreaWidth = SystemParameters.WorkArea.Width;
            double workAreaHeight = SystemParameters.WorkArea.Height;

            // Because the XAML has Margin="15" for the drop shadow, the window has 15px of invisible padding.
            // We shift the window +15 to push that invisible padding off-screen!
            double shadowMargin = 15;

            // Perfectly flush against the right edge and the bottom taskbar
            this.Left = workAreaWidth - this.Width + shadowMargin;
            this.Top = workAreaHeight - this.Height + shadowMargin;
        }

        // --- LIGHT DISMISS: Close automatically if the user clicks away! ---
        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Close();
        }

        // --- Checks the RAM and updates the UI ---
        public void RefreshHistory()
        {
            if (App.SessionHistory.Count > 0)
            {
                EmptyStateText.Visibility = Visibility.Collapsed;
                HistoryListBox.Visibility = Visibility.Visible;

                HistoryListBox.ItemsSource = null;
                HistoryListBox.ItemsSource = App.SessionHistory;
            }
            else
            {
                EmptyStateText.Visibility = Visibility.Visible;
                HistoryListBox.Visibility = Visibility.Collapsed;
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

        // --- BOTTOM BUTTON: 'Trash' (Clears the RAM) ---
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

            // 2. Clear the RAM and refresh this window
            App.SessionHistory.Clear();
            RefreshHistory();

            // 3. Find every single open pocket on the screen and force it to close!
            // (.ToList() is crucial here so the loop doesn't crash as windows disappear)
            var openPockets = Application.Current.Windows.OfType<MainWindow>().ToList();
            foreach (var pocket in openPockets)
            {
                pocket.ForceClose();
            }
        }

        // --- BOTTOM BUTTON: 'X' (Closes this window) ---
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
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
    }
}