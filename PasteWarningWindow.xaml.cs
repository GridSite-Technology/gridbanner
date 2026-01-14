using System;
using System.Windows;
using System.Windows.Forms;

namespace GridBanner
{
    public partial class PasteWarningWindow : Window
    {
        public PasteWarningWindow()
        {
            InitializeComponent();
            Loaded += PasteWarningWindow_Loaded;
        }

        private void PasteWarningWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Center window on screen
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen != null)
            {
                Left = (screen.WorkingArea.Width - Width) / 2;
                Top = (screen.WorkingArea.Height - Height) / 2;
            }
        }

        public void SetWarningInfo(SensitivityInfo source, SensitivityInfo destination)
        {
            SourceInfo.Text = $"Level: {source.Level}\nSource: {source.Source}\nLabel: {source.LabelName}";
            DestinationInfo.Text = $"Level: {destination.Level}\nSource: {destination.Source}\nLabel: {destination.LabelName}";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
