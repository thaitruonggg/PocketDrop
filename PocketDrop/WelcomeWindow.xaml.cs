// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

namespace PocketDrop
{
    public partial class WelcomeWindow : Window
    {

        // ================================================ //
        // 1. STATE & DATA (VARIABLES)
        // ================================================ //

        private int _currentStep = 1;


        // ================================================ //
        // 2. WINDOW LIFECYCLE
        // ================================================ //
        public WelcomeWindow()
        {
            InitializeComponent();
            UpdateUI();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            CleanUpMediaElements(this);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }


        // ================================================ //
        // 3. UI EVENTS & NAVIGATION
        // ================================================ //

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


        // ================================================ //
        // 4. THE LAZY LOADING ENGINE
        // ================================================ //
        private void UpdateUI()
        {
            Step1Container.Visibility = Visibility.Collapsed;
            Step2Container.Visibility = Visibility.Collapsed;
            Step3Container.Visibility = Visibility.Collapsed;
            Step4Container.Visibility = Visibility.Collapsed;
            Step5Container.Visibility = Visibility.Collapsed;
            Step6Container.Visibility = Visibility.Collapsed;

            BackBtn.Visibility = _currentStep == 1 ? Visibility.Collapsed : Visibility.Visible;

            Brush inactiveColor = (Brush)new BrushConverter().ConvertFrom("#D9D9D9");
            Brush activeColor = (Brush)new BrushConverter().ConvertFrom("#dd2c2f");

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

            Video1.Close();
            Video2.Close();
            Video3.Close();
            Video4.Close();
            Video5.Close();
            Video6.Close();

            Video1.Source = null;
            Video2.Source = null;
            Video3.Source = null;
            Video4.Source = null;
            Video5.Source = null;
            Video6.Source = null;

            switch (_currentStep)
            {
                case 1:
                    Step1Container.Visibility = Visibility.Visible;
                    Dot1.Background = activeColor;
                    Dot1.Width = 24;
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video1.Source = new Uri("pack://siteoforigin:,,,/Assets/v1.mp4");
                    Video1.Play();
                    break;
                case 2:
                    Step2Container.Visibility = Visibility.Visible;
                    Dot2.Background = activeColor;
                    Dot2.Width = 24;
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video2.Source = new Uri("pack://siteoforigin:,,,/Assets/v2.mp4");
                    Video2.Play();
                    break;
                case 3:
                    Step3Container.Visibility = Visibility.Visible;
                    Dot3.Background = activeColor;
                    Dot3.Width = 24;
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video3.Source = new Uri("pack://siteoforigin:,,,/Assets/v3.mp4");
                    Video3.Play();
                    break;
                case 4:
                    Step4Container.Visibility = Visibility.Visible;
                    Dot4.Background = activeColor;
                    Dot4.Width = 24;
                    ActionBtnText.SetResourceReference(TextBlock.TextProperty, "Text_ButtonNext");
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Visible;
                    Video4.Source = new Uri("pack://siteoforigin:,,,/Assets/v4.mp4");
                    Video4.Play();
                    break;
                case 5:
                    Step5Container.Visibility = Visibility.Visible;
                    Dot5.Background = activeColor;
                    Dot5.Width = 24;
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
                    if (ActionBtnIcon != null) ActionBtnIcon.Visibility = Visibility.Collapsed;
                    Video6.Source = new Uri("pack://siteoforigin:,,,/Assets/v6.mp4");
                    Video6.Play();
                    break;
            }
        }


        // ================================================ //
        // 5. VIDEO & MEDIA MANAGEMENT
        // ================================================ //
        private void MediaElement_Loaded(object sender, RoutedEventArgs e)
        { }

        // Keeps your videos looping infinitely
        private void LoopingMediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement media)
            {
                media.Position = TimeSpan.Zero;
                media.Play();
            }
        }

        private void CleanUpMediaElements(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is MediaElement media)
                {
                    media.MediaEnded -= LoopingMediaElement_MediaEnded;
                    media.Loaded -= MediaElement_Loaded;

                    media.Stop();
                    media.Source = null;
                    media.Close();
                }
                else
                {
                    CleanUpMediaElements(child);
                }
            }
        }


        // ================================================ //
        // 6. LANGUAGE MANAGEMENT
        // ================================================ //
        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return;

            if (LanguageCombo.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedLanguageCode = selectedItem.Tag.ToString();

                string dictPath = selectedLanguageCode == "English"
                    ? "Languages/Strings.en.xaml"
                    : "Languages/Strings.vi.xaml";

                ResourceDictionary newLangDict = new ResourceDictionary
                {
                    Source = new Uri(dictPath, UriKind.Relative)
                };

                var appResources = Application.Current.Resources.MergedDictionaries;

                for (int i = appResources.Count - 1; i >= 0; i--)
                {
                    if (appResources[i].Source != null && appResources[i].Source.OriginalString.Contains("Languages/"))
                    {
                        appResources.RemoveAt(i);
                    }
                }

                appResources.Add(newLangDict);
            }
        }  
    }
}