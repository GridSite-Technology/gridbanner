using System.Windows;

namespace GridBanner
{
    public partial class PasswordPromptWindow : Window
    {
        public string? Password { get; private set; }
        
        public PasswordPromptWindow(string keyName)
        {
            InitializeComponent();
            KeyNameText.Text = $"Enter the password for key: {keyName}";
            PasswordInput.Focus();
        }
        
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Password = PasswordInput.Password;
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

