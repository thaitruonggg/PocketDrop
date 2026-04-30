// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Collections.Generic;

namespace PocketDrop
{
    public partial class ShortcutDialog : Window
    {

        // ================================================ //
        // 1. STATE & VARIABLES
        // ================================================ //

        public uint SelectedVK { get; private set; }
        public string SelectedLetter { get; private set; }
        public uint SelectedModifiers { get; private set; }

        // Track key press order chronologically
        private List<string> _pressedModifiers = new List<string>();
        private List<string> _displayOrder = new List<string>();

        private string _originalLetter;
        private uint _originalModifiers;
        private string _defaultLetter;
        private uint _defaultModifiers;

        // ================================================ //
        // 2. LIFECYCLE (STARTUP)
        // ================================================ //

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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
        }


        // ================================================ //
        // 3. KEYBOARD INTERCEPTION ENGINE
        // ================================================ //

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

                if (orderedMods != null && orderedMods.Count > 0)
                {
                    _displayOrder = new List<string>(orderedMods);
                }
                else
                {
                    _displayOrder.Clear();
                    if ((mods & AppHelpers.MOD_WIN) != 0) _displayOrder.Add("Win");
                    if ((mods & AppHelpers.MOD_CTRL) != 0) _displayOrder.Add("Ctrl");
                    if ((mods & AppHelpers.MOD_ALT) != 0) _displayOrder.Add("Alt");
                    if ((mods & AppHelpers.MOD_SHIFT) != 0) _displayOrder.Add("Shift");
                }
            }
            RenderKeycaps();
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
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= AppHelpers.MOD_ALT;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods |= AppHelpers.MOD_CTRL;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= AppHelpers.MOD_SHIFT;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= AppHelpers.MOD_WIN;

            // 3. Clean up the tracking list if a key was released
            if ((mods & AppHelpers.MOD_WIN) == 0) _pressedModifiers.Remove("Win");
            if ((mods & AppHelpers.MOD_CTRL) == 0) _pressedModifiers.Remove("Ctrl");
            if ((mods & AppHelpers.MOD_ALT) == 0) _pressedModifiers.Remove("Alt");
            if ((mods & AppHelpers.MOD_SHIFT) == 0) _pressedModifiers.Remove("Shift");

            if (isModifier)
            {
                _displayOrder = new List<string>(_pressedModifiers);
                SelectedLetter = ""; // Clear previous letter input while modifier keys are held
                RenderKeycaps();     // Draw just the modifiers
                e.Handled = true;    // Stop the event
                return;
            }

            // 4. Catch the final letter
            if (keyToUse >= Key.A && keyToUse <= Key.Z)
            {
                // Enforce at least one modifier
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

            // Erase pending letter from screen when modifier released before input
            if (keyToUse == Key.LWin || keyToUse == Key.RWin) _pressedModifiers.Remove("Win");
            if (keyToUse == Key.LeftCtrl || keyToUse == Key.RightCtrl) _pressedModifiers.Remove("Ctrl");
            if (keyToUse == Key.LeftAlt || keyToUse == Key.RightAlt) _pressedModifiers.Remove("Alt");
            if (keyToUse == Key.LeftShift || keyToUse == Key.RightShift) _pressedModifiers.Remove("Shift");

            // Skip UI update if final letter already locked in
            if (string.IsNullOrEmpty(SelectedLetter))
            {
                _displayOrder = new List<string>(_pressedModifiers);

                // Return to blank state when all keys released
                if (_displayOrder.Count == 0)
                {
                    SelectedLetter = "_";
                }

                RenderKeycaps();
            }
        }


        // ================================================ //
        // 4. UI ACTIONS & COMMANDS
        // ================================================ //

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

            // Close immediately if no changes were made
            if (SelectedLetter == _originalLetter && SelectedModifiers == _originalModifiers)
            {
                this.DialogResult = true;
                this.Close();
                return;
            }

            // Check for hotkey conflicts by registering dummy test ID with OS
            bool success = AppHelpers.RegisterHotKey(IntPtr.Zero, 9999, SelectedModifiers, SelectedVK);
            if (!success)
            {
                string msg = (string)Application.Current.Resources["Text_MsgConflictDesc"];
                string title = (string)Application.Current.Resources["Text_MsgConflictTitle"];
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
                return; // Stop from saving!
            }

            // Unregister test hotkey and close dialog on success
            AppHelpers.UnregisterHotKey(IntPtr.Zero, 9999);

            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }


        // ================================================ //
        // 5. UI DYNAMIC RENDERING
        // ================================================ //

        private void RenderKeycaps()
        {
            DialogKeysContainer.Children.Clear();
            if (SelectedLetter == "_") return;

            foreach (string mod in _displayOrder)
            {
                AddKeycap(mod);
            }

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