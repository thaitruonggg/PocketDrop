using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PocketDrop
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // ══════════════════════════════════════════════════════
        // P/INVOKE — Low-level global mouse hook
        // ══════════════════════════════════════════════════════
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int VK_LBUTTON = 0x01;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

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

        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelMouseProc _hookProc; // Keep reference — prevents GC

        // ══════════════════════════════════════════════════════
        // SHAKE DETECTION PARAMETERS
        // ══════════════════════════════════════════════════════
        private const int MIN_SWING_PX = 40;   // min horizontal pixels per swing direction
        private const int REQUIRED_SWINGS = 3;    // number of direction reversals needed
        private const int SHAKE_WINDOW_MS = 1000; // all reversals must happen within this ms window

        private bool _leftButtonHeld = false;
        private int _lastX = 0;
        private int _currentDir = 0;       // +1 = right, -1 = left, 0 = none
        private int _swingOriginX = 0;       // X where current swing started
        private readonly Queue<long> _swingTimestamps = new Queue<long>();

        // ══════════════════════════════════════════════════════
        // --- VIEW MODE LOGIC ---
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

        private void ViewMode_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag != null)
            {
                CurrentViewMode = border.Tag.ToString();
            }
        }

        // --- NEW MEMORY: Holds multiple items and updates the UI automatically ---
        public ObservableCollection<PocketItem> PocketedItems { get; set; } = new ObservableCollection<PocketItem>();

        private Point? startPoint = null;
        private Point? _listDragStart = null;
        private List<PocketItem> _dragCandidates = null;

        // NEW: The safety flag to prevent self-drops
        private bool _isDraggingFromApp = false;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            // Start hidden — shake to reveal
            this.Opacity = 0;
            this.IsHitTestVisible = false;

            // Install global mouse hook
            _hookProc = HookCallback;
            using (var proc = Process.GetCurrentProcess())
            using (var mod = proc.MainModule)
                _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(mod.ModuleName), 0);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Uninstall hook when app exits
            if (_hookHandle != IntPtr.Zero)
                UnhookWindowsHookEx(_hookHandle);
            base.OnClosed(e);
        }

        // --- CATCHING THE FILES (Dropping In) ---
        private void Window_Drop(object sender, DragEventArgs e)
        {
            // SAFETY: If the user dropped the files back onto the app itself, cancel everything!
            if (_isDraggingFromApp)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            // 1. HANDLE STANDARD FILES (.png, .pdf, .txt, etc.)
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (droppedFiles != null && droppedFiles.Length > 0)
                {
                    foreach (string filePath in droppedFiles)
                    {
                        string fileName = Path.GetFileName(filePath);
                        System.Windows.Media.ImageSource fileIcon = null;

                        try
                        {
                            string ext = Path.GetExtension(filePath).ToLower();
                            string[] imageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

                            // If it's an image, load the actual picture
                            if (Array.Exists(imageExts, x => x == ext))
                            {
                                BitmapImage img = new BitmapImage();
                                img.BeginInit();
                                img.UriSource = new Uri(filePath);
                                img.CacheOption = BitmapCacheOption.OnLoad;
                                img.EndInit();
                                fileIcon = img;
                            }
                            // If it's literally ANY other file type, ask Windows for its icon
                            else
                            {
                                ShellFile shellFile = ShellFile.FromFilePath(filePath);
                                fileIcon = shellFile.Thumbnail.ExtraLargeBitmapSource;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not load icon for {fileName}: {ex.Message}");
                        }

                        PocketedItems.Add(new PocketItem
                        {
                            FileName = fileName,
                            FilePath = filePath,
                            Icon = fileIcon
                        });
                    }

                    // Update UI
                    StatusText.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;
                    UpdateStackPreview();

                    CountText.Text = $"{PocketedItems.Count} Items";
                    if (ItemsListBox != null && ItemsListBox.SelectedItems.Count == 0)
                    {
                        PopupCountText.Text = $"{PocketedItems.Count} Items";
                    }
                }
            }

            // 2. HANDLE DRAGGED URLS FROM WEB BROWSERS
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
                        string tempFolder = Path.GetTempPath();
                        string fileName = $"{domain} Link_{DateTime.Now.Ticks}.url";
                        string filePath = Path.Combine(tempFolder, fileName);

                        // Generate the physical shortcut file
                        File.WriteAllText(filePath, $"[InternetShortcut]\nURL={droppedText}");

                        ShellFile shellFile = ShellFile.FromFilePath(filePath);
                        System.Windows.Media.ImageSource fileIcon = shellFile.Thumbnail.ExtraLargeBitmapSource;

                        PocketedItems.Add(new PocketItem
                        {
                            FileName = domain,
                            FilePath = filePath,
                            Icon = fileIcon
                        });

                        // Update UI
                        StatusText.Visibility = Visibility.Collapsed;
                        FileIconContainer.Visibility = Visibility.Visible;
                        UpdateStackPreview();

                        CountText.Text = $"{PocketedItems.Count} Items";
                        if (ItemsListBox != null && ItemsListBox.SelectedItems.Count == 0)
                        {
                            PopupCountText.Text = $"{PocketedItems.Count} Items";
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not save URL: {ex.Message}");
                    }
                }
            }
        }

        // --- TRACKING THE CLICK (Preparing to drag a file out) ---
        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks on UI controls
            if (e.Source == CloseButton || e.Source == ExpandButton || e.Source == TopBar || e.Source == DragHandle)
                return;

            // Record the exact start point
            startPoint = e.GetPosition(null);
        }

        // --- THE MISSING PIECE: RESET ON RELEASE ---
        private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // THE FIX: As soon as you let go of the mouse, we "forget" the start point.
            // This prevents the "stuck" cursor and accidental dragging.
            startPoint = null;
        }

        // --- RELEASING THE FILES (Dragging Out) ---
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            // FIX: If the mouse isn't pressed, or we don't have a valid start point, 
            // or the pocket is empty, QUIT immediately.
            if (e.LeftButton != MouseButtonState.Pressed || startPoint == null || PocketedItems.Count == 0)
            {
                startPoint = null; // Extra safety reset
                return;
            }

            Point mousePos = e.GetPosition(null);
            Vector diff = (Point)startPoint - mousePos;

            // Only start if the mouse has moved significantly (Drag Threshold)
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                string[] pathsToDrag = new string[PocketedItems.Count];
                for (int i = 0; i < PocketedItems.Count; i++)
                {
                    pathsToDrag[i] = PocketedItems[i].FilePath;
                }

                DataObject dragData = new DataObject(DataFormats.FileDrop, pathsToDrag);

                // We set the startPoint to null BEFORE calling DoDragDrop
                // This breaks the loop so the cursor doesn't get "stuck"
                Point tempStart = (Point)startPoint;
                startPoint = null;

                // NEW: Turn the safety flag on before the drag, and off after
                _isDraggingFromApp = true;
                DragDropEffects result = DragDrop.DoDragDrop((DependencyObject)sender, dragData, DragDropEffects.Copy);
                _isDraggingFromApp = false;

                if (result != DragDropEffects.None)
                {
                    // Successful drop cleanup
                    PocketedItems.Clear();
                    StatusText.Visibility = Visibility.Visible;
                    FileIconContainer.Visibility = Visibility.Collapsed;
                    StackContainer.Children.Clear();
                    CountText.Text = "0 Items";
                    PopupCountText.Text = "0 Items";
                    ExpandButton.IsChecked = false;
                    // NEW: Also uncheck the box after a successful drag!
                    if (SelectAllCheckBox != null)
                        SelectAllCheckBox.IsChecked = false;
                }
            }
        }

        // --- UPDATE STACK PREVIEW ---
        // Rebuilds the card stack from scratch every time items change.
        // Bottom cards are oldest; top card is always the latest dropped item.
        private void UpdateStackPreview()
        {
            StackContainer.Children.Clear();

            int count = PocketedItems.Count;
            if (count == 0) return;

            // Rotation pattern — alternates sides, shrinks toward the top card.
            // Index 0 = bottom-most (oldest), last index = top (latest).
            // We cap visible cards at a reasonable spread; deeper cards reuse the last angle.
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

                // Add drop shadow only on the top card
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

        // --- ITEM LIST: TRACK CLICK ON A LIST ITEM ---
        private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Never trigger the background window drag from this area
            startPoint = null;

            // Find the ListBoxItem under the cursor
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && !(hit is ListBoxItem) && !(hit is ListBox))
                hit = VisualTreeHelper.GetParent(hit);

            if (hit is ListBoxItem lbi && lbi.IsSelected)
            {
                // Clicking any already-selected item → snapshot selection, arm drag,
                // and suppress the click so ListBox doesn't deselect anything.
                _dragCandidates = ItemsListBox.SelectedItems.Cast<PocketItem>().ToList();
                _listDragStart = e.GetPosition(null);
                e.Handled = true;
            }
            else if (hit is ListBoxItem)
            {
                // Clicking a single unselected item: let ListBox handle selection normally,
                // but arm a drag so a single-item drag also works.
                _dragCandidates = null;
                _listDragStart = e.GetPosition(null);
                // Do NOT set e.Handled — ListBox must update its selection first
            }
            else
            {
                // Click on empty space: let ListBox do rubber-band, don't arm drag
                _dragCandidates = null;
                _listDragStart = null;
            }
        }

        // --- ITEM LIST: DRAG ONLY SELECTED ITEMS OUT ---
        private void ItemsList_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _listDragStart == null)
            {
                _listDragStart = null;
                _dragCandidates = null;
                return;
            }

            Point pos = e.GetPosition(null);
            Vector diff = (Point)_listDragStart - pos;

            if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
                return;

            // Use snapshotted candidates for multi-select, or current selection for single
            var selectedItems = _dragCandidates
                ?? ItemsListBox.SelectedItems.Cast<PocketItem>().ToList();

            if (selectedItems.Count == 0)
            {
                _listDragStart = null;
                _dragCandidates = null;
                return;
            }

            string[] paths = selectedItems.Select(item => item.FilePath).ToArray();
            DataObject dragData = new DataObject(DataFormats.FileDrop, paths);
            _listDragStart = null;
            _dragCandidates = null;

            // NEW: Turn the safety flag on before the drag, and off after
            _isDraggingFromApp = true;
            DragDropEffects result = DragDrop.DoDragDrop(ItemsListBox, dragData, DragDropEffects.Copy);
            _isDraggingFromApp = false;

            if (result != DragDropEffects.None)
            {
                foreach (var item in selectedItems)
                    PocketedItems.Remove(item);

                if (PocketedItems.Count == 0)
                {
                    StatusText.Visibility = Visibility.Visible;
                    FileIconContainer.Visibility = Visibility.Collapsed;
                    StackContainer.Children.Clear();
                    ExpandButton.IsChecked = false;
                }
                else
                {
                    UpdateStackPreview();
                }

                CountText.Text = $"{PocketedItems.Count} Items";
                PopupCountText.Text = $"{PocketedItems.Count} Items";
            }
        }

        // --- DRAGGING THE WINDOW ---
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent the window from dragging if they actually clicked the 'X' button
            if (e.OriginalSource == CloseButton || e.Source == MoreButton)
                return;

            // Tell Windows to take over and drag the physical app window!
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // --- SMART POPUP PLACEMENT ---
        private CustomPopupPlacement[] Popup_PlacementCallback(Size popupSize, Size targetSize, Point offset)
        {
            // 1. Perfectly center X relative to the MainContainer
            double xNudge = -18;
            double xOffset = ((targetSize.Width - popupSize.Width) / 2.0) + xNudge;

            // 2. Plan A: Spawn BELOW the app
            double yBelow = targetSize.Height + 0;

            // 3. Plan B: Spawn ABOVE the app
            // Bumping this up to 40 fully clears the invisible 16px margin 
            // and the heavy downward drop shadow, matching the top visual gap.
            double yAbove = -popupSize.Height - 40;

            return new[]
            {
                new CustomPopupPlacement(new Point(xOffset, yBelow), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(xOffset, yAbove), PopupPrimaryAxis.Horizontal)
            };
        }

        // --- CLEANUP WHEN POPUP CLOSES ---
        private void Popup_Closed(object sender, EventArgs e)
        {
            // 1. Unselect all files so they don't stay highlighted
            ItemsListBox.UnselectAll();

            // 2. Ensure the toggle button visually unchecks if you closed the popup by clicking on the desktop
            ExpandButton.IsChecked = false;
        }

        // --- CLOSING THE WINDOW — clears items and hides ---
        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            // Clear all pocketed items
            PocketedItems.Clear();
            StackContainer.Children.Clear();
            StatusText.Visibility = Visibility.Visible;
            FileIconContainer.Visibility = Visibility.Collapsed;
            CountText.Text = "0 Items";
            PopupCountText.Text = "0 Items";
            ExpandButton.IsChecked = false;
            // NEW: Force the Select All checkbox to uncheck so it's fresh next time!
            if (SelectAllCheckBox != null)
                SelectAllCheckBox.IsChecked = false;

            HidePocketDrop();
        }

        // ══════════════════════════════════════════════════════
        // GLOBAL MOUSE HOOK CALLBACK
        // ══════════════════════════════════════════════════════
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                if (msg == WM_LBUTTONDOWN)
                {
                    _leftButtonHeld = true;
                    _lastX = hookStruct.pt.X;
                    _swingOriginX = hookStruct.pt.X;
                    _currentDir = 0;
                    _swingTimestamps.Clear();
                }
                else if (msg == WM_LBUTTONUP)
                {
                    _leftButtonHeld = false;
                    _currentDir = 0;
                    _swingTimestamps.Clear();
                }
                else if (msg == WM_MOUSEMOVE && _leftButtonHeld)
                {
                    int x = hookStruct.pt.X;
                    int dx = x - _lastX;
                    _lastX = x;

                    if (Math.Abs(dx) < 2) goto done; // ignore micro-jitter

                    int newDir = dx > 0 ? 1 : -1;

                    // Direction reversal detected
                    if (_currentDir != 0 && newDir != _currentDir)
                    {
                        int swingSize = Math.Abs(x - _swingOriginX);
                        if (swingSize >= MIN_SWING_PX)
                        {
                            long now = Environment.TickCount64;
                            _swingTimestamps.Enqueue(now);

                            // Evict old timestamps outside the shake window
                            while (_swingTimestamps.Count > 0 &&
                                   now - _swingTimestamps.Peek() > SHAKE_WINDOW_MS)
                                _swingTimestamps.Dequeue();

                            if (_swingTimestamps.Count >= REQUIRED_SWINGS)
                            {
                                _swingTimestamps.Clear();
                                _currentDir = 0;

                                // Marshal to UI thread — hook runs on a background thread
                                Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() =>
                                    ShowPocketDrop(hookStruct.pt.X, hookStruct.pt.Y)));
                            }
                        }
                        _swingOriginX = x;
                    }

                    if (newDir != 0) _currentDir = newDir;
                }
            }
        done:
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // ══════════════════════════════════════════════════════
        // SHOW / HIDE WITH ANIMATION
        // ══════════════════════════════════════════════════════
        private void ShowPocketDrop(int cursorX, int cursorY)
        {
            // Position near cursor, nudge away from screen edges
            double wx = cursorX - this.ActualWidth / 2;
            double wy = cursorY - this.ActualHeight - 20;

            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            wx = Math.Max(8, Math.Min(wx, screenW - this.ActualWidth - 8));
            wy = Math.Max(8, Math.Min(wy, screenH - this.ActualHeight - 8));

            this.Left = wx;
            this.Top = wy;
            this.IsHitTestVisible = true;

            // Fade + scale in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

            var scaleX = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var scaleY = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

            var st = new ScaleTransform(1, 1, this.ActualWidth / 2, this.ActualHeight / 2);
            MainContainer.RenderTransform = st;
            MainContainer.RenderTransformOrigin = new Point(0.5, 0.5);

            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            this.BeginAnimation(OpacityProperty, fadeIn);

            this.Activate();
        }

        private void HidePocketDrop()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOut.Completed += (s, e) => { this.IsHitTestVisible = false; };
            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        // --- SELECT ALL LOGIC ---
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

        // --- SELECTION CHANGED LOGIC (Updates Header Text) ---
        private void ItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int selectedCount = ItemsListBox.SelectedItems.Count;

            if (selectedCount > 0)
            {
                long totalBytes = 0;

                // Add up the file size of everything selected
                foreach (PocketItem item in ItemsListBox.SelectedItems)
                {
                    if (File.Exists(item.FilePath))
                    {
                        totalBytes += new FileInfo(item.FilePath).Length;
                    }
                }

                // Determine if we should say "file" or "files"
                string fileWord = selectedCount == 1 ? "file" : "files";

                // Update the text block with the new info!
                PopupCountText.Text = $"{selectedCount} {fileWord} selected  ({FormatBytes(totalBytes)})";
            }
            else
            {
                // If nothing is selected (or we unselected everything), revert to the total count
                PopupCountText.Text = $"{PocketedItems.Count} Items";
            }
        }

        // --- HELPER: Formats raw bytes into readable KB/MB/GB ---
        private string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };

            // Figure out the scale of the file size mathematically
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));

            // Round it to one decimal place (e.g., 4.2 MB)
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);

            return $"{num} {suffixes[place]}";
        }

        // --- MORE BUTTON DROPDOWN MENU ---
        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (MoreButton.ContextMenu != null)
            {
                // Align the menu directly under the 3-dots icon
                MoreButton.ContextMenu.PlacementTarget = MoreButton;
                MoreButton.ContextMenu.Placement = PlacementMode.Bottom;
                MoreButton.ContextMenu.VerticalOffset = 4;

                // Open it!
                MoreButton.ContextMenu.IsOpen = true;
            }
        }

        // --- MENU ACTION: Clear All ---
        private void Menu_ClearItems_Click(object sender, RoutedEventArgs e)
        {
            PocketedItems.Clear();
            StackContainer.Children.Clear();
            StatusText.Visibility = Visibility.Visible;
            FileIconContainer.Visibility = Visibility.Collapsed;
            CountText.Text = "0 Items";
            PopupCountText.Text = "0 Items";

            if (SelectAllCheckBox != null)
                SelectAllCheckBox.IsChecked = false;
        }

        // --- MENU ACTION: Open Temp Folder ---
        private void Menu_OpenTemp_Click(object sender, RoutedEventArgs e)
        {
            // Opens the Windows File Explorer directly to where your dragged URLs and web images are saved!
            System.Diagnostics.Process.Start("explorer.exe", Path.GetTempPath());
        }
    }

    // --- The blueprint for a dropped item ---
    public class PocketItem
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public System.Windows.Media.ImageSource Icon { get; set; }
    }
}