// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using System;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PocketDrop
{
    public partial class CustomDialog : Window
    {
        // Public property to hold the user's choice
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.No;

        public CustomDialog(string message, string title)
        {
            InitializeComponent();

            // Set the dynamic text passed in from the main window
            this.Title = title;
            MessageText.Text = message;

            // Grab the native Windows Warning Icon and convert it for WPF
            try
            {
                var iconHandle = SystemIcons.Warning.Handle;
                DialogIcon.Source = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            catch
            {
                // Silently fail if Windows refuses to yield the icon (it will just be blank)
            }
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            this.Close(); // Close the popup
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            this.Close(); // Close the popup
        }
    }
}