using System.Windows;
using System.Windows.Input;

namespace PocketDrop
{
    public partial class ShortcutDialog : Window
    {
        public uint SelectedVK { get; private set; } // The raw system code for the key
        public string SelectedLetter { get; private set; } // The readable letter

        public ShortcutDialog(string title, string currentLetter)
        {
            InitializeComponent();
            TitleText.Text = title;
            TargetKeyText.Text = currentLetter;
            SelectedLetter = currentLetter;

            // Convert the starting letter into its system code fallback
            SelectedVK = (uint)KeyInterop.VirtualKeyFromKey((Key)System.Enum.Parse(typeof(Key), currentLetter));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Instantly grab keyboard focus so the user doesn't have to click anything to start typing
            this.Focus();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Only allow the user to bind standard A-Z letters
            if (e.Key >= Key.A && e.Key <= Key.Z)
            {
                SelectedLetter = e.Key.ToString();
                TargetKeyText.Text = SelectedLetter;

                // Get the native Windows Virtual Key code needed for the global hotkey API
                SelectedVK = (uint)KeyInterop.VirtualKeyFromKey(e.Key);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // Returns true to let us know they hit Save
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}