using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;

namespace GridBanner
{
    public partial class AlertBarWindow : Window, INotifyPropertyChanged
    {
        private string _summary = string.Empty;
        private Brush _backgroundBrush = Brushes.Red;
        private Brush _foregroundBrush = Brushes.White;
        private Visibility _dismissVisibility = Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Summary
        {
            get => _summary;
            set => Set(ref _summary, value);
        }

        public Brush BackgroundBrush
        {
            get => _backgroundBrush;
            set => Set(ref _backgroundBrush, value);
        }

        public Brush ForegroundBrush
        {
            get => _foregroundBrush;
            set => Set(ref _foregroundBrush, value);
        }

        public Visibility DismissVisibility
        {
            get => _dismissVisibility;
            set => Set(ref _dismissVisibility, value);
        }

        public AlertMessage? CurrentAlert { get; private set; }

        public Action? OnDismissRequested { get; set; }

        public AlertBarWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void SetScreen(Screen screen, double height)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = screen.Bounds.X;
            Top = screen.Bounds.Y;
            Width = screen.Bounds.Width;
            Height = Math.Max(20, height);
            Topmost = true;
        }

        public void ApplyAlert(AlertMessage alert, Brush background, Brush foreground, bool showDismiss)
        {
            CurrentAlert = alert;
            Summary = $"{alert.Level.ToString().ToUpperInvariant()}: {alert.Summary}";
            BackgroundBrush = background;
            ForegroundBrush = foreground;
            DismissVisibility = showDismiss ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            OnDismissRequested?.Invoke();
        }

        private void ViewDetails_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentAlert == null)
            {
                return;
            }

            var dlg = new AlertDetailsWindow(CurrentAlert, BackgroundBrush, ForegroundBrush)
            {
                Owner = this
            };
            dlg.ShowDialog();
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value))
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}


