using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Net.Http;
using System.Threading.Tasks;

namespace PocketDrop
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // ✨ ADD THIS LINE RIGHT HERE, OUTSIDE OF ANY METHODS!
        private bool _isLanguageLoaded = false;
        public SettingsWindow()
        {
            InitializeComponent();

            // ✨ THE FIX 1: Load the current state of the setting when the window opens!
            CopyItemToDestinationCheckbox.IsChecked = App.CopyItemToDestination;

            StartupToggle.IsChecked = IsRunAtStartupEnabled();

            // ✨ THE FIX: Force the UI to display your actual saved keys!
            PocketKeyText.Text = App.PocketKeyChar;
            ClipboardKeyText.Text = App.ClipboardKeyChar;

            // Load Shake Settings
            ShakeToggle.IsChecked = App.EnableMouseShake;
            ShakeDistText.Text = App.ShakeMinimumDistance.ToString();
            GameModeCheck.IsChecked = App.DisableInGameMode;

            RefreshExcludedAppsDisplay();

            PlacementCombo.SelectedIndex = App.PocketPlacement;

            LayoutCombo.SelectedIndex = App.ItemsLayoutMode;

            CloseEmptiedToggle.IsChecked = App.CloseWhenEmptied;

            // Grab the text-based Informational Version
            var versionAttr = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                as System.Reflection.AssemblyInformationalVersionAttribute[];

            if (versionAttr != null && versionAttr.Length > 0)
            {
                // ✨ THE FIX: Chop off the Git hash at the '+' symbol
                string cleanVersion = versionAttr[0].InformationalVersion.Split('+')[0].Replace("-beta", " Beta ");

                // Update the UI text!
                AppVersionText.Text = $"Version {cleanVersion}";
            }
            else
            {
                AppVersionText.Text = "Version 1.0.0";
            }

            // <--- Add the Theme load here! --->
            ThemeCombo.SelectedIndex = App.AppTheme;
            ApplyTheme(App.AppTheme);

            // ✨ ADD THIS TO SYNC THE UI WITH THE SAVED LANGUAGE
            if (App.AppLanguage == "Vietnamese")
            {
                LanguageCombo.SelectedIndex = 1;
            }
            else
            {
                LanguageCombo.SelectedIndex = 0;
            }
            // Mark as loaded so the event doesn't trigger during window creation
            _isLanguageLoaded = true;

            // ✨ SMART SYNC: Check if the background startup scanner already found an update!
            if (App.UpdateAvailable)
            {
                CheckUpdateBtn.Content = "Update Available!";
                // Optional: Make it green to grab their attention!
                CheckUpdateBtn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
            }

        }

        // ══════════════════════════════════════════════════════
        // THEME ENGINE
        // ══════════════════════════════════════════════════════

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeCombo != null && this.IsLoaded)
            {
                App.AppTheme = ThemeCombo.SelectedIndex;
                App.SaveSettings();
                ApplyTheme(ThemeCombo.SelectedIndex);
            }
        }

        private void ApplyTheme(int themeIndex)
        {
            bool useDarkMode = themeIndex == 0 ? IsWindowsInDarkMode() : themeIndex == 2;
            string themeFileName = useDarkMode ? "DarkTheme.xaml" : "LightTheme.xaml";
            Uri themeUri = new Uri($"pack://application:,,,/PocketDrop;component/Themes/{themeFileName}");

            var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;

            // 1. Create and add the new theme at the very end (so it safely overrides everything)
            var newThemeDict = new ResourceDictionary { Source = themeUri };
            dictionaries.Add(newThemeDict);

            // 2. Find ALL old theme files and remove them to prevent conflicts and memory leaks!
            var oldThemes = new List<ResourceDictionary>();
            foreach (var dict in dictionaries)
            {
                if (dict != newThemeDict && dict.Source != null && dict.Source.ToString().Contains("Theme.xaml"))
                {
                    oldThemes.Add(dict);
                }
            }

            foreach (var oldTheme in oldThemes)
            {
                dictionaries.Remove(oldTheme);
            }
        }

        // Helper to ask Windows 11 what theme the user's OS is currently using
        private bool IsWindowsInDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") != null)
                    {
                        // Windows uses 0 for Dark Mode, and 1 for Light Mode
                        return (int)key.GetValue("AppsUseLightTheme") == 0;
                    }
                }
            }
            catch { }

            return false; // Safe fallback
        }

        // ══════════════════════════════════════════════════════
        // LANGUAGE ENGINE
        // ══════════════════════════════════════════════════════

        private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLanguageLoaded) return;

            var selectedBox = (ComboBoxItem)LanguageCombo.SelectedItem;
            string selectedLanguage = selectedBox.Content.ToString();

            string dictPath = selectedLanguage == "Vietnamese"
                ? "pack://application:,,,/PocketDrop;component/Languages/Strings.vi.xaml"
                : "pack://application:,,,/PocketDrop;component/Languages/Strings.en.xaml";

            // Save choice
            App.AppLanguage = selectedLanguage == "Vietnamese" ? "Vietnamese" : "English";
            App.SaveSettings();

            var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;

            // 1. Create and add the new language at the very end
            var newLangDict = new ResourceDictionary { Source = new Uri(dictPath) };
            dictionaries.Add(newLangDict);

            // 2. Clean up any old language files sitting in memory
            var oldLangs = new List<ResourceDictionary>();
            foreach (var dict in dictionaries)
            {
                if (dict != newLangDict && dict.Source != null && dict.Source.ToString().Contains("Strings."))
                {
                    oldLangs.Add(dict);
                }
            }

            foreach (var oldLang in oldLangs)
            {
                dictionaries.Remove(oldLang);
            }

            // ✨ THE FIX: Wait 50 milliseconds to let WPF finish loading the dictionary into memory!
            await System.Threading.Tasks.Task.Delay(50);

            // Now tell the tray menu to fetch the words
            App.UpdateTrayMenuLanguage();
        }

        // ✨ THE FIX 2: Update the global setting when the user toggles the switch!
        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            App.CopyItemToDestination = CopyItemToDestinationCheckbox.IsChecked ?? true;
        }

        // ══════════════════════════════════════════════════════
        // STARTUP ENGINE
        // ══════════════════════════════════════════════════════

        private void StartupToggle_Click(object sender, RoutedEventArgs e)
        {
            bool enable = StartupToggle.IsChecked ?? false;
            SetRunAtStartup(enable);
        }

        private bool IsRunAtStartupEnabled()
        {
            try
            {
                // Open the standard Windows Startup registry folder
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        // Check if PocketDrop is already in there
                        object value = key.GetValue("PocketDrop");
                        return value != null;
                    }
                }
            }
            catch { }
            return false;
        }

        private void SetRunAtStartup(bool enable)
        {
            try
            {
                // Open the registry folder with write access (true)
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            // Get the exact location of the PocketDrop.exe file on their PC
                            string exePath = Environment.ProcessPath;

                            // Wrap the path in quotes to protect against spaces in folder names
                            key.SetValue("PocketDrop", $"\"{exePath}\"");
                        }
                        else
                        {
                            // Remove it from the startup list
                            key.DeleteValue("PocketDrop", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not update startup settings: {ex.Message}", "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Snap the toggle back to its previous state if it failed
                StartupToggle.IsChecked = !enable;
            }
        }

        private void EditPocketKey_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // ✨ THE FIX: Dynamically read the translated text from memory!
            string dialogTitle = (string)this.FindResource("Text_NewPocketShortcut");

            var dialog = new ShortcutDialog(dialogTitle, PocketKeyText.Text) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                PocketKeyText.Text = dialog.SelectedLetter;
                App.PocketKeyChar = dialog.SelectedLetter;
                App.PocketKeyVK = dialog.SelectedVK;
                App.ReloadHotkeys();
            }
        }

        private void EditClipboardKey_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // ✨ THE FIX: Dynamically read the translated text from memory!
            string dialogTitle = (string)this.FindResource("Text_ClipboardShortcut");

            var dialog = new ShortcutDialog(dialogTitle, ClipboardKeyText.Text) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                ClipboardKeyText.Text = dialog.SelectedLetter;
                App.ClipboardKeyChar = dialog.SelectedLetter;
                App.ClipboardKeyVK = dialog.SelectedVK;
                App.ReloadHotkeys();
            }
        }

        private void ShakeToggle_Click(object sender, RoutedEventArgs e)
        {
            App.EnableMouseShake = ShakeToggle.IsChecked ?? true;
            App.SaveSettings();
        }

        // --- COMMIT TEXT BOX ON ENTER ---
        private void ShakeDistText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // This removes the blinking cursor and drops focus, 
                // essentially telling the app "I am done typing, save this!"
                Keyboard.ClearFocus();
            }
        }

        private void GameModeCheck_Click(object sender, RoutedEventArgs e)
        {
            App.DisableInGameMode = GameModeCheck.IsChecked ?? true;
            App.SaveSettings();
        }

        private void ShakeDistText_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only save if they typed a valid number
            if (int.TryParse(ShakeDistText.Text, out int dist))
            {
                App.ShakeMinimumDistance = dist;
                App.SaveSettings();
            }
        }

        private void OpenAppPicker_Click(object sender, RoutedEventArgs e)
        {
            // Open the dialog and pass in the currently saved apps
            var dialog = new AppPickerDialog(App.ExcludedApps) { Owner = this };

            // If they clicked "Save"...
            if (dialog.ShowDialog() == true)
            {
                // Save the new string to your global settings
                App.ExcludedApps = dialog.FinalExcludedAppsString;
                App.SaveSettings();

                // Refresh the UI only if changes were actually saved!
                RefreshExcludedAppsDisplay();
            }
        }

        private async void RefreshExcludedAppsDisplay()
        {
            if (string.IsNullOrWhiteSpace(App.ExcludedApps))
            {
                ExcludedAppsEmptyText.Visibility = Visibility.Visible;
                ExcludedAppsIconDisplay.ItemsSource = null;
                return;
            }

            ExcludedAppsEmptyText.Visibility = Visibility.Collapsed;

            // Grab the raw paths from settings
            var paths = App.ExcludedApps
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToList();

            // Run the icon extraction on a background thread!
            var displayItems = await System.Threading.Tasks.Task.Run(() =>
            {
                var list = new List<AppItem>();
                foreach (var path in paths)
                {
                    list.Add(new AppItem
                    {
                        AppName = System.IO.Path.GetFileNameWithoutExtension(path),
                        // This calls our existing scanner which safely Freezes the image!
                        AppIcon = AppScanner.GetIconFromExe(path)
                    });
                }
                return list;
            });

            // Feed the resulting list of AppItems to our XAML grid
            ExcludedAppsIconDisplay.ItemsSource = displayItems;
        }

        private void PlacementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlacementCombo != null && this.IsLoaded)
            {
                App.PocketPlacement = PlacementCombo.SelectedIndex;
                App.SaveSettings();
            }
        }

        private void LayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LayoutCombo != null && this.IsLoaded)
            {
                App.ItemsLayoutMode = LayoutCombo.SelectedIndex;
                App.SaveSettings();
            }
        }

        private void CloseEmptiedToggle_Click(object sender, RoutedEventArgs e)
        {
            App.CloseWhenEmptied = CloseEmptiedToggle.IsChecked ?? true;
            App.SaveSettings();
        }

        // ══════════════════════════════════════════════════════
        // ABOUT SECTION LINKS
        // ══════════════════════════════════════════════════════

        private void PrivacyPolicy_Click(object sender, MouseButtonEventArgs e)
        {
            // Show the popup overlay!
            PrivacyOverlay.Visibility = Visibility.Visible;
        }

        private void ClosePrivacy_Click(object sender, RoutedEventArgs e)
        {
            // Hide the popup overlay!
            PrivacyOverlay.Visibility = Visibility.Collapsed;
        }

        private void ThirdParty_Click(object sender, MouseButtonEventArgs e)
        {
            // Show the Licenses popup overlay!
            LicenseOverlay.Visibility = Visibility.Visible;
        }

        private void CloseLicense_Click(object sender, RoutedEventArgs e)
        {
            // Hide the Licenses popup overlay!
            LicenseOverlay.Visibility = Visibility.Collapsed;
        }

        private void Rate_Click(object sender, MouseButtonEventArgs e)
        {
            // Opens the Windows Store directly to your app (replace with your actual Store ID later)
            OpenUrl("ms-windows-store://review/?ProductId=YOUR_APP_ID");
        }

        private void GetHelp_Click(object sender, MouseButtonEventArgs e)
        {
            // The large "Get Help" card can also point straight to your GitHub Issues!
            OpenUrl("https://github.com/naofunyan/PocketDrop/issues");
        }

        // Helper method to safely launch URLs in the user's default browser
        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        // --- OPEN GITHUB WHEN THEY CLICK UPDATE ---
        // --- HYBRID UPDATE CHECKER ---
        private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            // ✨ 1. If the background scanner already found one, just open the download page!
            if (App.UpdateAvailable)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = App.UpdateUrl,
                    UseShellExecute = true
                });
                return; // Stop the code here so we don't scan again
            }

            // ✨ 2. Otherwise, run the manual network scan!
            CheckUpdateBtn.IsEnabled = false;
            CheckUpdateBtn.Content = "Checking...";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

                    string url = "https://raw.githubusercontent.com/naofunyan/PocketDrop/main/version.txt";
                    string latestVersionString = await client.GetStringAsync(url);
                    latestVersionString = latestVersionString.Trim();

                    string currentVersionString = "1.0.0";

                    if (Version.TryParse(currentVersionString, out Version current) &&
                        Version.TryParse(latestVersionString, out Version latest))
                    {
                        if (latest > current)
                        {
                            // Mark the global flag so the app remembers for next time!
                            App.UpdateAvailable = true;
                            App.UpdateUrl = "https://github.com/naofunyan/PocketDrop/releases/latest";

                            MessageBoxResult result = MessageBox.Show(
                                $"A new version of PocketDrop ({latestVersionString}) is available!\n\nWould you like to download it now?",
                                "Update Available",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Information);

                            if (result == MessageBoxResult.Yes)
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = App.UpdateUrl,
                                    UseShellExecute = true
                                });
                            }
                            else
                            {
                                // If they click 'No', turn the button green so they can grab it later
                                CheckUpdateBtn.Content = "Update Available!";
                                CheckUpdateBtn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
                            }
                        }
                        else
                        {
                            MessageBox.Show("You are already using the latest version of PocketDrop.", "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch
            {
                MessageBox.Show("Could not connect to the update server. Please check your internet connection and try again.", "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                // Only reset the button back to normal if an update WASN'T found
                if (!App.UpdateAvailable)
                {
                    CheckUpdateBtn.IsEnabled = true;
                    CheckUpdateBtn.Content = "Check for updates";
                }
                else
                {
                    CheckUpdateBtn.IsEnabled = true; // Keep it clickable!
                }
            }
        }
    }
}
