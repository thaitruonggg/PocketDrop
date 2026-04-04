using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PocketDrop
{
    public partial class ShortcutDialog : Window
    {
        // OS API for the Conflict Tester
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public uint SelectedVK { get; private set; }
        public string SelectedLetter { get; private set; }
        public uint SelectedModifiers { get; private set; }

        // ✨ NEW: Track chronological order of physical key presses
        private List<string> _pressedModifiers = new List<string>();
        private List<string> _displayOrder = new List<string>();

        private string _originalLetter;
        private uint _originalModifiers;
        private string _defaultLetter;
        private uint _defaultModifiers;

        public ShortcutDialog(string title, string currentLetter, uint currentMods, string defaultLetter, uint defaultMods)
        {
            InitializeComponent();
            TitleText.Text = title;

            _originalLetter = currentLetter;
            _originalModifiers = currentMods;
            _defaultLetter = defaultLetter;
            _defaultModifiers = defaultMods;

            SetKey(currentLetter, currentMods);
        }

        // ✨ Add the optional "orderedMods" parameter
        private void SetKey(string letter, uint mods, List<string> orderedMods = null)
        {
            if (string.IsNullOrEmpty(letter) || letter == "_")
            {
                SelectedLetter = "_";
                SelectedVK = 0;
                SelectedModifiers = 0;
                _displayOrder.Clear();
            }
            else
            {
                SelectedLetter = letter;
                SelectedModifiers = mods;
                SelectedVK = (uint)KeyInterop.VirtualKeyFromKey((Key)System.Enum.Parse(typeof(Key), letter));

                // Save the exact chronological visual order!
                if (orderedMods != null && orderedMods.Count > 0)
                {
                    _displayOrder = new List<string>(orderedMods);
                }
                else
                {
                    // Fallback order (used when loading the window from Settings)
                    _displayOrder.Clear();
                    if ((mods & App.MOD_WIN) != 0) _displayOrder.Add("Win");
                    if ((mods & App.MOD_CTRL) != 0) _displayOrder.Add("Ctrl");
                    if ((mods & App.MOD_ALT) != 0) _displayOrder.Add("Alt");
                    if ((mods & App.MOD_SHIFT) != 0) _displayOrder.Add("Shift");
                }
            }
            RenderKeycaps();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key keyToUse = e.Key == Key.System ? e.SystemKey : e.Key;

            // 1. Check if the pressed key is a modifier
            bool isModifier = false;
            if (keyToUse == Key.LWin || keyToUse == Key.RWin) { if (!_pressedModifiers.Contains("Win")) _pressedModifiers.Add("Win"); isModifier = true; }
            else if (keyToUse == Key.LeftCtrl || keyToUse == Key.RightCtrl) { if (!_pressedModifiers.Contains("Ctrl")) _pressedModifiers.Add("Ctrl"); isModifier = true; }
            else if (keyToUse == Key.LeftAlt || keyToUse == Key.RightAlt) { if (!_pressedModifiers.Contains("Alt")) _pressedModifiers.Add("Alt"); isModifier = true; }
            else if (keyToUse == Key.LeftShift || keyToUse == Key.RightShift) { if (!_pressedModifiers.Contains("Shift")) _pressedModifiers.Add("Shift"); isModifier = true; }

            // 2. Gather all currently held modifiers
            uint mods = 0;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= App.MOD_ALT;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods |= App.MOD_CTRL;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= App.MOD_SHIFT;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= App.MOD_WIN;

            // 3. Clean up the tracking list if a key was released
            if ((mods & App.MOD_WIN) == 0) _pressedModifiers.Remove("Win");
            if ((mods & App.MOD_CTRL) == 0) _pressedModifiers.Remove("Ctrl");
            if ((mods & App.MOD_ALT) == 0) _pressedModifiers.Remove("Alt");
            if ((mods & App.MOD_SHIFT) == 0) _pressedModifiers.Remove("Shift");

            // ✨ THE FIX: If the user just pressed or released a modifier, draw it instantly!
            if (isModifier)
            {
                _displayOrder = new List<string>(_pressedModifiers);
                SelectedLetter = ""; // Clear any previous letter while they hold modifiers
                RenderKeycaps();     // Draw just the modifiers!
                e.Handled = true;    // Stop the event
                return;
            }

            // 4. Catch the final letter
            if (keyToUse >= Key.A && keyToUse <= Key.Z)
            {
                // Enforce at least one modifier!
                if (mods == 0)
                {
                    string msg = (string)Application.Current.Resources["Text_MsgNeedModifier"];
                    string title = (string)Application.Current.Resources["Text_MsgInvalidShortcutTitle"];
                    MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);
                    _pressedModifiers.Clear();
                    SelectedLetter = "_";
                    RenderKeycaps();
                    return;
                }

                // Save and lock in the final shortcut
                SetKey(keyToUse.ToString(), mods, _pressedModifiers);
                e.Handled = true;
                _pressedModifiers.Clear(); // Reset for the next time the user tries
            }
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            Key keyToUse = e.Key == Key.System ? e.SystemKey : e.Key;

            // If they release a modifier before locking in a letter, we need to erase it from the screen
            if (keyToUse == Key.LWin || keyToUse == Key.RWin) _pressedModifiers.Remove("Win");
            if (keyToUse == Key.LeftCtrl || keyToUse == Key.RightCtrl) _pressedModifiers.Remove("Ctrl");
            if (keyToUse == Key.LeftAlt || keyToUse == Key.RightAlt) _pressedModifiers.Remove("Alt");
            if (keyToUse == Key.LeftShift || keyToUse == Key.RightShift) _pressedModifiers.Remove("Shift");

            // Only update the UI if they haven't locked in a final letter yet
            if (string.IsNullOrEmpty(SelectedLetter))
            {
                _displayOrder = new List<string>(_pressedModifiers);

                // If they let go of everything, return to the blank state
                if (_displayOrder.Count == 0)
                {
                    SelectedLetter = "_";
                }

                RenderKeycaps();
            }
        }

        private void Reset_Click(object sender, MouseButtonEventArgs e)
        {
            SetKey(_defaultLetter, _defaultModifiers);
        }

        private void Clear_Click(object sender, MouseButtonEventArgs e)
        {
            SetKey("_", 0);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedLetter == "_")
            {
                string msg = (string)Application.Current.Resources["Text_MsgNeedValidKey"];
                string title = (string)Application.Current.Resources["Text_MsgInvalidShortcutTitle"];
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // If they didn't actually change anything, just close immediately!
            if (SelectedLetter == _originalLetter && SelectedModifiers == _originalModifiers)
            {
                this.DialogResult = true;
                this.Close();
                return;
            }

            // ✨ CONFLICT CHECK! Try to register it with the OS using a dummy ID (9999)
            bool success = RegisterHotKey(IntPtr.Zero, 9999, SelectedModifiers, SelectedVK);
            if (!success)
            {
                string msg = (string)Application.Current.Resources["Text_MsgConflictDesc"];
                string title = (string)Application.Current.Resources["Text_MsgConflictTitle"];
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
                return; // Stop them from saving!
            }

            // It worked! Unregister the test so the main app can claim it, and close the dialog
            UnregisterHotKey(IntPtr.Zero, 9999);

            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // --- DYNAMIC KEYCAP GENERATOR FOR THE DIALOG ---
        private void RenderKeycaps()
        {
            DialogKeysContainer.Children.Clear();
            if (SelectedLetter == "_") return;

            // ✨ 1. Render the modifiers in real-time
            foreach (string mod in _displayOrder)
            {
                AddKeycap(mod);
            }

            // ✨ 2. THE FIX: Only draw the final blue square if a letter has actually been pressed!
            if (!string.IsNullOrEmpty(SelectedLetter))
            {
                AddKeycap(SelectedLetter);
            }
        }

        private void AddKeycap(string text)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0, 95, 184)),
                CornerRadius = new CornerRadius(4),
                // ✨ FIXED: Explicitly declaring Left, Top, Right, Bottom
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = 48
            };

            if (text == "Win")
            {
                border.Child = new Path
                {
                    Data = Geometry.Parse("M3 3H11V11H3V3ZM13 3H21V11H13V3ZM3 13H11V21H3V13ZM13 13H21V21H13V13Z"),
                    Fill = Brushes.White,
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                border.Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            DialogKeysContainer.Children.Add(border);
        }
    }
}