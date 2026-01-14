using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;

namespace GridBanner
{
    public partial class SystemLockdownAlertWindow : Window, INotifyPropertyChanged
    {
        private string _header = string.Empty;
        private string _message = string.Empty;
        private Brush _backgroundBrush = Brushes.DarkRed;
        private Brush _foregroundBrush = Brushes.White;

        public event PropertyChangedEventHandler? PropertyChanged;
        public Action? OnExitLockdown { get; set; }

        public string Header
        {
            get => _header;
            set => Set(ref _header, value);
        }

        public string Message
        {
            get => _message;
            set => Set(ref _message, value);
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

        public SystemLockdownAlertWindow()
        {
            InitializeComponent();
            DataContext = this;
            
            // Prevent window from being closed via Alt+F4 or X button
            Closing += (sender, e) =>
            {
                // Only allow closing if ExitLockdown is called
                if (!_allowClose)
                {
                    e.Cancel = true;
                }
            };
        }

        private bool _allowClose = false;

        public void SetScreen(Screen screen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = screen.Bounds.X;
            Top = screen.Bounds.Y;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;
            Topmost = true;
        }

        public void Apply(AlertMessage alert, Brush background, Brush foreground)
        {
            Header = alert.Summary; // No "SYSTEM LOCKDOWN:" prefix
            Message = alert.Message;
            BackgroundBrush = background;
            ForegroundBrush = foreground;
        }

        public void ExitLockdown()
        {
            _allowClose = true;
            Hide();
            OnExitLockdown?.Invoke();
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
