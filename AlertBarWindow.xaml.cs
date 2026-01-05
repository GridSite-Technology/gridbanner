using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;
using System.Windows.Media.Animation;

namespace GridBanner
{
    public partial class AlertBarWindow : Window, INotifyPropertyChanged
    {
        private string _summary = string.Empty;
        private Brush _backgroundBrush = Brushes.Red;
        private Brush _foregroundBrush = Brushes.White;
        private Visibility _dismissVisibility = Visibility.Collapsed;
        private bool _isFlashing;
        private bool _isInitialized;
        private Storyboard? _flashStoryboard;

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

        public bool IsFlashing
        {
            get => _isFlashing;
            set
            {
                if (Set(ref _isFlashing, value))
                {
                    UpdateFlashing();
                }
            }
        }

        public AlertMessage? CurrentAlert { get; private set; }

        public Action? OnDismissRequested { get; set; }

        public AlertBarWindow()
        {
            InitializeComponent();
            DataContext = this;
            _isInitialized = true;

            try
            {
                _flashStoryboard = (Storyboard)FindResource("SubBarFlashStoryboard");
            }
            catch
            {
                _flashStoryboard = null;
            }
        }

        public void SetScreen(Screen screen, double height)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = screen.Bounds.X;
            // Place the alert bar below the reserved AppBar work area (i.e., below the main banner).
            // Our main banner reserves space at the top, so WorkingArea.Top is where normal windows start.
            Top = screen.WorkingArea.Y;
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
            IsFlashing = alert.Level == AlertLevel.SuperCritical;
        }

        private void UpdateFlashing()
        {
            if (!_isInitialized)
            {
                return;
            }

            try
            {
                if (_flashStoryboard == null)
                {
                    return;
                }

                if (IsFlashing)
                {
                    // Start (or restart) flashing
                    _flashStoryboard.Begin(this, true);
                }
                else
                {
                    // Stop flashing and reset visuals
                    _flashStoryboard.Remove(this);
                    if (FindName("FlashRect") is FrameworkElement flash)
                    {
                        flash.Opacity = 0.0;
                    }
                    if (FindName("PulseBorder") is FrameworkElement border)
                    {
                        border.Opacity = 0.0;
                    }
                }
            }
            catch
            {
                // Never crash the alert window because of animation failures.
            }
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

            try
            {
                var dlg = new AlertDetailsWindow(CurrentAlert, BackgroundBrush, ForegroundBrush)
                {
                    Owner = this
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open AlertDetailsWindow: {ex}");
                System.Windows.MessageBox.Show(
                    $"Failed to open alert details: {ex.Message}",
                    "GridBanner Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value))
            {
                return false;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }
    }
}


