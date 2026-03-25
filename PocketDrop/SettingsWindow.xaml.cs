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

namespace PocketDrop
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // ✨ THE FIX 1: Load the current state of the setting when the window opens!
            CopyItemToDestinationCheckbox.IsChecked = App.CopyItemToDestination;

            // ✨ THE FIX: Force the UI to display your actual saved keys!
            PocketKeyText.Text = App.PocketKeyChar;
            ClipboardKeyText.Text = App.ClipboardKeyChar;

            // Load Shake Settings
            ShakeToggle.IsChecked = App.EnableMouseShake;
            ShakeDistText.Text = App.ShakeMinimumDistance.ToString();
            GameModeCheck.IsChecked = App.DisableInGameMode;

            ExcludedAppsText.Text = App.ExcludedApps;

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
        }

        // ✨ THE FIX 2: Update the global setting when the user toggles the switch!
        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            App.CopyItemToDestination = CopyItemToDestinationCheckbox.IsChecked ?? true;
        }

        private void EditPocketKey_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dialog = new ShortcutDialog("New pocket shortcut", PocketKeyText.Text) { Owner = this };
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
            var dialog = new ShortcutDialog("New pocket from clipboard", ClipboardKeyText.Text) { Owner = this };
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

        private void ExcludedAppsText_TextChanged(object sender, TextChangedEventArgs e)
        {
            App.ExcludedApps = ExcludedAppsText.Text;
            App.SaveSettings();
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
    }
}
