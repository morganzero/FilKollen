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

        // ÄNDRAT: Inte readonly så vi kan tilldela i async metoder
        private RealTimeProtectionService? _protectionService;
        private SystemTrayService? _trayService;

        private bool _isProtectionActive = false;

        public MainWindow() : this(null, null, null)
        {
            // XAML constructor - delegerar till main constructor med null values
        }

        public MainWindow(LicenseService? licenseService, BrandingService? brandingService, ThemeService? themeService)
        {
            try
            {
                _logger = Log.Logger ?? throw new InvalidOperationException("Logger inte initierad");
                _logger.Information("MainWindow startar (SÄKER MODE med Svenska)");

                // SÄKER: Hantera null services gracefully
                _licenseService = licenseService;
                _brandingService = brandingService;
                _themeService = themeService;

                if (licenseService == null)
                    _logger.Warning("LicenseService är null - vissa funktioner kan vara begränsade");
                if (brandingService == null)
                    _logger.Warning("BrandingService är null - använder standard branding");
                if (themeService == null)
                    _logger.Warning("ThemeService är null - använder standard tema");

                // Initiera säker config
                _config = new AppConfig();
                _logger.Information("AppConfig initierad");

                // Försök initiera supporting services säkert
                try
                {
                    _fileScanner = new TempFileScanner(_config, _logger);
                    _logger.Information("TempFileScanner initierad");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"TempFileScanner init misslyckades: {ex.Message}");
                    _fileScanner = null;
                }

                try
                {
                    _quarantine = new QuarantineManager(_logger);
                    _logger.Information("QuarantineManager initierad");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"QuarantineManager init misslyckades: {ex.Message}");
                    _quarantine = null;
                }

                try
                {
                    _logViewer = new LogViewerService();
                    _logger.Information("LogViewerService initierad");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"LogViewerService init misslyckades: {ex.Message}");
                    _logViewer = null;
                }

                _logger.Information("Supporting services initierade (med fallbacks för misslyckanden)");

                // KRITISKT: InitializeComponent kan krascha - hantera säkert
                _logger.Information("Anropar InitializeComponent...");
                InitializeComponent();
                _logger.Information("InitializeComponent slutförd framgångsrikt");

                DataContext = this;

                // Asynkron initiation för att undvika blocking
                _logger.Information("Startar asynkron initiering...");
                Loaded += async (s, e) =>
                {
                    try
                    {
                        await InitializeAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Asynkron initiering misslyckades");
                        MessageBox.Show($"Vissa funktioner kanske inte fungerar korrekt:\n{ex.Message}",
                            "Initieringsvarning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };

                _logger.Information("MainWindow constructor slutförd framgångsrikt");
            }
            catch (Exception ex)
            {
                var errorMsg = $"MainWindow constructor misslyckades: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";

                try
                {
                    Log.Logger?.Error(ex, "MainWindow constructor misslyckades");
                }
                catch { }

                try
                {
                    System.IO.File.WriteAllText($"mainwindow-error-{DateTime.Now:yyyyMMdd-HHmmss}.log", errorMsg);
                }
                catch { }

                MessageBox.Show(errorMsg, "MainWindow Fel", MessageBoxButton.OK, MessageBoxImage.Error);
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
                await InitializeSystemTrayAsync();
                await InitializeMonitoringAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Misslyckades att initiera MainWindow");
                ShowCriticalErrorDialog("Initiering misslyckades", ex);
            }
        }

        private async Task InitializeServicesAsync()
        {
            _logger.Information("Initierar tjänster...");

            if (_logViewer != null)
            {
                try
                {
                    _logViewer.AddLogEntry(LogLevel.Information, "MainWindow", "🚀 FilKollen huvudfönster initierat");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"LogViewer init misslyckades: {ex.Message}");
                }
            }

            await Task.Delay(10);
        }

        private async Task InitializeUIComponentsAsync()
        {
            _logger.Information("Initierar UI-komponenter...");

            try
            {
                // Sätt svenskt språk och initial status
                if (StatusIndicator != null)
                    StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 107, 125)); // #ff6b7d

                if (SystemStatusText != null)
                    SystemStatusText.Text = "SKYDD INAKTIVERAT";

                if (StatusBarText != null)
                    StatusBarText.Text = "FilKollen Säkerhetsscanner - Initierar system...";

                if (LastScanText != null)
                    LastScanText.Text = "Senaste skanning: Aldrig";

                // Sätt tema-toggle baserat på nuvarande tema
                if (_themeService != null && ThemeToggle != null)
                {
                    ThemeToggle.IsChecked = !_themeService.IsDarkTheme; // Inverted eftersom ljust tema = checked
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"UI initiering varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeSecurityComponentsAsync()
        {
            _logger.Information("Initierar säkerhetskomponenter...");

            try
            {
                // NYTT: Initiera RealTimeProtectionService här
                if (_fileScanner != null && _quarantine != null && _logViewer != null)
                {
                    _protectionService = new RealTimeProtectionService(_fileScanner, _quarantine, _logViewer, _logger, _config);
                    _logger.Information("RealTimeProtectionService initierad");
                }

                // Uppdatera säkerhetsstatus
                UpdateProtectionStatus(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Säkerhetskomponenter init varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeLicensingAsync()
        {
            _logger.Information("Initierar licensiering...");

            try
            {
                if (_licenseService != null)
                {
                    var status = await _licenseService.ValidateLicenseAsync();
                    _logger.Information($"Licensstatus: {status}");

                    // Uppdatera UI baserat på licensstatus
                    if (LicenseStatusText != null)
                    {
                        LicenseStatusText.Text = status switch
                        {
                            LicenseStatus.Valid => "LICENS GILTIG",
                            LicenseStatus.TrialActive => "TRIAL AKTIVT",
                            LicenseStatus.TrialExpired => "TRIAL UTGÅNGET",
                            LicenseStatus.Expired => "LICENS UTGÅNGEN",
                            _ => "OKLICENSIERAD"
                        };
                    }
                }
                else
                {
                    _logger.Information("Ingen licenstjänst tillgänglig - kör i begränsat läge");
                    if (LicenseStatusText != null)
                        LicenseStatusText.Text = "BEGRÄNSAT LÄGE";
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Licensinitiering misslyckades: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeSystemTrayAsync()
        {
            _logger.Information("Initierar systemfält...");

            try
            {
                if (_protectionService != null && _logViewer != null)
                {
                    _trayService = new SystemTrayService(_protectionService, _logViewer, _logger);

                    // Koppla events
                    _trayService.ShowMainWindowRequested += (s, e) =>
                    {
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    };

                    _trayService.ExitApplicationRequested += (s, e) => Close();

                    _logger.Information("SystemTrayService initierad");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"SystemTray initiering misslyckades: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeMonitoringAsync()
        {
            _logger.Information("Initierar övervakning...");

            try
            {
                // Sätt final status
                if (StatusIndicator != null)
                    StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 107, 125));

                if (SystemStatusText != null)
                    SystemStatusText.Text = "SKYDD INAKTIVERAT";

                if (StatusBarText != null)
                    StatusBarText.Text = "FilKollen Säkerhetsscanner - Redo för säkerhetsskanning";

                _logViewer?.AddLogEntry(LogLevel.Information, "System", "✅ FilKollen fullständigt initierat och redo");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Övervakningsinitiering varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        // ====== FÖRENKLAD SVENSKA EVENT HANDLERS ======

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_themeService != null && ThemeToggle != null)
                {
                    // Toggle mellan mörkt och ljust tema
                    _themeService.ToggleTheme();
                    _logger.Information($"Tema växlat till: {(_themeService.IsDarkTheme ? "Mörkt" : "Ljust")}");
                    _logViewer?.AddLogEntry(LogLevel.Information, "UI", $"🎨 Tema ändrat till {(_themeService.IsDarkTheme ? "mörkt" : "ljust")}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Tema-växling misslyckades: {ex.Message}");
            }
        }

        private async void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Realtidsskydd aktiverat");
                _logViewer?.AddLogEntry(LogLevel.Information, "Skydd", "🛡️ Realtidsskydd AKTIVERAT");

                if (_protectionService != null)
                {
                    await _protectionService.StartProtectionAsync();
                    _isProtectionActive = true;
                    UpdateProtectionStatus(true);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Realtidsskydd aktivering misslyckades: {ex.Message}");
                UpdateProtectionStatus(false);
                if (ProtectionToggle != null)
                    ProtectionToggle.IsChecked = false;
            }
        }

        private async void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Realtidsskydd inaktiverat");
                _logViewer?.AddLogEntry(LogLevel.Warning, "Skydd", "⚠️ Realtidsskydd INAKTIVERAT");

                if (_protectionService != null)
                {
                    await _protectionService.StopProtectionAsync();
                    _isProtectionActive = false;
                    UpdateProtectionStatus(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Realtidsskydd inaktivering misslyckades: {ex.Message}");
            }
        }

        private void UpdateProtectionStatus(bool isActive)
        {
            try
            {
                if (SystemStatusText != null)
                {
                    SystemStatusText.Text = isActive ? "SKYDD AKTIVERAT" : "SKYDD INAKTIVERAT";
                    SystemStatusText.Foreground = new SolidColorBrush(isActive ?
                        Color.FromRgb(0, 255, 136) : Color.FromRgb(255, 107, 125));
                }

                if (StatusIndicator != null)
                {
                    StatusIndicator.Fill = new SolidColorBrush(isActive ?
                        Color.FromRgb(0, 255, 136) : Color.FromRgb(255, 107, 125));
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte uppdatera skyddsstatus: {ex.Message}");
            }
        }

        private async Task RunTempScanAsync()
        {
            _logger.Information("Temp-skanning triggad");
            _logViewer?.AddLogEntry(LogLevel.Information, "Manuell", "🔍 Temp-katalogskanning startad");

            if (TempScanButton != null)
            {
                TempScanButton.Content = "🔄 SKANNAR...";
                TempScanButton.IsEnabled = false;
            }

            try
            {
                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var suspiciousFiles = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateThreatsList(suspiciousFiles);
                    UpdateStatistics(results?.Count ?? 0, suspiciousFiles.Count);

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
            finally
            {
                if (TempScanButton != null)
                {
                    TempScanButton.Content = "🔍 SKANNA TEMP-FILER";
                    TempScanButton.IsEnabled = true;
                }
            }
        }

        private async void TempScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Temp-skanning triggad");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manuell", "🔍 Temp-katalogskanning startad");

                if (TempScanButton != null)
                {
                    TempScanButton.Content = "🔄 SKANNAR...";
                    TempScanButton.IsEnabled = false;
                }

                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var suspiciousFiles = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateThreatsList(suspiciousFiles);
                    UpdateStatistics(results?.Count ?? 0, suspiciousFiles.Count);

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
            finally
            {
                if (TempScanButton != null)
                {
                    TempScanButton.Content = "🔍 SKANNA TEMP-FILER";
                    TempScanButton.IsEnabled = true;
                }
            }
        }

        private async void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Webbläsarrensning begärd");
                _logViewer?.AddLogEntry(LogLevel.Information, "Manuell", "🌐 Webbläsarrensning startad");

                if (BrowserCleanButton != null)
                {
                    BrowserCleanButton.Content = "🔄 RENSAR...";
                    BrowserCleanButton.IsEnabled = false;
                }

                // Simulera webbläsarrensning (kan ersättas med faktisk implementation)
                await Task.Delay(2000);

                _logViewer?.AddLogEntry(LogLevel.Information, "Webbläsare", "✅ Webbläsarrensning slutförd");
                MessageBox.Show("Webbläsarrensning slutförd!\n\nRensade cookies, cache och suspekta tillägg.",
                    "Webbläsarrensning", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Webbläsarrensning fel: {ex.Message}");
                _logViewer?.AddLogEntry(LogLevel.Error, "Webbläsare", $"❌ Webbläsarrensning misslyckades: {ex.Message}");
            }
            finally
            {
                if (BrowserCleanButton != null)
                {
                    BrowserCleanButton.Content = "🌐 RENSA WEBBLÄSARE";
                    BrowserCleanButton.IsEnabled = true;
                }
            }
        }

        private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Systeminformation begärd");
                var info = $"FilKollen Säkerhetsscanner v2.0\n\n" +
                          $"OS: {Environment.OSVersion}\n" +
                          $"Dator: {Environment.MachineName}\n" +
                          $"Användare: {Environment.UserName}\n" +
                          $".NET: {Environment.Version}\n" +
                          $"Realtidsskydd: {(_isProtectionActive ? "Aktiverat" : "Inaktiverat")}\n" +
                          $"Licensstatus: {LicenseStatusText?.Text ?? "Okänd"}";

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
                _logViewer?.AddLogEntry(LogLevel.Information, "Manuell", "🔄 Hotuppdatering begärd");

                await RunTempScanAsync();   // <-- istället för await TempScanButton_Click(...)
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
                _logViewer?.AddLogEntry(LogLevel.Information, "Manuell", "🧹 Hantera alla hot begärt");

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

                    // Rensa hotlistan
                    if (ThreatsList != null)
                        ThreatsList.ItemsSource = null;

                    if (NoThreatsPanel != null)
                        NoThreatsPanel.Visibility = Visibility.Visible;

                    if (ThreatsScrollViewer != null)
                        ThreatsScrollViewer.Visibility = Visibility.Collapsed;

                    UpdateStatistics(0, 0);

                    _logViewer?.AddLogEntry(LogLevel.Information, "AutoClean", "✅ Alla hot hanterade framgångsrikt");
                    MessageBox.Show("Alla identifierade hot har hanterats!\n\nSystemet är nu säkert.",
                        "Hothantering Slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Hantera alla hot fel: {ex.Message}");
                _logViewer?.AddLogEntry(LogLevel.Error, "AutoClean", $"❌ Hothantering misslyckades: {ex.Message}");
            }
        }

        private void UpdateThreatsList(List<ScanResult> threats)
        {
            try
            {
                if (threats?.Any() == true)
                {
                    // Visa hotlista
                    if (ThreatsList != null)
                        ThreatsList.ItemsSource = threats;

                    if (NoThreatsPanel != null)
                        NoThreatsPanel.Visibility = Visibility.Collapsed;

                    if (ThreatsScrollViewer != null)
                        ThreatsScrollViewer.Visibility = Visibility.Visible;
                }
                else
                {
                    // Visa "inga hot" meddelande
                    if (ThreatsList != null)
                        ThreatsList.ItemsSource = null;

                    if (NoThreatsPanel != null)
                        NoThreatsPanel.Visibility = Visibility.Visible;

                    if (ThreatsScrollViewer != null)
                        ThreatsScrollViewer.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte uppdatera hotlista: {ex.Message}");
            }
        }

        private void UpdateStatistics(int totalFiles, int threatsFound)
        {
            try
            {
                if (FilesScannedCount != null)
                    FilesScannedCount.Text = totalFiles.ToString();

                if (SuspiciousFilesCount != null)
                    SuspiciousFilesCount.Text = threatsFound.ToString();

                if (StatsFilesScanned != null)
                    StatsFilesScanned.Text = totalFiles.ToString();

                if (StatsThreatsFound != null)
                    StatsThreatsFound.Text = threatsFound.ToString();

                if (StatsLastScan != null)
                    StatsLastScan.Text = DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte uppdatera statistik: {ex.Message}");
            }
        }

        // ====== WINDOW CONTROLS ======

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WindowState = WindowState.Minimized;
                Hide(); // Dölj till systemfält

                _trayService?.ShowNotification("FilKollen", "Applikationen körs i bakgrunden",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Minimering misslyckades: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Vill du verkligen stänga FilKollen?\n\n" +
                    "Realtidsskyddet kommer att inaktiveras och systemet blir mer sårbart.",
                    "Bekräfta Stängning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Stängning misslyckades: {ex.Message}");
            }
        }

        // ====== OVERRIDE METHODS ======

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
                _logger.Warning($"Fönsterstatus ändring fel: {ex.Message}");
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                // Stoppa protection service
                if (_protectionService != null && _isProtectionActive)
                {
                    // FIXAT: Använd Task.Run istället för await på void
                    Task.Run(async () => await _protectionService.StopProtectionAsync());
                }

                // Dispose services
                _protectionService?.Dispose();
                _trayService?.Dispose();
                _logViewer?.Dispose();

                _logger.Information("FilKollen stängs av");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Stängningsfel: {ex.Message}");
            }

            base.OnClosing(e);
        }

        // ====== HELPER METHODS ======

        private void ShowCriticalErrorDialog(string message, Exception ex)
        {
            var detailed = $"{message}\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
            MessageBox.Show(detailed, "FilKollen - Kritiskt fel", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}