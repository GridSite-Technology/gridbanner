using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GridBanner
{
    public partial class CommandWindow : Window
    {
        public event EventHandler<string>? CommandExecuted;
        private string _commandText = string.Empty;

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
            
            // Force set colors using multiple methods to ensure visibility
            var blackBrush = new SolidColorBrush(Colors.Black);
            var whiteBrush = new SolidColorBrush(Colors.White);
            
            CommandTextBox.Foreground = blackBrush;
            CommandTextBox.Background = whiteBrush;
            CommandTextBox.CaretBrush = blackBrush;
            CommandTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212));
            CommandTextBox.BorderThickness = new Thickness(1);
            
            // Set text directly
            CommandTextBox.Text = string.Empty;
            
            CommandTextBox.Focus();
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
