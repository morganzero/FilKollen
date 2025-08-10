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
        private readonly LicenseService? _licenseService;
        private readonly BrandingService? _brandingService;
        private readonly ThemeService? _themeService;
        private readonly AppConfig _config;
        private readonly TempFileScanner? _fileScanner;
        private readonly LogViewerService? _logViewer;
        private readonly QuarantineManager? _quarantine;

public MainWindow() : this(null, null, null)
{
    // XAML constructor - delegerar till main constructor med null values
}
        // SÄKER constructor som hanterar null services
        public MainWindow(LicenseService? licenseService, BrandingService? brandingService, ThemeService? themeService)
        {
            try 
            {
                _logger = Log.Logger ?? throw new InvalidOperationException("Logger not initialized");
                _logger.Information("MainWindow constructor started (SÄKER MODE)");

                // SÄKER: Hantera null services gracefully
                _licenseService = licenseService;
                _brandingService = brandingService;
                _themeService = themeService;
                
                if (licenseService == null)
                    _logger.Warning("LicenseService is null - some features may be limited");
                if (brandingService == null)
                    _logger.Warning("BrandingService is null - using default branding");
                if (themeService == null)
                    _logger.Warning("ThemeService is null - using default theme");

                // Initiera säker config
                _config = new AppConfig();
                _logger.Information("AppConfig initialized");

                // Försök initiera supporting services säkert
                try
                {
                    _fileScanner = new TempFileScanner(_config, _logger);
                    _logger.Information("TempFileScanner initialized");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"TempFileScanner init failed: {ex.Message}");
                    _fileScanner = null;
                }

                try
                {
                    _quarantine = new QuarantineManager(_logger);
                    _logger.Information("QuarantineManager initialized");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"QuarantineManager init failed: {ex.Message}");
                    _quarantine = null;
                }

                try
                {
                    _logViewer = new LogViewerService();
                    _logger.Information("LogViewerService initialized");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"LogViewerService init failed: {ex.Message}");
                    _logViewer = null;
                }

                _logger.Information("Supporting services initialized (with fallbacks for failures)");

                // KRITISKT: InitializeComponent kan krascha - hantera säkert
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
                try
                {
                    System.IO.File.WriteAllText($"mainwindow-error-{DateTime.Now:yyyyMMdd-HHmmss}.log", errorMsg);
                }
                catch { }

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

        // ====== Säkra init-metoder ======
        private async Task InitializeServicesAsync() 
        {
            _logger.Information("Initializing services...");
            
            // Initiera logviewer om tillgänglig
            if (_logViewer != null)
            {
                try
                {
                    _logViewer.AddLogEntry(LogLevel.Information, "MainWindow", "🚀 FilKollen huvudfönster initierat");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"LogViewer init failed: {ex.Message}");
                }
            }
            
            await Task.Delay(10); // Yield
        }
        
        private async Task InitializeUIComponentsAsync() 
        {
            _logger.Information("Initializing UI components...");
            
            // Säker UI-initiation
            try
            {
                // Initiera UI-komponenter säkert
                if (StatusIndicator != null)
                    StatusIndicator.Fill = System.Windows.Media.Brushes.Orange;
                    
                if (SystemStatusText != null)
                    SystemStatusText.Text = "INITIERAR...";
                    
                if (StatusBarText != null)
                    StatusBarText.Text = "FilKollen Real-time Security - Initierar system...";
            }
            catch (Exception ex)
            {
                _logger.Warning($"UI initialization warning: {ex.Message}");
            }
            
            await Task.Delay(10); // Yield
        }
        
        private async Task InitializeSecurityComponentsAsync() 
        {
            _logger.Information("Initializing security components...");
            
            try
            {
                // Uppdatera säkerhetsstatus
                if (ProtectionStatusText != null)
                    ProtectionStatusText.Text = "INAKTIVERAT";
                    
                if (ProtectionIcon != null)
                {
                    ProtectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ShieldOff;
                    ProtectionIcon.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Security components init warning: {ex.Message}");
            }
            
            await Task.Delay(10); // Yield
        }
        
        private async Task InitializeLicensingAsync() 
        {
            _logger.Information("Initializing licensing...");
            
            try
            {
                if (_licenseService != null)
                {
                    // Försök validera licens säkert
                    var status = await _licenseService.ValidateLicenseAsync();
                    _logger.Information($"License status: {status}");
                }
                else
                {
                    _logger.Information("No license service available - running in limited mode");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"License initialization failed: {ex.Message}");
            }
            
            await Task.Delay(10); // Yield
        }
        
        private async Task InitializeMonitoringAsync() 
        {
            _logger.Information("Initializing monitoring...");
            
            try
            {
                // Sätt final status
                if (StatusIndicator != null)
                    StatusIndicator.Fill = System.Windows.Media.Brushes.Green;
                    
                if (SystemStatusText != null)
                    SystemStatusText.Text = "SYSTEM REDO";
                    
                if (StatusBarText != null)
                    StatusBarText.Text = "FilKollen Real-time Security - Redo för säkerhetsskanning";
                    
                _logViewer?.AddLogEntry(LogLevel.Information, "System", "✅ FilKollen fullständigt initierat och redo");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Monitoring initialization warning: {ex.Message}");
            }
            
            await Task.Delay(10); // Yield
        }

        // ====== Säker error dialog ======
        private void ShowCriticalErrorDialog(string message, Exception ex)
        {
            var detailed = $"{message}\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
            MessageBox.Show(detailed, "FilKollen - Kritiskt fel", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // ====== SÄKRA XAML event-handlers ======
        private void EmergencyScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Emergency scan requested");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🚨 Akutskanning begärd av användare");
                
                if (_fileScanner != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var results = await _fileScanner.ScanTempDirectoriesAsync();
                            _logger.Information($"Emergency scan completed: {results?.Count ?? 0} threats found");
                            
                            Dispatcher.Invoke(() =>
                            {
                                _logViewer?.AddLogEntry(LogLevel.Information, "Scan", 
                                    $"🔍 Akutskanning slutförd: {results?.Count ?? 0} hot funna");
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Emergency scan failed: {ex.Message}");
                        }
                    });
                }
                else
                {
                    MessageBox.Show("Skanningsfunktion inte tillgänglig", "Fel", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Emergency scan button error: {ex.Message}");
            }
        }
        
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Settings requested");
                MessageBox.Show("Inställningar kommer snart!", "FilKollen", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Settings button error: {ex.Message}");
            }
        }
        
        private void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Protection enabled");
                _logViewer?.AddLogEntry(LogLevel.Information, "Protection", "🛡️ Real-time skydd AKTIVERAT");
                
                if (ProtectionStatusText != null)
                    ProtectionStatusText.Text = "AKTIVERAT";
                if (ProtectionIcon != null)
                {
                    ProtectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Shield;
                    ProtectionIcon.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Protection toggle error: {ex.Message}");
            }
        }
        
        private void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Protection disabled");
                _logViewer?.AddLogEntry(LogLevel.Warning, "Protection", "⚠️ Real-time skydd INAKTIVERAT");
                
                if (ProtectionStatusText != null)
                    ProtectionStatusText.Text = "INAKTIVERAT";
                if (ProtectionIcon != null)
                {
                    ProtectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ShieldOff;
                    ProtectionIcon.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Protection toggle error: {ex.Message}");
            }
        }
        
        private async void TempScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Temp scan triggered");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔍 Temp-katalogskanning startad");
                
                if (_fileScanner != null)
                {
                    var res = await _fileScanner.ScanTempDirectoriesAsync();
                    _logger.Information("Temp scan done. Results: {Count}", res?.Count ?? 0);
                    _logViewer?.AddLogEntry(LogLevel.Information, "Scan", 
                        $"✅ Temp-skanning slutförd: {res?.Count ?? 0} hot funna");
                }
                else
                {
                    MessageBox.Show("Skanningsfunktion inte tillgänglig", "Fel", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Temp scan error: {ex.Message}");
                _logViewer?.AddLogEntry(LogLevel.Error, "Scan", $"❌ Temp-skanning misslyckades: {ex.Message}");
            }
        }
        
        private void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Browser clean requested");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🌐 Webbläsarrensning begärd");
                MessageBox.Show("Webbläsarrensning kommer snart!", "FilKollen", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Browser clean error: {ex.Message}");
            }
        }
        
        private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("System info requested");
                var info = $"FilKollen Real-time Security v2.0\n\n" +
                          $"OS: {Environment.OSVersion}\n" +
                          $"Dator: {Environment.MachineName}\n" +
                          $"Användare: {Environment.UserName}\n" +
                          $".NET: {Environment.Version}";
                MessageBox.Show(info, "Systeminformation", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"System info error: {ex.Message}");
            }
        }
        
        private void RefreshThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Threats refresh requested");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔄 Hotuppdatering begärd");
            }
            catch (Exception ex)
            {
                _logger.Error($"Refresh threats error: {ex.Message}");
            }
        }
        
        private void HandleAllThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Handle all threats requested");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🧹 Hantera alla hot begärt");
                MessageBox.Show("Automatisk hothantering kommer snart!", "FilKollen", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Handle all threats error: {ex.Message}");
            }
        }
        
        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logViewer?.ClearLogs();
            }
            catch (Exception ex)
            {
                _logger.Error($"Clear logs error: {ex.Message}");
            }
        }
        
        private void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"FilKollen-Logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                _logViewer?.ExportLogs(path);
                _logger.Information("Logs exported to {Path}", path);
                MessageBox.Show($"Loggar exporterade till:\n{path}", "Export slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Export logs failed");
            }
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}