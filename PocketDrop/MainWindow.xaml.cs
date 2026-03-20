using System;
using System.IO;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
// Add the new NuGet package reference
using Microsoft.WindowsAPICodePack.Shell;
using System.IO;

namespace PocketDrop
{
    public partial class MainWindow : Window
    {
        private string[] pocketedFiles = null;
        private System.Windows.Point startPoint;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                pocketedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (pocketedFiles != null && pocketedFiles.Length > 0)
                {
                    string firstFilePath = pocketedFiles[0];
                    string fileName = Path.GetFileName(firstFilePath);

                    try
                    {
                        // Check if the file is an actual image type
                        string ext = Path.GetExtension(firstFilePath).ToLower();
                        string[] imageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

                        if (Array.Exists(imageExts, x => x == ext))
                        {
                            // IF IT IS AN IMAGE: Load the real file directly in crisp HD
                            BitmapImage img = new BitmapImage();
                            img.BeginInit();
                            img.UriSource = new Uri(firstFilePath);
                            img.CacheOption = BitmapCacheOption.OnLoad;

                            // REMOVED the DecodePixelWidth line so it loads the full quality!

                            img.EndInit();

                            FileIcon.Source = img;
                        }
                        else
                        {
                            // IF IT IS A DOC/APP: Use the Windows Shell icon
                            ShellFile shellFile = ShellFile.FromFilePath(firstFilePath);
                            FileIcon.Source = shellFile.Thumbnail.ExtraLargeBitmapSource;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not load icon: {ex.Message}");
                    }

                    StatusText.Visibility = Visibility.Collapsed;
                    FileIcon.Visibility = Visibility.Visible;

                    if (fileName.Length > 15)
                        CountItem.Content = fileName.Substring(0, 15) + "...";
                    else
                        CountItem.Content = fileName;
                }
            }
        }

        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Do not start a file drag if the user clicks the close button, dropdown, or the top drag bar
            if (e.Source == CloseButton || e.Source == ItemsDropdown || e.Source == TopBar || e.Source == DragHandle)
                return;

            startPoint = e.GetPosition(null);
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || pocketedFiles == null)
                return;

            System.Windows.Point mousePos = e.GetPosition(null);
            Vector diff = startPoint - mousePos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DataObject dragData = new DataObject(DataFormats.FileDrop, pocketedFiles);
                DragDrop.DoDragDrop(MainContainer, dragData, DragDropEffects.Copy);

                pocketedFiles = null;
                StatusText.Visibility = Visibility.Visible;
                FileIcon.Visibility = Visibility.Collapsed;
                FileIcon.Source = null;
                CountItem.Content = "0 items";
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

        private void CloseButton_Click(object sender, MouseButtonEventArgs e)
        {
            this.Close();
        }
    }
}