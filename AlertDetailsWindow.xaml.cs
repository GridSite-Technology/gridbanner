using System.Windows;
using System.Windows.Media;

namespace GridBanner
{
    public partial class AlertDetailsWindow : Window
    {
        public string Header { get; }
        public string Message { get; }
        public Brush BackgroundBrush { get; }
        public Brush ForegroundBrush { get; }

        public AlertDetailsWindow(AlertMessage alert, Brush background, Brush foreground)
        {
            InitializeComponent();
            Header = $"{alert.Level.ToString().ToUpperInvariant()}: {alert.Summary}";
            Message = alert.Message;
            BackgroundBrush = background;
            ForegroundBrush = foreground;
            DataContext = this;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}


