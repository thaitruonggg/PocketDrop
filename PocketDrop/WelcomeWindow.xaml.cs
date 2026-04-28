using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

namespace PocketDrop
{
    public partial class WelcomeWindow : Window
    {
        private int _currentStep = 1;

        public WelcomeWindow()
        {
            InitializeComponent();
            UpdateUI();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 6)
            {
                _currentStep++;
                UpdateUI();
            }
            else
            {
                this.Close();
            }
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            // 1. Hide all steps first
            Step1Container.Visibility = Visibility.Collapsed;
            Step2Container.Visibility = Visibility.Collapsed;
            Step3Container.Visibility = Visibility.Collapsed;
            Step4Container.Visibility = Visibility.Collapsed;
            Step5Container.Visibility = Visibility.Collapsed;
            Step6Container.Visibility = Visibility.Collapsed;

            // Hide the "Back" button if we are on the first screen
            BackBtn.Visibility = _currentStep == 1 ? Visibility.Collapsed : Visibility.Visible;

            Brush inactiveColor = (Brush)new BrushConverter().ConvertFrom("#D9D9D9");
            Brush activeColor = (Brush)new BrushConverter().ConvertFrom("#dd2c2f");

            // Reset all dots to inactive color and small width
            Dot1.Background = inactiveColor;
            Dot2.Background = inactiveColor;
            Dot3.Background = inactiveColor;
            Dot4.Background = inactiveColor;
            Dot5.Background = inactiveColor;
            Dot6.Background = inactiveColor;

            Dot1.Width = 8;
            Dot2.Width = 8;
            Dot3.Width = 8;
            Dot4.Width = 8;
            Dot5.Width = 8;
            Dot6.Width = 8;

            // ✨ THE LAZY LOADING ENGINE ✨

            // 1. Explicitly force Windows Media Foundation to destroy the unmanaged decoders
            Video1.Close();
            Video2.Close();
            Video3.Close();
            Video4.Close();
            Video5.Close();
            Video6.Close();
            // 1. Instantly sever the connection to all videos. 
            // This forces WMF to dump the unmanaged buffers from RAM!
            Video1.Source = null;
            Video2.Source = null;
            Video3.Source = null;
            Video4.Source = null;
            Video5.Source = null;
            Video6.Source = null;

            // 2. Only inject the source into the active step, and play it!
            switch (_currentStep)
            {
                case 1:
                    Step1Container.Visibility = Visibility.Visible;
                    Dot1.Background = activeColor;
                    Dot1.Width = 24; // Expand the pill!
                    // ✨ THE FIX: Actively bind to the dynamic resource instead of a static string
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video1.Source = new Uri("pack://siteoforigin:,,,/Assets/v1.mp4");
                    Video1.Play();
                    break;
                case 2:
                    Step2Container.Visibility = Visibility.Visible;
                    Dot2.Background = activeColor;
                    Dot2.Width = 24; // Expand the pill!
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video2.Source = new Uri("pack://siteoforigin:,,,/Assets/v2.mp4");
                    Video2.Play();
                    break;
                case 3:
                    Step3Container.Visibility = Visibility.Visible;
                    Dot3.Background = activeColor;
                    Dot3.Width = 24; // Expand the pill!
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video3.Source = new Uri("pack://siteoforigin:,,,/Assets/v3.mp4");
                    Video3.Play();
                    break;
                case 4:
                    Step4Container.Visibility = Visibility.Visible;
                    Dot4.Background = activeColor;
                    Dot4.Width = 24; // Expand the pill!
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video4.Source = new Uri("pack://siteoforigin:,,,/Assets/v4.mp4");
                    Video4.Play();
                    break;
                case 5:
                    Step5Container.Visibility = Visibility.Visible;
                    Dot5.Background = activeColor;
                    Dot5.Width = 24; // Expand the pill!
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video5.Source = new Uri("pack://siteoforigin:,,,/Assets/v5.mp4");
                    Video5.Play();
                    break;
                case 6:
                    Step6Container.Visibility = Visibility.Visible;
                    Dot6.Background = activeColor;
                    Dot6.Width = 24;
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonGetStarted");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Collapsed; // Hide arrow on last step
                    Video6.Source = new Uri("pack://siteoforigin:,,,/Assets/v6.mp4");
                    Video6.Play();
                    break;
            }
        }

        // ✨ FIX 1: Stop videos from blindly auto-playing in the background
        private void MediaElement_Loaded(object sender, RoutedEventArgs e)
        {
            // Empty! We are taking manual control now.
        }

        // Keeps your videos looping infinitely
        private void LoopingMediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement media)
            {
                media.Position = TimeSpan.Zero;
                media.Play();
            }
        }

        // We will wire this up to your DynamicResource dictionaries later!
        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Prevents firing during window initialization before everything is loaded
            if (!this.IsLoaded) return;

            if (LanguageCombo.SelectedItem is ComboBoxItem selectedItem)
            {
                // ✨ THE FIX: Read the hidden 'Tag' property instead of the visible 'Content'
                string selectedLanguageCode = selectedItem.Tag.ToString();

                // 1. Determine which dictionary file to load based on the Tag
                string dictPath = selectedLanguageCode == "English"
                    ? "Languages/Strings.en.xaml"
                    : "Languages/Strings.vi.xaml";

                // 2. Load the new language dictionary into memory
                ResourceDictionary newLangDict = new ResourceDictionary
                {
                    Source = new Uri(dictPath, UriKind.Relative)
                };

                // 3. Get the global application resources
                var appResources = Application.Current.Resources.MergedDictionaries;

                // 4. Safely remove ONLY the old language dictionary. 
                // We loop backward so we don't break the index as we remove items.
                for (int i = appResources.Count - 1; i >= 0; i--)
                {
                    if (appResources[i].Source != null && appResources[i].Source.OriginalString.Contains("Languages/"))
                    {
                        appResources.RemoveAt(i);
                    }
                }

                // 5. Inject the new language. 
                // Because you used {DynamicResource} in your XAML, every text block will update instantly!
                appResources.Add(newLangDict);
            }
        }

        // ✨ NEW: Triggered automatically the exact moment the window closes
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // 1. Hunt down all 6 MediaElements and nuke their unmanaged buffers
            CleanUpMediaElements(this);

            // 2. The Brute Force Drop (Optional but highly recommended here)
            // Force the Garbage Collector to instantly flush the 1.1GB of RAM back to Windows
            // rather than lazily waiting for the app to need it later.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // ✨ NEW: A helper that scans the window looking for video players to destroy
        private void CleanUpMediaElements(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is MediaElement media)
                {
                    // Unhook events to prevent ghost triggers
                    media.MediaEnded -= LoopingMediaElement_MediaEnded;
                    media.Loaded -= MediaElement_Loaded;

                    // Stop playback, clear the source, and explicitly close the COM object
                    media.Stop();
                    media.Source = null;
                    media.Close();
                }
                else
                {
                    // If it's a grid or border, look inside it
                    CleanUpMediaElements(child);
                }
            }
        }

        // ✨ FIX 3: Helpers to scan the UI and control the videos
        private void PauseAllVideos(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is MediaElement media) media.Pause();
                else PauseAllVideos(child);
            }
        }

        private void PlayVideoInContainer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is MediaElement media)
                {
                    media.Position = TimeSpan.Zero; // Rewind perfectly to the start!
                    media.Play();
                }
                else PlayVideoInContainer(child);
            }
        }
    }
}