// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using System.ComponentModel;
using System.Windows.Media;

namespace PocketDrop
{
    public class PocketItem : INotifyPropertyChanged
    {
        private string _fileName;
        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileName)));
            }
        }

        // Stores the file's original source path
        private string _originalFilePath;
        public string OriginalFilePath => _originalFilePath;

        private string _filePath;
        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;

                // Automatically capture the original path on first assignment
                if (string.IsNullOrEmpty(_originalFilePath)) _originalFilePath = value;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath)));
            }
        }

        public bool IsPinned { get; set; }
        public bool IsSnippet { get; set; } = false;

        private ImageSource _icon;
        public ImageSource Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
