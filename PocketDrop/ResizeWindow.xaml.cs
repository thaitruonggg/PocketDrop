// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace PocketDrop
{
    public enum ImageResizeMode { Fit, Fill, Stretch }
    public enum ImageResizeUnit { Pixels, Percentages, Centimeters }

    public partial class ResizeWindow : Window
    {
        public double TargetWidth { get; private set; }
        public double TargetHeight { get; private set; }
        public ImageResizeMode SelectedMode { get; private set; }
        public ImageResizeUnit SelectedUnit { get; private set; }

        public ResizeWindow()
        {
            InitializeComponent();
        }

        private void NumberValidation(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            double.TryParse(WidthInput.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double w);
            double.TryParse(HeightInput.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double h);

            if (w > 0 || h > 0)
            {
                TargetWidth = w;
                TargetHeight = h;
                SelectedMode = (ImageResizeMode)ModeCombo.SelectedIndex;
                SelectedUnit = (ImageResizeUnit)UnitCombo.SelectedIndex;

                this.DialogResult = true;
                this.Close();
            }
            else
            {
                string title = (string)Application.Current.TryFindResource("Text_InvalidInputTitle") ?? "Invalid Input";
                string message = (string)Application.Current.TryFindResource("Text_InvalidInputMsg") ?? "Please enter at least one valid number.";

                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}