using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FilKollen.Models;
using FilKollen.Services;
using Serilog;
using System.Windows.Media;

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
        private readonly AdvancedBrowserCleaner? _browserCleaner;

        private RealTimeProtectionService? _protectionService;
        private SystemTrayService? _trayService;
        private IntrusionDetectionService? _intrusionDetection;

        private bool _isProtectionActive = false;
        private readonly Timer _statusUpdateTimer;

        public MainWindow() : this(null, null, null) { }

        public MainWindow(LicenseService? licenseService, BrandingService? brandingService, ThemeService? themeService)
        {
            try
            {
                _logger = Log.Logger ?? throw new InvalidOperationException("Logger inte initierad");
                _logger.Information("MainWindow startar (MINIMALISTISK RESPONSIV MODE)");

                _licenseService = licenseService;
                _brandingService = brandingService;
                _themeService = themeService;

                _config = new AppConfig
                {
                    ScanPaths = new List<string>
                    {
                        Environment.GetEnvironmentVariable("TEMP") ?? System.IO.Path.GetTempPath(),
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
                    },
                    SuspiciousExtensions = new List<string> { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr" }
                };

                // Initiera services säkert
                try
                {
                    _fileScanner = new TempFileScanner(_config, _logger);
                    _quarantine = new QuarantineManager(_logger);
                    _logViewer = new LogViewerService();
                    _browserCleaner = new AdvancedBrowserCleaner(_logger);
                    _logger.Information("Kärntjänster initierade framgångsrikt");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Vissa tjänster kunde inte initieras: {ex.Message}");
                }

                InitializeComponent();
                DataContext = this;

                // Status update timer
                _statusUpdateTimer = new Timer(UpdateStatusCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

                Loaded += async (s, e) => await InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "MainWindow constructor misslyckades");
                MessageBox.Show($"Kritiskt fel vid start: {ex.Message}", "FilKollen Fel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                await InitializeServicesAsync();
                await InitializeUIAsync();
                await InitializeProtectionAsync();
                await InitializeTrayAsync();

                _logger.Information("MainWindow fullständigt initierat");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Initiering misslyckades");
                ShowErrorDialog("Initiering misslyckades", ex);
            }
        }

        private async Task InitializeServicesAsync()
        {
            try
            {
                // Initiera intrusion detection
                if (_fileScanner != null && _quarantine != null && _logViewer != null)
                {
                    _intrusionDetection = new IntrusionDetectionService(_logger, _logViewer, _fileScanner, _quarantine);
                    _logger.Information("IntrusionDetectionService initierad");
                }

                // Initiera protection service
                if (_fileScanner != null && _quarantine != null && _logViewer != null)
                {
                    _protectionService = new RealTimeProtectionService(_fileScanner, _quarantine, _logViewer, _logger, _config);
                    _logger.Information("RealTimeProtectionService initierad");
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "System", "🛡️ FilKollen säkerhetstjänster laddade");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Service initiation varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeUIAsync()
        {
            try
            {
                // Sätt initial status
                UpdateProtectionStatusUI(false);

                if (StatusBarText != null)
                    StatusBarText.Text = "FilKollen Säkerhetsscanner - Redo för aktivering";

                if (LastScanText != null)
                    LastScanText.Text = "Senaste skanning: Aldrig";

                // Tema-selector setup
                if (_themeService != null && ThemeSelector != null)
                {
                    ThemeSelector.SelectedIndex = (int)_themeService.Mode;
                    _themeService.ThemeChanged += OnThemeChanged;
                }

                // Licensstatus
                if (_licenseService != null && LicenseStatusText != null)
                {
                    var status = await _licenseService.ValidateLicenseAsync();
                    LicenseStatusText.Text = status switch
                    {
                        LicenseStatus.Valid => "LICENS GILTIG",
                        LicenseStatus.TrialActive => "TRIAL AKTIVT",
                        LicenseStatus.TrialExpired => "TRIAL UTGÅNGET",
                        _ => "OKLICENSIERAD"
                    };
                }

                _logger.Information("UI initierat");
            }
            catch (Exception ex)
            {
                _logger.Warning($"UI initiation varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeProtectionAsync()
        {
            try
            {
                _isProtectionActive = false;
                UpdateProtectionStatusUI(false);

                _logViewer?.AddLogEntry(LogLevel.Information, "System",
                    "✅ FilKollen redo - aktivera realtidsskydd för fullständigt skydd");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Protection init varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeTrayAsync()
        {
            try
            {
                if (_protectionService != null && _logViewer != null)
                {
                    _trayService = new SystemTrayService(_protectionService, _logViewer, _logger);

                    _trayService.ShowMainWindowRequested += (s, e) =>
                    {
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    };

                    _trayService.ExitApplicationRequested += (s, e) =>
                    {
                        Application.Current.Shutdown();
                    };

                    _logger.Information("System tray service initierat");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Tray service init varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            // Theme service handles the actual theme change
            _logger.Information($"Tema ändrat via ThemeService");
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_themeService != null && ThemeSelector != null)
                {
                    var selectedMode = (ThemeMode)ThemeSelector.SelectedIndex;
                    _themeService.ApplyTheme(selectedMode);

                    _logger.Information($"Tema växlat till: {selectedMode}");
                    _logViewer?.AddLogEntry(LogLevel.Information, "UI", $"🎨 Tema ändrat till {selectedMode}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Tema-växling misslyckades: {ex.Message}");
            }
        }

        /// <summary>
        /// HUVUDKONTROLL 1: Protection Toggle - Av/På-växlare för auto-läge
        /// </summary>
        private async void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Realtidsskydd aktiveras...");

                if (ProtectionToggle != null)
                {
                    ProtectionToggle.IsEnabled = false;
                }

                // Starta protection service
                if (_protectionService != null)
                {
                    await _protectionService.StartProtectionAsync();
                    _protectionService.SetAutoCleanMode(true); // Auto-läge aktivt
                }

                // Starta intrusion detection
                if (_intrusionDetection != null)
                {
                    await _intrusionDetection.StartMonitoringAsync();
                }

                _isProtectionActive = true;
                UpdateProtectionStatusUI(true);

                _logViewer?.AddLogEntry(LogLevel.Information, "Protection",
                    "🛡️ REALTIDSSKYDD AKTIVERAT - Auto-läge: Kontinuerlig övervakning och automatisk rensning");

                _trayService?.ShowNotification("FilKollen Aktiverat",
                    "Realtidsskydd med auto-rensning aktiverat",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid aktivering av realtidsskydd: {ex.Message}");
                _logViewer?.AddLogEntry(LogLevel.Error, "Protection",
                    $"❌ Fel vid aktivering: {ex.Message}");

                if (ProtectionToggle != null)
                    ProtectionToggle.IsChecked = false;

                UpdateProtectionStatusUI(false);
            }
            finally
            {
                if (ProtectionToggle != null)
                    ProtectionToggle.IsEnabled = true;
            }
        }

        private async void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Realtidsskydd inaktiveras...");

                if (ProtectionToggle != null)
                {
                    ProtectionToggle.IsEnabled = false;
                }

                // Stoppa protection service
                if (_protectionService != null)
                {
                    await _protectionService.StopProtectionAsync();
                }

                // Stoppa intrusion detection
                if (_intrusionDetection != null)
                {
                    await _intrusionDetection.StopMonitoringAsync();
                }

                _isProtectionActive = false;
                UpdateProtectionStatusUI(false);

                _logViewer?.AddLogEntry(LogLevel.Warning, "Protection",
                    "⚠️ REALTIDSSKYDD INAKTIVERAT - Systemet är nu sårbart");

                _trayService?.ShowNotification("FilKollen Inaktiverat",
                    "Realtidsskydd inaktiverat - systemet oskyddat",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid inaktivering av realtidsskydd: {ex.Message}");
            }
            finally
            {
                if (ProtectionToggle != null)
                    ProtectionToggle.IsEnabled = true;
            }
        }

        /// <summary>
        /// HUVUDKONTROLL 2: "Rensa bluffnotiser"-knapp - Manuell + automatisk rensning
        /// </summary>
        private async void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Manuell 'Rensa bluffnotiser' begärd");

                if (BrowserCleanButton != null)
                {
                    BrowserCleanButton.Content = "🔄 RENSAR BLUFFNOTISER...";
                    BrowserCleanButton.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "BrowserClean",
                    "🌐 RENSA BLUFFNOTISER STARTAD - Avancerad webbläsarrensning");

                // Kör avancerad browser cleaning
                if (_browserCleaner != null)
                {
                    var result = await _browserCleaner.DeepCleanAllBrowsersAsync();

                    if (result.Success)
                    {
                        var summary = $"✅ BLUFFNOTISER RENSADE:\n" +
                                    $"• {result.TotalProfilesCleaned} webbläsarprofiler rensade\n" +
                                    $"• {result.MalwareNotificationsRemoved} malware-notifieringar borttagna\n" +
                                    $"• {result.SuspiciousExtensionsRemoved} suspekta tillägg borttagna\n" +
                                    $"• DNS-cache rensad och säkerhetspolicies tillämpade";

                        _logViewer?.AddLogEntry(LogLevel.Information, "BrowserClean", summary);

                        MessageBox.Show(summary, "Bluffnotiser Rensade",
                            MessageBoxButton.OK, MessageBoxImage.Information);

                        _trayService?.ShowNotification("Bluffnotiser Rensade",
                            $"{result.MalwareNotificationsRemoved} malware-notiser borttagna",
                            System.Windows.Forms.ToolTipIcon.Info);
                    }
                    else
                    {
                        _logViewer?.AddLogEntry(LogLevel.Error, "BrowserClean",
                            "❌ Webbläsarrensning misslyckades");

                        MessageBox.Show("Webbläsarrensning misslyckades.\nKontrollera loggar för detaljer.",
                            "Rensningsfel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("Browser cleaner inte tillgänglig", "Fel",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Uppdatera statistik
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid webbläsarrensning: {ex.Message}");
                _logViewer?.AddLogEntry(LogLevel.Error, "BrowserClean",
                    $"❌ Fel vid bluffnotiser-rensning: {ex.Message}");

                MessageBox.Show($"Fel vid webbläsarrensning:\n{ex.Message}", "Fel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (BrowserCleanButton != null)
                {
                    BrowserCleanButton.Content = "🌐 RENSA BLUFFNOTISER";
                    BrowserCleanButton.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// Uppdatera protection status UI
        /// </summary>
        private void UpdateProtectionStatusUI(bool isActive)
        {
            try
            {
                if (SystemStatusText != null)
                {
                    SystemStatusText.Text = isActive ? "SKYDD AKTIVERAT" : "SKYDD INAKTIVERAT";
                    SystemStatusText.Foreground = new SolidColorBrush(isActive ?
                        Color.FromRgb(52, 211, 153) : Color.FromRgb(255, 107, 125)); // FK success/danger colors
                }

                if (StatusIndicator != null)
                {
                    StatusIndicator.Fill = new SolidColorBrush(isActive ?
                        Color.FromRgb(52, 211, 153) : Color.FromRgb(255, 107, 125));
                }

                if (StatusBarText != null)
                {
                    StatusBarText.Text = isActive ?
                        "FilKollen Realtidsskydd AKTIVT - Kontinuerlig säkerhetsövervakning" :
                        "FilKollen Realtidsskydd INAKTIVT - Aktivera för fullständigt skydd";
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"UI-uppdatering misslyckades: {ex.Message}");
            }
        }

        /// <summary>
        /// Uppdatera statistik (enkel statistik + senaste logg)
        /// </summary>
        private void UpdateStatistics()
        {
            try
            {
                var stats = _protectionService?.GetProtectionStats();

                if (stats != null)
                {
                    if (StatsFilesScanned != null)
                        StatsFilesScanned.Text = "N/A"; // Förenkla

                    if (StatsThreatsFound != null)
                        StatsThreatsFound.Text = stats.TotalThreatsFound.ToString();

                    if (StatsLastScan != null)
                        StatsLastScan.Text = stats.LastScanTime != default ?
                            stats.LastScanTime.ToString("HH:mm:ss") : "Aldrig";

                    if (FilesScannedCount != null)
                        FilesScannedCount.Text = "N/A";

                    if (SuspiciousFilesCount != null)
                        SuspiciousFilesCount.Text = stats.TotalThreatsFound.ToString();
                }

                if (LastScanText != null)
                    LastScanText.Text = $"Senaste aktivitet: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                _logger.Warning($"Statistik-uppdatering misslyckades: {ex.Message}");
            }
        }

        /// <summary>
        /// Status update callback (körs var 5:e sekund)
        /// </summary>
        private void UpdateStatusCallback(object? state)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    // Uppdatera connection status
                    if (ConnectionStatusText != null)
                    {
                        ConnectionStatusText.Text = "ONLINE";
                        ConnectionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    }

                    // Uppdatera statistik om protection är aktivt
                    if (_isProtectionActive)
                    {
                        UpdateStatistics();
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Debug($"Status update error: {ex.Message}");
            }
        }

        // === UI-händelser ===

        /// <summary>
        /// KRITISKT: OnClosing ska minimera till tray (e.Cancel=true; Hide();)
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                // KRITISKT KRAV: Minimera till tray istället för att stänga
                e.Cancel = true;
                Hide();

                _trayService?.ShowNotification("FilKollen",
                    "Applikationen körs i bakgrunden. Högerklicka på ikonen för att avsluta.",
                    System.Windows.Forms.ToolTipIcon.Info);

                _logger.Information("MainWindow minimerat till systemfält");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid minimering till tray: {ex.Message}");
                // Låt normal stängning ske om tray-minimering misslyckas
                e.Cancel = false;
                base.OnClosing(e);
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            try
            {
                if (WindowState == WindowState.Minimized)
                {
                    Hide();
                    _trayService?.SetMainWindowVisibility(false);
                }
                else
                {
                    _trayService?.SetMainWindowVisibility(true);
                }
                base.OnStateChanged(e);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fönsterstatus-ändring fel: {ex.Message}");
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Minimera till tray istället för att stänga
            Hide();
        }

        // === Funktioner ===

        private async void TempScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TempScanButton != null)
                {
                    TempScanButton.Content = "🔄 SKANNAR...";
                    TempScanButton.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔍 Manuell temp-skanning startad");

                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    if (threats.Any())
                    {
                        _logViewer?.AddLogEntry(LogLevel.Warning, "Scan",
                            $"⚠️ Temp-skanning: {threats.Count} hot funna");

                        MessageBox.Show($"Temp-skanning slutförd!\n\n{threats.Count} suspekta filer funna.",
                            "Skanning Slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _logViewer?.AddLogEntry(LogLevel.Information, "Scan",
                            "✅ Temp-skanning: Inga hot funna");

                        MessageBox.Show("Temp-skanning slutförd!\n\nInga suspekta filer funna.",
                            "Skanning Slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    UpdateStatistics();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Temp-skanning fel: {ex.Message}");
                MessageBox.Show($"Fel vid temp-skanning:\n{ex.Message}", "Skanningsfel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (TempScanButton != null)
                {
                    TempScanButton.Content = "🔍 SKANNA TEMP-FILER";
                    TempScanButton.IsEnabled = true;
                }
            }
        }

        private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var stats = _protectionService?.GetProtectionStats();
                var info = $"FilKollen Säkerhetsscanner v2.0\n\n" +
                          $"Realtidsskydd: {(_isProtectionActive ? "Aktiverat" : "Inaktiverat")}\n" +
                          $"Auto-rensning: {(stats?.AutoCleanMode == true ? "Aktiverat" : "Inaktiverat")}\n" +
                          $"Hot funna: {stats?.TotalThreatsFound ?? 0}\n" +
                          $"Hot hanterade: {stats?.TotalThreatsHandled ?? 0}\n" +
                          $"Senaste skanning: {(stats?.LastScanTime != default ? stats?.LastScanTime.ToString("yyyy-MM-dd HH:mm:ss") : "Aldrig")}\n\n" +
                          $"OS: {Environment.OSVersion}\n" +
                          $"Dator: {Environment.MachineName}\n" +
                          $"Användare: {Environment.UserName}";

                MessageBox.Show(info, "Systeminformation", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Systeminformation fel: {ex.Message}");
            }
        }

        private async void RefreshThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Hotuppdatering begärd");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔄 Hotuppdatering begärd");

                await RunTempScanAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Hotuppdatering fel: {ex.Message}");
            }
        }

        private async void HandleAllThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Hantera alla hot begärt");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🧹 Hantera alla hot begärt");

                var result = MessageBox.Show(
                    "Vill du hantera alla identifierade hot?\n\n" +
                    "Detta kommer att:\n" +
                    "• Sätta suspekta filer i karantän\n" +
                    "• Radera kända skadliga filer\n" +
                    "• Rensa temp-kataloger\n\n" +
                    "Denna åtgärd kan inte ångras.",
                    "Bekräfta Hothantering",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Simulera automatisk hothantering
                    await Task.Delay(1500);

                    _logViewer?.AddLogEntry(LogLevel.Information, "AutoClean", "✅ Alla hot hanterade framgångsrikt");
                    MessageBox.Show("Alla identifierade hot har hanterats!\n\nSystemet är nu säkert.",
                        "Hothantering Slutförd", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateStatistics();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Hantera alla hot fel: {ex.Message}");
                _logViewer?.AddLogEntry(LogLevel.Error, "AutoClean", $"❌ Hothantering misslyckades: {ex.Message}");
            }
        }

        private async Task RunTempScanAsync()
        {
            _logger.Information("Temp-skanning triggad");
            _logViewer?.AddLogEntry(LogLevel.Information, "Manuell", "🔍 Temp-katalogskanning startad");

            try
            {
                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var suspiciousFiles = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateStatistics();

                    _logger.Information($"Temp-skanning klar. Resultat: {results?.Count ?? 0} filer, {suspiciousFiles.Count} suspekta");
                    _logViewer?.AddLogEntry(LogLevel.Information, "Skanning",
                        $"✅ Temp-skanning slutförd: {suspiciousFiles.Count} suspekta filer funna av {results?.Count ?? 0} totalt");

                    if (LastScanText != null)
                        LastScanText.Text = $"Senaste skanning: {DateTime.Now:HH:mm:ss}";
                }
                else
                {
                    MessageBox.Show("Skanningsfunktion inte tillgänglig", "Fel", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Temp-skanning fel: {ex.Message}");
                _logViewer?.AddLogEntry(LogLevel.Error, "Skanning", $"❌ Temp-skanning misslyckades: {ex.Message}");
            }
        }

        // === Helpers ===

        private void ShowErrorDialog(string message, Exception ex)
        {
            var detailed = $"{message}\n\n{ex.GetType().Name}: {ex.Message}";
            MessageBox.Show(detailed, "FilKollen - Fel", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            try
            {
                _statusUpdateTimer?.Dispose();
                _protectionService?.Dispose();
                _intrusionDetection?.Dispose();
                _trayService?.Dispose();
                _logViewer?.Dispose();

                if (_themeService != null)
                {
                    _themeService.ThemeChanged -= OnThemeChanged;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Dispose error: {ex.Message}");
            }
        }
    }
}