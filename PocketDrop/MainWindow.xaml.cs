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
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;
using System.IO.Compression;

namespace PocketDrop
{
    // --- NATIVE INTEROP FOR WINDOWS 11 SHARE UI ---
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

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

        private static IntPtr _hookHandle = IntPtr.Zero;
        private static LowLevelMouseProc _hookProc; // Keep reference — prevents GC

        public bool IsGhost { get; set; } = false;

        // ══════════════════════════════════════════════════════
        // SHAKE DETECTION PARAMETERS
        // ══════════════════════════════════════════════════════
        private const int MIN_SWING_PX = 40;   // min horizontal pixels per swing direction
        private const int REQUIRED_SWINGS = 3;    // number of direction reversals needed
        private const int SHAKE_WINDOW_MS = 1000; // all reversals must happen within this ms window

        private static bool _leftButtonHeld = false;
        private static bool _hasSpawnedPocketThisDrag = false; // ✨ NEW: The Lockout Flag!
        private static int _lastX = 0;
        private static int _currentDir = 0;       // +1 = right, -1 = left, 0 = none
        private static int _swingOriginX = 0;       // X where current swing started
        private static readonly Queue<long> _swingTimestamps = new Queue<long>();

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

            // THE FIX: Only install the global hook ONCE for the entire application!
            if (_hookHandle == IntPtr.Zero)
            {
                _hookProc = HookCallback;
                using (var proc = Process.GetCurrentProcess())
                using (var mod = proc.MainModule)
                    _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(mod.ModuleName), 0);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // THE FIX: We no longer uninstall the hook here! 
            // The Master Listener stays alive until you right-click the tray and hit "Quit"
            base.OnClosed(e);
        }

        // --- DRAG HOVER EFFECTS ---
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                // ✨ Just turn the overlay on! No layout math required.
                DragGlowBorder.Visibility = Visibility.Visible;
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            // Turn it off when they drag away
            DragGlowBorder.Visibility = Visibility.Collapsed;
        }

        // --- CATCHING THE FILES (Dropping In) ---
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            // ✨ Instantly turn off the glow AND reset the margin when the file drops!
            DragGlowBorder.Visibility = Visibility.Collapsed;

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

                        // THE FIX: Push the heavy image downscaling to a background worker thread!
                        System.Windows.Media.ImageSource fileIcon = await System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                string ext = Path.GetExtension(filePath).ToLower();
                                string[] imageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

                                if (Array.Exists(imageExts, x => x == ext))
                                {
                                    BitmapImage img = new BitmapImage();
                                    img.BeginInit();
                                    img.CacheOption = BitmapCacheOption.OnLoad; // CRITICAL for background loading
                                    img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; // Bonus speed boost! Ignores heavy color correction profiles.
                                    img.UriSource = new Uri(filePath);
                                    img.DecodePixelWidth = 120;
                                    img.EndInit();

                                    // Freeze makes it read-only, allowing it to cross over to the UI thread!
                                    img.Freeze();
                                    return img;
                                }
                                else
                                {
                                    // Handle PDFs, text files, EXEs, etc.
                                    ShellFile shellFile = ShellFile.FromFilePath(filePath);

                                    // THE FIX: Request the Large icon instead of ExtraLarge to completely avoid the 256px padding bug
                                    var thumb = shellFile.Thumbnail.LargeBitmapSource;

                                    thumb.Freeze();
                                    return thumb;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Icon error: {ex.Message}");
                                return null;
                            }
                        });

                        var newItem = new PocketItem { FileName = fileName, FilePath = filePath, Icon = fileIcon };
                        PocketedItems.Add(newItem);

                        // ✨ INSTANT SYNC: Save it to the global list the millisecond it drops!
                        if (!App.SessionHistory.Exists(x => x.FilePath == newItem.FilePath))
                        {
                            App.SessionHistory.Add(newItem);
                        }
                    }

                    // Update UI after the items are loaded
                    StatusText.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;
                    UpdateStackPreview();

                    if (ItemsListBox == null || ItemsListBox.SelectedItems.Count == 0)
                    {
                        UpdateItemCountDisplay(PocketedItems.Count);
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

                        // THE FIX: Use LargeBitmapSource here too, and remove the cropper wrapper
                        var fileIcon = shellFile.Thumbnail.LargeBitmapSource;
                        fileIcon.Freeze();

                        var newUrlItem = new PocketItem { FileName = domain, FilePath = filePath, Icon = fileIcon };
                        PocketedItems.Add(newUrlItem);

                        // ✨ INSTANT SYNC for URLs!
                        if (!App.SessionHistory.Exists(x => x.FilePath == newUrlItem.FilePath))
                        {
                            App.SessionHistory.Add(newUrlItem);
                        }

                        // Update UI
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

                // ✨ THE MAGIC FIX: Force Windows Explorer to explicitly Copy or Cut!
                // 1 = Copy (Leaves original file), 2 = Move (Deletes original file)
                byte[] dropEffect = new byte[] { (byte)(App.CopyItemToDestination ? 1 : 2), 0, 0, 0 };
                dragData.SetData("Preferred DropEffect", new System.IO.MemoryStream(dropEffect));

                Point tempStart = (Point)startPoint;
                startPoint = null;

                _isDraggingFromApp = true;
                DragDropEffects allowedEffects = App.CopyItemToDestination ? DragDropEffects.Copy : DragDropEffects.Move;
                DragDropEffects result = DragDrop.DoDragDrop((DependencyObject)sender, dragData, allowedEffects);
                _isDraggingFromApp = false;

                // ✨ ALWAYS clear the pocket if the drop was successful, regardless of Copy or Move!
                // ✨ ALWAYS clear the pocket if the drop was successful, regardless of Copy or Move!
                if (result != DragDropEffects.None)
                {
                    foreach (var item in PocketedItems)
                    {
                        CleanupTempFile(item.FilePath);
                    }

                    PocketedItems.Clear();

                    // ✨ THE NEW FIX: Check if we should auto-close!
                    if (App.CloseWhenEmptied)
                    {
                        ExpandButton.IsChecked = false; // Ensure popup closes
                        ForceClose(); // Uses your built-in safe close method!
                    }
                    else
                    {
                        // Standard UI reset if they want it to stay open on screen
                        StackContainer.Children.Clear();
                        ExpandButton.IsChecked = false;
                        UpdateItemCountDisplay(0);

                        if (SelectAllCheckBox != null)
                            SelectAllCheckBox.IsChecked = false;
                    }
                }
            }

            // ✨ PING THE WINDOW: Tell the Saved Pockets window to update in real-time!
            var openHistoryWindow = Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();
            if (openHistoryWindow != null)
            {
                // We use Dispatcher here just in case the background image-loading thread tries to trigger it
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    openHistoryWindow.RefreshHistory();
                }));
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

            // ✨ THE MAGIC FIX: Force Windows Explorer to explicitly Copy or Cut!
            byte[] dropEffect = new byte[] { (byte)(App.CopyItemToDestination ? 1 : 2), 0, 0, 0 };
            dragData.SetData("Preferred DropEffect", new System.IO.MemoryStream(dropEffect));

            _listDragStart = null;
            _dragCandidates = null;

            _isDraggingFromApp = true;
            DragDropEffects allowedEffects = App.CopyItemToDestination ? DragDropEffects.Copy : DragDropEffects.Move;
            DragDropEffects result = DragDrop.DoDragDrop(ItemsListBox, dragData, allowedEffects);
            _isDraggingFromApp = false;

            // ✨ ALWAYS clear the selected items from the pocket if the drop was successful!
            // ✨ ALWAYS clear the selected items from the pocket if the drop was successful!
            if (result != DragDropEffects.None)
            {
                foreach (var item in selectedItems)
                {
                    CleanupTempFile(item.FilePath);
                    PocketedItems.Remove(item);
                }

                if (PocketedItems.Count == 0)
                {
                    // ✨ THE NEW FIX: Check if we should auto-close!
                    if (App.CloseWhenEmptied)
                    {
                        ExpandButton.IsChecked = false; // Ensure popup closes
                        ForceClose(); // Uses your built-in safe close method!
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
            // 1. Log all current items to the Global History
            foreach (var item in PocketedItems)
            {
                if (!App.SessionHistory.Exists(x => x.FilePath == item.FilePath))
                {
                    App.SessionHistory.Add(item);
                }
            }

            // 2. Ping the Saved Pockets window to update in real-time!
            var openHistoryWindow = Application.Current.Windows.OfType<SavedPocketsWindow>().FirstOrDefault();
            if (openHistoryWindow != null)
            {
                openHistoryWindow.RefreshHistory();
            }

            // 3. ✨ THE FIX: Play the animation, then let it close itself!
            bool isLastWindow = Application.Current.Windows.OfType<MainWindow>().Count() <= 1;
            HidePocketDrop(!isLastWindow);
        }

        // ══════════════════════════════════════════════════════
        // GLOBAL MOUSE HOOK CALLBACK
        // ══════════════════════════════════════════════════════
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                if (msg == WM_LBUTTONDOWN)
                {
                    _leftButtonHeld = true;
                    _hasSpawnedPocketThisDrag = false; // ✨ THE FIX: Reset the lock every time they click a new file!

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
                    // ✨ THE CHECKS: Abort if shaking is disabled, gaming, excluded app...
                    if (!App.EnableMouseShake) goto done;
                    if (App.DisableInGameMode && App.IsGameModeActive()) goto done;
                    if (App.IsForegroundAppExcluded()) goto done;

                    // ✨ THE FIX: If we already spawned a pocket during this specific drag, stop calculating shakes!
                    if (_hasSpawnedPocketThisDrag) goto done;

                    int x = hookStruct.pt.X;
                    int dx = x - _lastX;
                    _lastX = x;

                    if (Math.Abs(dx) < 2) goto done;

                    int newDir = dx > 0 ? 1 : -1;

                    if (_currentDir != 0 && newDir != _currentDir)
                    {
                        int swingSize = Math.Abs(x - _swingOriginX);

                        if (swingSize >= App.ShakeMinimumDistance)
                        {
                            long now = Environment.TickCount64;
                            _swingTimestamps.Enqueue(now);

                            while (_swingTimestamps.Count > 0 &&
                                   now - _swingTimestamps.Peek() > SHAKE_WINDOW_MS)
                                _swingTimestamps.Dequeue();

                            if (_swingTimestamps.Count >= REQUIRED_SWINGS)
                            {
                                _swingTimestamps.Clear();
                                _currentDir = 0;

                                _hasSpawnedPocketThisDrag = true; // ✨ THE FIX: Lock it down! No more pockets until they let go of the mouse.

                                // Spawn the window!
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
        public void ShowPocketDrop(int rawCursorX, int rawCursorY)
        {
            if (this.IsHitTestVisible) return;

            // ✨ THE NEW FIX: Apply the user's preferred layout mode before the UI calculates its size!
            // 0 = Grid view, 1 = List view
            this.CurrentViewMode = App.ItemsLayoutMode == 1 ? "List" : "Grid";

            this.UpdateLayout();

            // 1. Get the raw physical screen the mouse is currently on
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(rawCursorX, rawCursorY));
            var rawWorkArea = screen.WorkingArea;

            // ✨ THE NEW FIX: Calculate the exact Windows DPI Display Scaling!
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
                // Failsafe if the window is still waking up
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpiX = g.DpiX / 96.0;
                    dpiY = g.DpiY / 96.0;
                }
            }

            // 2. Convert raw physical pixels into WPF's custom DPI scale
            double workAreaLeft = rawWorkArea.Left / dpiX;
            double workAreaTop = rawWorkArea.Top / dpiY;
            double workAreaWidth = rawWorkArea.Width / dpiX;
            double workAreaHeight = rawWorkArea.Height / dpiY;
            double workAreaRight = workAreaLeft + workAreaWidth;
            double workAreaBottom = workAreaTop + workAreaHeight;

            double cursorX = rawCursorX / dpiX;
            double cursorY = rawCursorY / dpiY;

            // 3. Safely grab the window size
            double w = this.ActualWidth > 0 ? this.ActualWidth : (double.IsNaN(this.Width) ? 380 : this.Width);
            double h = this.ActualHeight > 0 ? this.ActualHeight : (double.IsNaN(this.Height) ? 500 : this.Height);

            // 4. Default position (Near Mouse)
            double targetLeft = cursorX - (w / 2) + 40;
            double targetTop = cursorY - h - 80;

            // Keep "Near Mouse" safely on screen
            targetLeft = Math.Max(workAreaLeft + 8, Math.Min(targetLeft, workAreaRight - w - 8));
            targetTop = Math.Max(workAreaTop + 8, Math.Min(targetTop, workAreaBottom - h - 8));

            // 5. Override based on the user's setting (NOW USING SCALED MATH)
            switch (App.PocketPlacement)
            {
                case 1: // Top edge
                    targetLeft = workAreaLeft + (workAreaWidth / 2) - (w / 2);
                    targetTop = workAreaTop + 8;
                    break;
                case 2: // Bottom edge
                    targetLeft = workAreaLeft + (workAreaWidth / 2) - (w / 2);
                    targetTop = workAreaBottom - h - 8;
                    break;
                case 3: // Left edge
                    targetLeft = workAreaLeft + 8;
                    targetTop = workAreaTop + (workAreaHeight / 2) - (h / 2);
                    break;
                case 4: // Right edge
                    targetLeft = workAreaRight - w - 8;
                    targetTop = workAreaTop + (workAreaHeight / 2) - (h / 2);
                    break;
                case 5: // Top left corner
                    targetLeft = workAreaLeft + 8;
                    targetTop = workAreaTop + 8;
                    break;
                case 6: // Top right corner
                    targetLeft = workAreaRight - w - 8;
                    targetTop = workAreaTop + 8;
                    break;
                case 7: // Bottom left corner
                    targetLeft = workAreaLeft + 8;
                    targetTop = workAreaBottom - h - 8;
                    break;
                case 8: // Bottom right corner
                    targetLeft = workAreaRight - w - 8;
                    targetTop = workAreaBottom - h - 8;
                    break;
            }

            // 6. Apply the final, perfectly scaled position!
            this.Left = targetLeft;
            this.Top = targetTop;
            this.IsHitTestVisible = true;

            // --- ANIMATIONS ---
            // 1. The Fade In
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

            // 2. The Bouncy Easing Function (Amplitude controls how much it overshoots and bounces back)
            var bounceEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

            // 3. The Scale and Slide animations
            var scaleAnim = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = bounceEase };
            var slideAnim = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = bounceEase };

            // 4. Combine the Transforms
            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(0.85, 0.85);
            var translateTransform = new TranslateTransform(0, 30);

            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);

            // 5. Apply it to the MainContainer (Ensure it scales from the direct center)
            MainContainer.RenderTransformOrigin = new Point(0.5, 0.5);
            MainContainer.RenderTransform = transformGroup;

            // 6. Fire them all at once!
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

        // ══════════════════════════════════════════════════════
        // HIDE WITH ANIMATION & BACKGROUND CLEANUP
        // ══════════════════════════════════════════════════════
        private void HidePocketDrop(bool closeWindow = false)
        {
            // Lock the window immediately so the user can't interact while it closes
            this.IsHitTestVisible = false;

            // 1. The Fade Out
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            // 2. The Shrink and Drop animations
            var scaleAnim = new DoubleAnimation(1, 0.85, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            var slideAnim = new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            // 3. Set up the transforms safely
            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(1, 1);
            var translateTransform = new TranslateTransform(0, 0);

            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);

            MainContainer.RenderTransformOrigin = new Point(0.5, 0.5);
            MainContainer.RenderTransform = transformGroup;

            // 4. ✨ THE FIX: Clean up the UI *only after* the animation completely finishes!
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

                // ✨ THE FIX: Destroy the window now that it's invisible!
                if (closeWindow)
                {
                    this.Close();
                }
            };

            // 5. Fire them all at once!
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
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
                foreach (PocketItem item in ItemsListBox.SelectedItems)
                {
                    if (File.Exists(item.FilePath))
                        totalBytes += new FileInfo(item.FilePath).Length;
                }

                // ✨ Use the dictionary to translate "file selected" vs "files selected"
                string fileWord = selectedCount == 1
                    ? (string)Application.Current.Resources["Text_FileSelected"]
                    : (string)Application.Current.Resources["Text_FilesSelected"];

                PopupCountText.Text = $"{selectedCount} {fileWord} ({FormatBytes(totalBytes)})";
            }
            else
            {
                UpdateItemCountDisplay(PocketedItems.Count);
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
            // NEW: Clean up temp files before clearing
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

        // --- MENU ACTION: Open Temp Folder ---
        private void Menu_OpenTemp_Click(object sender, RoutedEventArgs e)
        {
            // Opens the Windows File Explorer directly to where your dragged URLs and web images are saved!
            System.Diagnostics.Process.Start("explorer.exe", Path.GetTempPath());
        }

        // --- MENU ACTION: Settings ---
        private void Menu_Settings_Click(object sender, RoutedEventArgs e)
        {
            // Check if the settings window is already open so we don't spawn duplicates!
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

        // --- NATIVE WINDOWS APP PICKER API ---
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO poainfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENASINFO
        {
            public string pcszFile;
            public string pcszClass;
            public int oaUIAction;
        }

        // --- MENU ACTION: Open With ---
        private void Menu_OpenWith_Click(object sender, RoutedEventArgs e)
        {
            // 1. Determine which file to open 
            string targetFilePath = null;

            if (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
            {
                targetFilePath = ((PocketItem)ItemsListBox.SelectedItems[0]).FilePath;
            }
            else if (PocketedItems.Count > 0)
            {
                targetFilePath = PocketedItems[0].FilePath;
            }

            // 2. Trigger the native Windows app picker dialog
            if (!string.IsNullOrEmpty(targetFilePath) && File.Exists(targetFilePath))
            {
                // THE FIX: Push the heavy Windows Shell call to a background thread!
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        OPENASINFO info = new OPENASINFO();
                        info.pcszFile = targetFilePath;
                        info.pcszClass = null;
                        info.oaUIAction = 7;

                        // This now runs in the background, freeing up your UI instantly
                        SHOpenWithDialog(IntPtr.Zero, ref info);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not open file picker: {ex.Message}");
                    }
                });
            }
        }

        // Class-level variables to handle the share lifecycle
        private string _fileToSharePath;
        private DataTransferManager _shareManager;

        // --- MENU ACTION: Share ---
        private void Menu_Share_Click(object sender, RoutedEventArgs e)
        {
            if (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
                _fileToSharePath = ((PocketItem)ItemsListBox.SelectedItems[0]).FilePath;
            else if (PocketedItems.Count > 0)
                _fileToSharePath = PocketedItems[0].FilePath;

            if (!string.IsNullOrEmpty(_fileToSharePath) && File.Exists(_fileToSharePath))
            {
                try
                {
                    // 1. Get the exact Window Handle (HWND) of your WPF app
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;

                    // 2. Access the native Windows 11 Share factory
                    var factory = WinRT.ActivationFactory.Get("Windows.ApplicationModel.DataTransfer.DataTransferManager");

                    // THE FIX: Extract the raw COM pointer and create a standard .NET COM Wrapper
                    // This perfectly bypasses the ObjectReference error!
                    var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);

                    // 3. Get the Share Manager assigned to THIS specific window
                    Guid guid = Guid.Parse("a5caee9b-8708-49d1-8d36-67d25a8da00c");
                    IntPtr ptr = interop.GetForWindow(hwnd, ref guid);
                    _shareManager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);

                    // 4. Attach our file to the payload and show the UI!
                    _shareManager.DataRequested += ShareManager_DataRequested;
                    interop.ShowShareUIForWindow(hwnd);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Share error: {ex.Message}");
                }
            }
        }

        // --- BUILDING THE SHARE PAYLOAD ---
        private async void ShareManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            // A deferral tells Windows to keep the Share UI spinning for a millisecond while we load the file from the hard drive
            DataRequestDeferral deferral = args.Request.GetDeferral();

            try
            {
                args.Request.Data.Properties.Title = "Sharing from PocketDrop";

                // Convert the standard physical file path into a modern Windows Storage File
                StorageFile file = await StorageFile.GetFileFromPathAsync(_fileToSharePath);
                args.Request.Data.SetStorageItems(new[] { file });
            }
            finally
            {
                // Unhook the event so it doesn't fire twice next time, and release the loading spinner
                _shareManager.DataRequested -= ShareManager_DataRequested;
                deferral.Complete();
            }
        }

        // --- NATIVE EXPLORER HIGHLIGHT API ---
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern void ILFree(IntPtr pidlList);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr ILCreateFromPathW(string pszPath);

        // ✨ NEW: Extracts the relative file pointer from an absolute path
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern IntPtr ILFindLastID(IntPtr pidl);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, uint dwFlags);

        // --- NATIVE WINDOW FOCUS API ---
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // --- MENU ACTION: Compress to ZIP ---
        private async void Menu_CompressZip_Click(object sender, RoutedEventArgs e)
        {
            // 1. Determine what to compress (Selected items, or ALL items if none are selected)
            var itemsToCompress = (ItemsListBox != null && ItemsListBox.SelectedItems.Count > 0)
                ? ItemsListBox.SelectedItems.Cast<PocketItem>().ToList()
                : PocketedItems.ToList();

            if (itemsToCompress.Count == 0) return;

            // 2. Ask the user where they want to save the new ZIP archive
            Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save ZIP Archive",
                FileName = "PocketDrop_Archive",
                DefaultExt = ".zip",
                Filter = "ZIP Archives (*.zip)|*.zip"
            };

            if (saveDialog.ShowDialog() == true)
            {
                string zipPath = saveDialog.FileName;

                // 3. Compress in the background so the UI doesn't stutter!
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // Delete the file if it already exists so we can cleanly overwrite it
                        if (File.Exists(zipPath)) File.Delete(zipPath);

                        using (FileStream zipToOpen = new FileStream(zipPath, FileMode.Create))
                        using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                        {
                            foreach (var item in itemsToCompress)
                            {
                                if (File.Exists(item.FilePath))
                                {
                                    // Add each physical file to the ZIP payload
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

                // 4. THE ULTIMATE FIX: Search existing Windows 11 Explorer Tabs!
                try
                {
                    Type shellType = Type.GetTypeFromProgID("Shell.Application");
                    dynamic shell = Activator.CreateInstance(shellType);
                    bool windowFound = false;

                    // Convert our standard path (C:\...) to a URI format (file:///C:/...) which Explorer tabs use internally
                    string targetUri = new Uri(Path.GetDirectoryName(zipPath)).AbsoluteUri;

                    // Check every single currently open Explorer window and tab
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

                                // Force the existing Explorer window to pop to the front of your screen!
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
        }

        // --- HELPER: Safely delete temporary files ---
        private void CleanupTempFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    // SAFETY CHECK: Only delete the file if it lives in the Temp folder!
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

        // --- NEW: Handle pasting files directly from the Windows Clipboard ---
        public async void PasteFromClipboard()
        {
            try
            {
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var files = System.Windows.Clipboard.GetFileDropList();
                    string[] fileArray = new string[files.Count];
                    files.CopyTo(fileArray, 0);

                    // Loop through the clipboard files and process them exactly like a drag-and-drop!
                    foreach (string filePath in fileArray)
                    {
                        string fileName = Path.GetFileName(filePath);

                        System.Windows.Media.ImageSource fileIcon = await System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                string ext = Path.GetExtension(filePath).ToLower();
                                string[] imageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

                                if (Array.Exists(imageExts, x => x == ext))
                                {
                                    System.Windows.Media.Imaging.BitmapImage img = new System.Windows.Media.Imaging.BitmapImage();
                                    img.BeginInit();
                                    img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                    img.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
                                    img.UriSource = new Uri(filePath);
                                    img.DecodePixelWidth = 120;
                                    img.EndInit();
                                    img.Freeze();
                                    return img;
                                }
                                else
                                {
                                    // Handle PDFs, text files, EXEs, etc. using the bug-free Large icon
                                    Microsoft.WindowsAPICodePack.Shell.ShellFile shellFile = Microsoft.WindowsAPICodePack.Shell.ShellFile.FromFilePath(filePath);
                                    var thumb = shellFile.Thumbnail.LargeBitmapSource;
                                    thumb.Freeze();
                                    return thumb;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Icon error: {ex.Message}");
                                return null;
                            }
                        });

                        // ✨ THE FIX: Create the item, add it to the pocket, AND save it globally
                        var newItem = new PocketItem { FileName = fileName, FilePath = filePath, Icon = fileIcon };
                        PocketedItems.Add(newItem);

                        // ✨ INSTANT SYNC: Save it to the global list the millisecond it pastes!
                        if (!App.SessionHistory.Exists(x => x.FilePath == newItem.FilePath))
                        {
                            App.SessionHistory.Add(newItem);
                        }
                    }

                    // Update UI after the items are loaded
                    StatusText.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;
                    UpdateStackPreview();

                    UpdateItemCountDisplay(PocketedItems.Count);

                    // ✨ PING THE WINDOW: Tell the Saved Pockets window to update in real-time!
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
                    // Optional: You can remove this else block entirely if you want it to fail silently!
                    MessageBox.Show("No files found in clipboard!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Clipboard error: {ex.Message}");
            }
        }

        // --- SAFE KILL SWITCH: Clears and closes the window from the outside ---
        public void ForceClose()
        {
            IsGhost = true;

            // ✨ THE FIX: Let HidePocketDrop handle all the cleanup and closing!
            bool isLastWindow = Application.Current.Windows.OfType<MainWindow>().Count() <= 1;
            HidePocketDrop(!isLastWindow);
        }

        // --- NEW: Syncs the pocket UI with the global history ---
        public void RefreshPocketUI()
        {
            // 1. Find items in this pocket that no longer exist in the global history
            var itemsToRemove = PocketedItems.Where(p => !App.SessionHistory.Exists(h => h.FilePath == p.FilePath)).ToList();

            if (itemsToRemove.Count == 0) return; // Nothing to sync!

            // 2. Remove the deleted files from this pocket's local memory
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
                // If there are still files left, just redraw the card stack!
                UpdateStackPreview();
                UpdateItemCountDisplay(PocketedItems.Count);
            }
        }

        // --- HELPER: Updates text and translates dynamically ---
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