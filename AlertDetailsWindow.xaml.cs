using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace GridBanner
{
    public partial class AlertDetailsWindow : Window
    {
        public string Header { get; }
        public string Message { get; }
        public Brush BackgroundBrush { get; }
        public Brush ForegroundBrush { get; }

        public string? ContactName { get; }
        public string? ContactPhone { get; }
        public string? ContactEmail { get; }
        public string? ContactTeams { get; }

        public string ContactTeamsLabel => string.IsNullOrWhiteSpace(ContactTeams) ? string.Empty : ContactTeams!;

        public string? ContactPhoneLink => BuildTelLink(ContactPhone);
        public string? ContactEmailLink => BuildMailtoLink(ContactEmail);
        public string? ContactTeamsLink => BuildTeamsLink(ContactTeams);

        public Visibility ContactVisibility => HasAnyContact ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ContactNameVisibility => string.IsNullOrWhiteSpace(ContactName) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ContactPhoneVisibility => string.IsNullOrWhiteSpace(ContactPhoneLink) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ContactEmailVisibility => string.IsNullOrWhiteSpace(ContactEmailLink) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ContactTeamsVisibility => string.IsNullOrWhiteSpace(ContactTeamsLink) ? Visibility.Collapsed : Visibility.Visible;
        private bool HasAnyContact =>
            !string.IsNullOrWhiteSpace(ContactName) ||
            !string.IsNullOrWhiteSpace(ContactPhone) ||
            !string.IsNullOrWhiteSpace(ContactEmail) ||
            !string.IsNullOrWhiteSpace(ContactTeams);

        public AlertDetailsWindow(AlertMessage alert, Brush background, Brush foreground)
        {
            InitializeComponent();
            Header = $"{alert.Level.ToString().ToUpperInvariant()}: {alert.Summary}";
            Message = alert.Message;
            BackgroundBrush = background;
            ForegroundBrush = foreground;
            ContactName = alert.ContactName;
            ContactPhone = alert.ContactPhone;
            ContactEmail = alert.ContactEmail;
            ContactTeams = alert.ContactTeams;
            DataContext = this;
        }

        private void Link_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.Tag is string link && !string.IsNullOrWhiteSpace(link))
                {
                    // UseShellExecute allows OS to route mailto:/tel:/msteams:/https links properly.
                    Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
                }
            }
            catch
            {
                // ignore
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string? BuildTelLink(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return null;
            }

            // Keep digits/+ only for the tel URI
            var trimmed = phone.Trim();
            var cleaned = string.Empty;
            foreach (var ch in trimmed)
            {
                if (char.IsDigit(ch) || ch == '+')
                {
                    cleaned += ch;
                }
            }

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return null;
            }

            return "tel:" + cleaned;
        }

        private static string? BuildMailtoLink(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            var trimmed = email.Trim();
            if (!trimmed.Contains("@", StringComparison.Ordinal))
            {
                return null;
            }

            return "mailto:" + trimmed;
        }

        private static string? BuildTeamsLink(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();

            // Accept msteams: deep links or https links (meeting/chat)
            if (trimmed.StartsWith("msteams:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.TryCreate(trimmed, UriKind.Absolute, out _) ? trimmed : null;
            }

            // If user provided something else (like a Teams user ID), we can't reliably turn it into a link.
            return null;
        }
    }
}


