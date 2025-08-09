using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FilKollen.Models;
using FilKollen.Services;
using Serilog;

namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private ILogger _logger;
        private AppConfig _config;
        private TempFileScanner _fileScanner;
        private LogViewerService _logViewer;
        private QuarantineManager _quarantine;

        // Parameterlös ctor om XAML kräver
        public MainWindow() : this(Log.Logger, new LogViewerService(), null) { }

        // Overload för App.xaml.cs som förväntar 3 argument (logger, logViewer, themeService)
        public MainWindow(ILogger logger, LogViewerService logViewer, object? _themeService)
        {
            InitializeComponent();

            _logger = logger ?? Log.Logger;
            _logViewer = logViewer ?? new LogViewerService();
            _config = new AppConfig();
            _fileScanner = new TempFileScanner(_config, _logger);
            _quarantine = new QuarantineManager(_logger);
        }

        // ====== Init-stubs som din kod refererar till ======
        private Task InitializeServicesAsync() => Task.CompletedTask;
        private Task InitializeUIComponentsAsync() => Task.CompletedTask;
        private Task InitializeSecurityComponentsAsync() => Task.CompletedTask;
        private Task InitializeLicensingAsync() => Task.CompletedTask;
        private Task InitializeMonitoringAsync() => Task.CompletedTask;

        // ====== Critical error dialog (utan TaskDialog-beroende) ======
        private void ShowCriticalErrorDialog(string message, Exception ex)
        {
            var detailed = $"{message}\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
            MessageBox.Show(detailed, "FilKollen - Kritiskt fel", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // ====== XAML event-stubs (matchar namnen i MainWindow.xaml) ======
        private void EmergencyScanButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("Emergency scan requested");
        }
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("Settings requested");
        }
        private void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            _logger.Information("Protection enabled");
        }
        private void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _logger.Information("Protection disabled");
        }
        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb) _logger.Information("Mode changed: {Mode}", rb.Content);
        }
        private void IDSToggle_Checked(object sender, RoutedEventArgs e)
        {
            _logger.Information("IDS enabled");
        }
        private void IDSToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _logger.Information("IDS disabled");
        }
        private async void TempScanButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("Temp scan triggered");
            var res = await _fileScanner.ScanTempDirectoriesAsync();
            _logger.Information("Temp scan done. Results: {Count}", res?.Count ?? 0);
        }
        private void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("Browser clean requested");
        }
        private void QuarantineManagerButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("Quarantine manager opened");
        }
        private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("System info requested");
        }
        private void RefreshThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("Threats refresh requested");
        }
        private void HandleAllThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("Handle all threats requested");
        }
        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            _logViewer.ClearLogs();
        }
        private void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"FilKollen-Logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                _logViewer.ExportLogs(path);
                _logger.Information("Logs exported to {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Export logs failed");
            }
        }
        private void LogLevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _logger.Information("Log level filter changed");
        }
    }
}
