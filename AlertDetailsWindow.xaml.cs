using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;

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

        public Uri? ContactPhoneUri => BuildTelUri(ContactPhone);
        public Uri? ContactEmailUri => BuildMailtoUri(ContactEmail);
        public Uri? ContactTeamsUri => BuildTeamsUri(ContactTeams);

        public Visibility ContactVisibility => HasAnyContact ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ContactNameVisibility => string.IsNullOrWhiteSpace(ContactName) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ContactPhoneVisibility => ContactPhoneUri == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ContactEmailVisibility => ContactEmailUri == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ContactTeamsVisibility => ContactTeamsUri == null ? Visibility.Collapsed : Visibility.Visible;
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

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // UseShellExecute allows OS to route mailto:/tel:/msteams:/https links properly.
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
            e.Handled = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static Uri? BuildTelUri(string? phone)
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

            return new Uri("tel:" + cleaned);
        }

        private static Uri? BuildMailtoUri(string? email)
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

            return new Uri("mailto:" + trimmed);
        }

        private static Uri? BuildTeamsUri(string? value)
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
                return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ? uri : null;
            }

            // If user provided something else (like a Teams user ID), we can't reliably turn it into a link.
            return null;
        }
    }
}


