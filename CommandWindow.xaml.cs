using System;
using System.Windows;
using System.Windows.Input;

namespace GridBanner
{
    public partial class CommandWindow : Window
    {
        public event EventHandler<string>? CommandExecuted;

        public CommandWindow()
        {
            InitializeComponent();
            Loaded += CommandWindow_Loaded;
        }
        
        private void CommandWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure window is on top and has focus
            Activate();
            Topmost = true;
            Focus();
            CommandTextBox.Focus();
            CommandTextBox.SelectAll();
        }

        private void CommandTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ExecuteCommand();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand();
        }

        private void ExecuteCommand()
        {
            var command = CommandTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(command))
            {
                CommandExecuted?.Invoke(this, command);
            }
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

