using System;
using System.Windows;
using System.IO;

namespace GridBanner
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Handle unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            
            // Ensure default config exists early
            try
            {
                var config = ConfigManager.LoadConfig();
            }
            catch (Exception ex)
            {
                // Log error but don't crash - use defaults
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Log the error
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");
            
            // Show error message (optional - you might want to remove this for production)
            MessageBox.Show($"An error occurred: {e.Exception.Message}\n\nStack trace: {e.Exception.StackTrace}", 
                "GridBanner Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // Mark as handled to prevent app crash
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"Unhandled domain exception: {exception?.Message}");
            
            if (exception != null)
            {
                MessageBox.Show($"A critical error occurred: {exception.Message}", 
                    "GridBanner Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

