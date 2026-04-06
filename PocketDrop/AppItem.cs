using System.Windows.Media;

namespace PocketDrop
{
    public class AppItem
    {
        public string AppName { get; set; }
        public string ExePath { get; set; }
        public ImageSource AppIcon { get; set; }
        public bool IsSelected { get; set; } // Tracks if the user checked the box
    }
}