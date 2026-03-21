using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PocketDrop
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
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

        public MainWindow()
        {
            InitializeComponent();

            // This tells the window to use itself for data binding (crucial for Phase 2's UI)
            this.DataContext = this;
        }

        // --- CATCHING THE FILES (Dropping In) ---
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (droppedFiles != null && droppedFiles.Length > 0)
                {
                    // Loop through EVERY file dropped
                    foreach (string filePath in droppedFiles)
                    {
                        string fileName = Path.GetFileName(filePath);
                        System.Windows.Media.ImageSource fileIcon = null;

                        try
                        {
                            string ext = Path.GetExtension(filePath).ToLower();
                            string[] imageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

                            if (Array.Exists(imageExts, x => x == ext))
                            {
                                // Load real image in full quality
                                BitmapImage img = new BitmapImage();
                                img.BeginInit();
                                img.UriSource = new Uri(filePath);
                                img.CacheOption = BitmapCacheOption.OnLoad;
                                img.EndInit();
                                fileIcon = img;
                            }
                            else
                            {
                                // Load Windows icon
                                ShellFile shellFile = ShellFile.FromFilePath(filePath);
                                fileIcon = shellFile.Thumbnail.ExtraLargeBitmapSource;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not load icon for {fileName}: {ex.Message}");
                        }

                        // Package the file up and add it to our new list!
                        PocketedItems.Add(new PocketItem
                        {
                            FileName = fileName,
                            FilePath = filePath,
                            Icon = fileIcon
                        });
                    }

                    // Update the simple UI elements
                    StatusText.Visibility = Visibility.Collapsed;
                    FileIconContainer.Visibility = Visibility.Visible;

                    // Update the stacked card preview
                    UpdateStackPreview();

                    // Update the button text and the popup header text
                    CountText.Text = $"{PocketedItems.Count} Items";
                    PopupCountText.Text = $"{PocketedItems.Count} Items";
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

                DragDropEffects result = DragDrop.DoDragDrop((DependencyObject)sender, dragData, DragDropEffects.Copy);

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

        // --- DRAGGING THE WINDOW ---
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent the window from dragging if they actually clicked the 'X' button
            if (e.OriginalSource == CloseButton)
                return;

            // Tell Windows to take over and drag the physical app window!
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // --- CLOSING THE WINDOW ---
        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            this.Close();
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