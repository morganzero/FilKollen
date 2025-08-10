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

        private readonly ILogger _logger;
        private readonly LicenseService _licenseService;
        private readonly BrandingService _brandingService;
        private readonly ThemeService _themeService;
        private readonly AppConfig _config;
        private readonly TempFileScanner _fileScanner;
        private readonly LogViewerService _logViewer;
        private readonly QuarantineManager _quarantine;

        // KORRIGERAD constructor som matchar App.xaml.cs förväntningar
        public MainWindow(LicenseService licenseService, BrandingService brandingService, ThemeService themeService)
        {
            try 
            {
                // Validation först
                _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
                _brandingService = brandingService ?? throw new ArgumentNullException(nameof(brandingService));
                _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
                
                _logger = Log.Logger;
                _logger.Information("MainWindow constructor started with all services");

                // Initiera config och services
                _config = new AppConfig();
                _fileScanner = new TempFileScanner(_config, _logger);
                _quarantine = new QuarantineManager(_logger);
                _logViewer = new LogViewerService();

                _logger.Information("Supporting services initialized");

                // KRITISKT: InitializeComponent kan krascha
                _logger.Information("Calling InitializeComponent...");
                InitializeComponent();
                _logger.Information("InitializeComponent completed successfully");

                DataContext = this;
                
                // Asynkron initiation för att undvika blocking
                _logger.Information("Starting async initialization...");
                Loaded += async (s, e) => 
                {
                    try
                    {
                        await InitializeAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Async initialization failed");
                        // Visa varning men fortsätt köra
                        MessageBox.Show($"Vissa funktioner kanske inte fungerar korrekt:\n{ex.Message}", 
                            "Initieringsvarning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };

                _logger.Information("MainWindow constructor completed successfully");
            }
            catch (Exception ex)
            {
                var errorMsg = $"MainWindow constructor failed: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
                
                // Logga om möjligt
                try
                {
                    Log.Logger?.Error(ex, "MainWindow constructor failed");
                }
                catch { }

                // Skriv till fil för debug
                System.IO.File.WriteAllText($"mainwindow-error-{DateTime.Now:yyyyMMdd-HHmmss}.log", errorMsg);

                // Visa fel och kasta vidare
                MessageBox.Show(errorMsg, "MainWindow Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }
        private async Task InitializeAsync()
        {
            try
            {
                await InitializeServicesAsync();
                await InitializeUIComponentsAsync();
                await InitializeSecurityComponentsAsync();
                await InitializeLicensingAsync();
                await InitializeMonitoringAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize MainWindow");
                ShowCriticalErrorDialog("Initialization failed", ex);
            }
        }

        // ====== Init-stubs som din kod refererar till ======
        private async Task InitializeServicesAsync() 
        {
            _logger.Information("Initializing services...");
            await Task.Delay(10); // Yield
        }
        
        private async Task InitializeUIComponentsAsync() 
        {
            _logger.Information("Initializing UI components...");
            await Task.Delay(10); // Yield
        }
        
        private async Task InitializeSecurityComponentsAsync() 
        {
            _logger.Information("Initializing security components...");
            await Task.Delay(10); // Yield
        }
        
        private async Task InitializeLicensingAsync() 
        {
            _logger.Information("Initializing licensing...");
            await Task.Delay(10); // Yield
        }
        
        private async Task InitializeMonitoringAsync() 
        {
            _logger.Information("Initializing monitoring...");
            await Task.Delay(10); // Yield
        }

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

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}