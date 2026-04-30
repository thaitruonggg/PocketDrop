// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PocketDrop
{
    public partial class AppPickerDialog : Window
    {
        // ================================================ //
        // 1. STATE & DATA (VARIABLES)
        // ================================================ //

        // Store final exe path string for settings output
        public string FinalExcludedAppsString { get; private set; } = "";

        // Store raw previously saved app string for parsing
        private string _existingApps;

        // ObservableCollection for automatic UI updates when items are added
        private ObservableCollection<AppItem> _allApps;


        // ================================================ //
        // 2. WINDOW LIFECYCLE (STARTUP)
        // ================================================ //
        public AppPickerDialog(string existingApps)
        {
            InitializeComponent();
            _existingApps = existingApps ?? "";

            LoadAppsAsync(); // Trigger app scan on window open
        }

        private async void LoadAppsAsync()
        {
            // 1. Run registry scan on background thread to prevent UI freeze, then save this into a temporary variable
            var scannedApps = await Task.Run(() => AppScanner.GetInstalledApps());

            // 2. Parse the saved exceptions list
            var savedList = _existingApps.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(a => a.Trim().ToLower())
                                         .ToList();

            // 3. Loop through the temporary list to check off any apps the user previously saved
            foreach (var app in scannedApps)
            {
                if (savedList.Contains(app.ExePath.ToLower()) ||
                    savedList.Contains(System.IO.Path.GetFileName(app.ExePath).ToLower()))
                {
                    app.IsSelected = true;
                }
            }

            // 4. Wrap the fully processed list into the ObservableCollection
            _allApps = new ObservableCollection<AppItem>(scannedApps);

            // Hide the loading text and show the grid
            LoadingPanel.Visibility = Visibility.Collapsed;
            AppListControl.ItemsSource = _allApps;
            AppListControl.Visibility = Visibility.Visible;
        }


        // ================================================ //
        // 3. UI EVENTS (CLICKS)
        // ================================================ //

        private void ManualBrowse_Click(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select an Application"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string path = openFileDialog.FileName;

                // Manually create AppItem for custom file entry
                var customApp = new AppItem
                {
                    AppName = System.IO.Path.GetFileNameWithoutExtension(path),
                    ExePath = path,
                    AppIcon = AppScanner.GetIconFromExe(path),
                    IsSelected = true // Auto-check it when the user adds it
                };

                _allApps.Insert(0, customApp); // Put it at the very top of the list
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Gather all the currently checked apps
            var checkedApps = _allApps.Where(a => a.IsSelected).Select(a => a.ExePath).ToList();

            // Join checked apps with newlines
            FinalExcludedAppsString = string.Join(Environment.NewLine, checkedApps);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}