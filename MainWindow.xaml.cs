using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FilKollen.Models;
using FilKollen.Services;
using FilKollen.Windows;
using Serilog;
using System.Windows.Media;
using System.Windows.Input;
using System.Net.Http;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ILogger _logger;
        private readonly LicenseService? _licenseService;
        private readonly BrandingService? _brandingService;
        private readonly ThemeService? _themeService;
        private readonly AppConfig _config;
        private TempFileScanner? _fileScanner;
        private LogViewerService? _logViewer;
        private QuarantineManager? _quarantine;
        private AdvancedBrowserCleaner? _browserCleaner;

        private RealTimeProtectionService? _protectionService;
        private SystemTrayService? _trayService;
        private IntrusionDetectionService? _intrusionDetection;

        private bool _isProtectionActive = false;
        private bool _isIpProtectionActive = false;
        private readonly Timer _statusUpdateTimer;
        private readonly List<ScanResult> _currentThreats = new();
        private string _currentSortColumn = "";
        private bool _sortAscending = true;

        // TA BORT UI-referenser - de skapas automatiskt från XAML
        // Dessa tas bort för att undvika dubletter

        public MainWindow() : this(null, null, null) { }

        public MainWindow(LicenseService? licenseService, BrandingService? brandingService, ThemeService? themeService)
        {
            try
            {
                _logger = Log.Logger ?? throw new InvalidOperationException("Logger inte initierad");
                _logger.Information("MainWindow startar med återställd elegant design v2.1");

                _licenseService = licenseService;
                _brandingService = brandingService;
                _themeService = themeService;

                _config = InitializeConfig();
                InitializeServices();
                InitializeComponent();
                InitializeBrandingFixed();
                InitializeTheme();

                DataContext = this;
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

        private AppConfig InitializeConfig()
        {
            return new AppConfig
            {
                ScanPaths = new List<string>
                {
                    Environment.GetEnvironmentVariable("TEMP") ?? System.IO.Path.GetTempPath(),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                    @"C:\Windows\Temp"
                },
                SuspiciousExtensions = new List<string> { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr" }
            };
        }

        private void InitializeServices()
        {
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
        }

        private void InitializeBrandingFixed()
        {
            try
            {
                var branding = _brandingService?.GetCurrentBranding();
                var logoPath = "Resources/Branding/default-logo.png";

                bool logoExists = File.Exists(logoPath);
                bool logoLoadable = false;

                if (logoExists)
                {
                    try
                    {
                        var testImage = new System.Windows.Media.Imaging.BitmapImage(new Uri(logoPath, UriKind.RelativeOrAbsolute));
                        logoLoadable = testImage.PixelWidth > 1 && testImage.PixelHeight > 1;
                    }
                    catch
                    {
                        logoLoadable = false;
                    }
                }

                var brandLogo = FindName("BrandLogo") as Button;
                var brandFallback = FindName("BrandFallback") as StackPanel;

                if (logoExists && logoLoadable)
                {
                    if (brandLogo != null && brandFallback != null)
                    {
                        var image = brandLogo.Content as Image;
                        if (image == null)
                        {
                            image = new Image();
                            brandLogo.Content = image;
                        }
                        image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(logoPath, UriKind.RelativeOrAbsolute));
                        brandLogo.Visibility = Visibility.Visible;
                        brandFallback.Visibility = Visibility.Collapsed;
                        _logger.Information($"Branding logo visas: {logoPath}");
                    }
                }
                else
                {
                    if (brandLogo != null && brandFallback != null)
                    {
                        brandLogo.Visibility = Visibility.Collapsed;
                        brandFallback.Visibility = Visibility.Visible;
                        _logger.Information("Använder FILKOLLEN-text (ingen giltig logga hittades)");
                    }
                }

                LoadHiDPILogo();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte ladda branding: {ex.Message}");
                var brandLogo = FindName("BrandLogo") as Button;
                var brandFallback = FindName("BrandFallback") as StackPanel;
                if (brandLogo != null && brandFallback != null)
                {
                    brandLogo.Visibility = Visibility.Collapsed;
                    brandFallback.Visibility = Visibility.Visible;
                }
            }
        }

        private void LoadHiDPILogo()
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var logoFileName = dpi switch
                {
                    >= 2.0 => "logo@2x.png",
                    >= 1.5 => "logo@1.5x.png",
                    _ => "logo.png"
                };

                var logoPath = Path.Combine("Resources", "Branding", logoFileName);
                if (File.Exists(logoPath))
                {
                    var brandLogo = FindName("BrandLogo") as Button;
                    var brandFallback = FindName("BrandFallback") as StackPanel;

                    var image = brandLogo?.Content as Image ?? new Image();
                    // FIX: Ta bort UriKind parameter för RelativeOrAbsolute
                    image.Source = new BitmapImage(new Uri(logoPath, UriKind.Relative));
                    if (brandLogo != null)
                    {
                        brandLogo.Content = image;
                        brandLogo.Visibility = Visibility.Visible;
                    }
                    if (brandFallback != null)
                    {
                        brandFallback.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Hi-DPI logo loading failed: {ex.Message}");
            }
        }

        private void BrandLogo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var branding = _brandingService?.GetCurrentBranding();
                if (!string.IsNullOrEmpty(branding?.Website))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(branding.Website) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not open website: {ex.Message}");
            }
        }

        private void InitializeTheme()
        {
            var themeToggle = FindName("ThemeToggle") as ToggleButton;
            if (_themeService != null && themeToggle != null)
            {
                var isDarkTheme = _themeService.Mode == ThemeMode.Dark ||
                                 (_themeService.Mode == ThemeMode.System && DetectSystemDarkMode());

                themeToggle.IsChecked = isDarkTheme;
                _themeService.ThemeChanged += OnThemeChanged;

                _logger.Information($"Tema initierat: {(isDarkTheme ? "Mörkt" : "Ljust")} tema");
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            var themeToggle = FindName("ThemeToggle") as ToggleButton;
            if (themeToggle != null && _themeService != null)
            {
                var isDarkTheme = _themeService.Mode == ThemeMode.Dark ||
                                 (_themeService.Mode == ThemeMode.System && DetectSystemDarkMode());
                themeToggle.IsChecked = isDarkTheme;
            }
        }

        private bool DetectSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
                return appsUseLightTheme is int lightTheme && lightTheme == 0;
            }
            catch
            {
                return false;
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

                _ = Task.Run(async () => await MonitorConnectionStatus());
                _ = Task.Run(async () => await PerformEnhancedStartupScanAsync());

                _logger.Information("MainWindow fullständigt initierat med elegant design");
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
                if (_fileScanner != null && _quarantine != null && _logViewer != null)
                {
                    _intrusionDetection = new IntrusionDetectionService(_logger, _logViewer, _fileScanner, _quarantine);
                    _protectionService = new RealTimeProtectionService(_fileScanner, _quarantine, _logViewer, _logger, _config);
                    _logger.Information("Protection services initierade");
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
                UpdateSecurityStatus(isSecure: true, threatsCount: 0);

                if (_licenseService != null)
                {
                    var status = await _licenseService.ValidateLicenseAsync();
                    UpdateLicenseDisplay(status);
                }

                var lastScanText = FindName("LastScanText") as TextBlock;
                if (lastScanText != null)
                    lastScanText.Text = "Aldrig";

                var threatsHandledText = FindName("ThreatsHandledText") as TextBlock;
                if (threatsHandledText != null)
                    threatsHandledText.Text = "0";

                _logger.Information("UI initierat med elegant design");
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
                _isIpProtectionActive = false;

                UpdateProtectionToggles();

                _logViewer?.AddLogEntry(LogLevel.Information, "System",
                    "✅ FilKollen redo - aktivera auto-skydd för fullständigt skydd");
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

        private void UpdateStatusCallback(object? state)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    var connectionStatusText = FindName("ConnectionStatusText") as TextBlock;
                    if (connectionStatusText != null)
                    {
                        connectionStatusText.Text = "ONLINE";
                        connectionStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    }

                    if (_protectionService != null)
                    {
                        var threatsHandledText = FindName("ThreatsHandledText") as TextBlock;
                        if (threatsHandledText != null)
                        {
                            var stats = _protectionService.GetProtectionStats();
                            threatsHandledText.Text = stats.TotalThreatsHandled.ToString();
                        }
                    }

                    UpdateTrialBadge();
                });
            }
            catch (Exception ex)
            {
                _logger.Debug($"Status update error: {ex.Message}");
            }
        }

        // Connection Status Management
        public enum ConnectionStatus
        {
            Online,
            Offline,
            Connecting,
            Error
        }

        private readonly Dictionary<ConnectionStatus, (string Text, string Description, Brush Color)> _statusMessages = new()
        {
            [ConnectionStatus.Online] = ("ONLINE", "Licensserver ansluten • Skydd aktivt", Brushes.Green),
            [ConnectionStatus.Offline] = ("OFFLINE", "Ingen internetanslutning • Lokalt skydd aktivt", Brushes.Orange),
            [ConnectionStatus.Connecting] = ("ANSLUTER", "Kontrollerar anslutning...", Brushes.Blue),
            [ConnectionStatus.Error] = ("FEL", "Anslutningsproblem • Kontrollera nätverk", Brushes.Red)
        };

        private void UpdateConnectionStatus(ConnectionStatus status)
        {
            var (text, description, color) = _statusMessages[status];

            var connectionStatusText = FindName("ConnectionStatusText") as TextBlock;
            if (connectionStatusText != null)
            {
                connectionStatusText.Text = text;
                connectionStatusText.Foreground = color;
            }

            if (FindName("StatusDot") is Ellipse statusDot)
            {
                statusDot.Fill = color;
            }

            if (FindName("OnlineStatusBadge") is Border statusBadge)
            {
                statusBadge.ToolTip = new ToolTip
                {
                    Content = new StackPanel
                    {
                        Children = {
                            new TextBlock { Text = $"Status: {text}", FontWeight = FontWeights.SemiBold },
                            new TextBlock { Text = description, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, MaxWidth = 200 },
                            new TextBlock { Text = $"Senast kontrollerad: {DateTime.Now:HH:mm:ss}", FontSize = 10, Opacity = 0.6 }
                        }
                    }
                };
            }
        }
        private async Task MonitorConnectionStatus()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(30000);

                    var isOnline = await CheckInternetConnection();
                    LicenseStatus? licenseStatus = null;
                    if (_licenseService != null)
                        licenseStatus = await _licenseService.ValidateLicenseAsync();

                    // Simplified approach - no switch expression
                    ConnectionStatus status;
                    if (isOnline)
                    {
                        status = ConnectionStatus.Online;  // Online regardless of license status
                    }
                    else
                    {
                        status = ConnectionStatus.Offline;
                    }

                    Application.Current.Dispatcher.Invoke(() => UpdateConnectionStatus(status));
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Connection status check error: {ex.Message}");
                    Application.Current.Dispatcher.Invoke(() => UpdateConnectionStatus(ConnectionStatus.Error));
                }
            }
        }
        private async Task<bool> CheckInternetConnection()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetAsync("https://www.google.com");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // Trial Badge Management
        private void UpdateTrialBadge()
        {
            var trialTime = _licenseService?.GetRemainingTrialTime();
            var isTrialMode = trialTime.HasValue && trialTime.GetValueOrDefault() > TimeSpan.Zero;

            if (FindName("TrialBadge") is Border trialBadge &&
                FindName("TrialBadgeText") is TextBlock trialText)
            {
                if (isTrialMode && trialTime.HasValue) // Fixed: Added null check
                {
                    trialBadge.Visibility = Visibility.Visible;

                    var days = (int)trialTime.Value.TotalDays;
                    var hours = trialTime.Value.Hours;

                    trialText.Text = days > 0
                        ? $"PROVPERIOD: {days} DAGAR KVAR"
                        : $"PROVPERIOD: {hours} TIMMAR KVAR";

                    trialBadge.ToolTip = new ToolTip
                    {
                        Content = new StackPanel
                        {
                            Children = {
                        new TextBlock { Text = "Provperiod aktiv", FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = $"{FormatTimeSpan(trialTime.Value)} kvar" },
                        new TextBlock { Text = $"Slutar: {DateTime.UtcNow.Add(trialTime.Value):yyyy-MM-dd HH:mm}", Opacity = 0.8 }
                    }
                        }
                    };
                }
                else
                {
                    trialBadge.Visibility = Visibility.Collapsed;
                }
            }
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} dagar";
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} timmar";
            return $"{(int)timeSpan.TotalMinutes} minuter";
        }

        // Event Handlers
        private async void QuickScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var quickScanButton = FindName("QuickScanButton") as Button;
                if (quickScanButton != null)
                {
                    quickScanButton.Content = "🔄 SÖKER...";
                    quickScanButton.IsEnabled = false;
                }

                var scanningIndicator = FindName("ScanningIndicator") as StackPanel;
                if (scanningIndicator != null)
                    scanningIndicator.Visibility = Visibility.Visible;

                var scanProgress = FindName("ScanProgress") as System.Windows.Controls.ProgressBar;
                if (scanProgress != null)
                    scanProgress.Value = 0;

                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔍 Manuell skanning startad");

                if (_fileScanner != null)
                {
                    for (int i = 0; i <= 100; i += 20)
                    {
                        if (scanProgress != null)
                            scanProgress.Value = i;
                        await Task.Delay(300);
                    }

                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateThreatsDisplay(threats);

                    var lastScanText = FindName("LastScanText") as TextBlock;
                    if (lastScanText != null)
                        lastScanText.Text = DateTime.Now.ToString("HH:mm");

                    if (threats.Any())
                    {
                        _logViewer?.AddLogEntry(LogLevel.Warning, "Scan",
                            $"⚠️ Manuell skanning: {threats.Count} hot funna");

                        ShowInAppNotification($"⚠️ {threats.Count} hot upptäckta!", NotificationType.Warning);
                    }
                    else
                    {
                        _logViewer?.AddLogEntry(LogLevel.Information, "Scan",
                            "✅ Manuell skanning: Inga hot funna");

                        ShowInAppNotification("✅ Inga hot funna - systemet är säkert", NotificationType.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Manuell skanning fel: {ex.Message}");
                ShowInAppNotification("❌ Skanning misslyckades", NotificationType.Error);
            }
            finally
            {
                var scanningIndicator = FindName("ScanningIndicator") as StackPanel;
                if (scanningIndicator != null)
                    scanningIndicator.Visibility = Visibility.Collapsed;

                var quickScanButton = FindName("QuickScanButton") as Button;
                if (quickScanButton != null)
                {
                    quickScanButton.Content = "🔍 Sök efter hot";
                    quickScanButton.IsEnabled = true;
                }
            }
        }

        private async void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button ?? FindName("BrowserCleanTileButton") as Button;
                if (button != null)
                {
                    button.Content = "🔄 RENSAR...";
                    button.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "BrowserClean",
                    "🌐 RENSA FALSKA AVISERINGAR STARTAD");

                if (_browserCleaner != null)
                {
                    var result = await _browserCleaner.DeepCleanAllBrowsersAsync();

                    if (result.Success)
                    {
                        var message = $"✅ {result.MalwareNotificationsRemoved} falska aviseringar rensade";
                        ShowInAppNotification(message, NotificationType.Success);

                        _logViewer?.AddLogEntry(LogLevel.Information, "BrowserClean",
                            $"✅ Falska aviseringar rensade: {result.MalwareNotificationsRemoved} st");

                        _trayService?.ShowNotification("Falska aviseringar rensade",
                            $"{result.MalwareNotificationsRemoved} malware-notiser borttagna",
                            System.Windows.Forms.ToolTipIcon.Info);
                    }
                    else
                    {
                        ShowInAppNotification("❌ Rensning misslyckades", NotificationType.Error);
                        _logViewer?.AddLogEntry(LogLevel.Error, "BrowserClean",
                            "❌ Webbläsarrensning misslyckades");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid webbläsarrensning: {ex.Message}");
                ShowInAppNotification("❌ Rensning misslyckades", NotificationType.Error);
            }
            finally
            {
                var button = sender as Button ?? FindName("BrowserCleanTileButton") as Button;
                if (button != null)
                {
                    button.Content = "🌐 Rensa bluffnotiser";
                    button.IsEnabled = true;
                }
            }
        }

        private async void HandleAllThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var handleAllThreatsButton = FindName("HandleAllThreatsButton") as Button;
                if (handleAllThreatsButton != null)
                {
                    handleAllThreatsButton.Content = "🔄 Tar bort alla...";
                    handleAllThreatsButton.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "ThreatAction",
                    "🧹 Tar bort alla upptäckta hot automatiskt");

                var threatsToHandle = new List<ScanResult>(_currentThreats);
                int handledCount = 0;

                foreach (var threat in threatsToHandle)
                {
                    try
                    {
                        if (_quarantine != null)
                        {
                            var success = await _quarantine.DeleteFileAsync(threat);
                            if (success)
                            {
                                handledCount++;
                                _currentThreats.Remove(threat);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Kunde inte hantera hot {threat.FileName}: {ex.Message}");
                    }
                }

                UpdateThreatsDisplay(_currentThreats);

                var threatsHandledText = FindName("ThreatsHandledText") as TextBlock;
                if (threatsHandledText != null)
                {
                    var currentHandled = int.Parse(threatsHandledText.Text);
                    threatsHandledText.Text = (currentHandled + handledCount).ToString();
                }

                var message = $"✅ {handledCount} hot har tagits bort";
                ShowInAppNotification(message, NotificationType.Success);

                _logViewer?.AddLogEntry(LogLevel.Information, "ThreatAction",
                    $"✅ {handledCount} hot har hanterats framgångsrikt");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hantering av alla hot: {ex.Message}");
                ShowInAppNotification("❌ Fel vid borttagning av hot", NotificationType.Error);
            }
            finally
            {
                var handleAllThreatsButton = FindName("HandleAllThreatsButton") as Button;
                if (handleAllThreatsButton != null)
                {
                    handleAllThreatsButton.Content = "🧹 Ta bort alla hot";
                    handleAllThreatsButton.IsEnabled = true;
                }
            }
        }

        private async void RefreshScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var refreshScanButton = FindName("RefreshScanButton") as Button;
                if (refreshScanButton != null)
                {
                    refreshScanButton.Content = "🔄 Skannar...";
                    refreshScanButton.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔄 Uppdaterar hotskanning");

                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateThreatsDisplay(threats);

                    var lastScanText = FindName("LastScanText") as TextBlock;
                    if (lastScanText != null)
                        lastScanText.Text = DateTime.Now.ToString("HH:mm");

                    if (threats.Any())
                    {
                        ShowInAppNotification($"🔄 Uppdatering: {threats.Count} hot funna", NotificationType.Warning);
                    }
                    else
                    {
                        ShowInAppNotification("🔄 Uppdatering: Inga hot funna", NotificationType.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hotskanning: {ex.Message}");
                ShowInAppNotification("❌ Uppdatering misslyckades", NotificationType.Error);
            }
            finally
            {
                var refreshScanButton = FindName("RefreshScanButton") as Button;
                if (refreshScanButton != null)
                {
                    refreshScanButton.Content = "🔄 Skanna om";
                    refreshScanButton.IsEnabled = true;
                }
            }
        }

        private void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            _isProtectionActive = true;
            _protectionService?.StartProtectionAsync();
            _logViewer?.AddLogEntry(LogLevel.Information, "Protection", "🛡️ Auto-skydd AKTIVERAT");
        }

        private void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _isProtectionActive = false;
            _protectionService?.StopProtectionAsync();
            _logViewer?.AddLogEntry(LogLevel.Warning, "Protection", "⚠️ Auto-skydd INAKTIVERAT");
        }

        private void IpProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            _isIpProtectionActive = true;
            _logViewer?.AddLogEntry(LogLevel.Information, "IPProtection", "🌐 IP-skydd AKTIVERAT");
        }

        private void IpProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _isIpProtectionActive = false;
            _logViewer?.AddLogEntry(LogLevel.Warning, "IPProtection", "⚠️ IP-skydd INAKTIVERAT");
        }

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            _themeService?.ApplyTheme(ThemeMode.Dark);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _themeService?.ApplyTheme(ThemeMode.Light);
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var helpUrl = "https://wiki.filkollen.se";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(helpUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to open help URL: {ex.Message}");
                var helpMessage =
                    "🔑 FILKOLLEN HJÄLP\n\n" +
                    "Auto-skydd:\n" +
                    "• Aktivera för kontinuerlig övervakning\n" +
                    "• Automatisk hantering av upptäckta hot\n\n" +
                    "Manuell skanning:\n" +
                    "• Klicka 'Sök efter hot' för omedelbar kontroll\n" +
                    "• Visar alla upptäckta säkerhetshot\n\n" +
                    "Webbläsarrensning:\n" +
                    "• Tar bort falska varningar och bluffnotiser\n" +
                    "• Säker för alla populära webbläsare\n\n" +
                    "Support: support@filkollen.se";

                MessageBox.Show(helpMessage, "FilKollen Hjälp",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Window Controls
        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize)
                {
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                }
                else
                {
                    DragMove();
                }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // Enhanced UI Updates
        private void UpdateThreatsDisplay(List<ScanResult> threats)
        {
            _currentThreats.Clear();
            _currentThreats.AddRange(threats);

            if (threats.Any())
            {
                ShowThreatsView(threats);
            }
            else
            {
                ShowSafeView();
            }

            InitializeTableHeaders();
        }

        private void ShowThreatsView(List<ScanResult> threats)
        {
            var safeStatusPanel = FindName("SafeStatusPanel") as StackPanel;
            if (safeStatusPanel != null)
                safeStatusPanel.Visibility = Visibility.Collapsed;

            var threatsPanel = FindName("ThreatsPanel") as StackPanel;
            if (threatsPanel != null)
                threatsPanel.Visibility = Visibility.Visible;

            UpdateThreatCounter(threats.Count);
            UpdateMainStatus("HOT UPPTÄCKTA", Colors.Orange, $"{threats.Count} hot kräver åtgärd");
            BuildThreatsTableEnhanced(threats);
        }

        private void ShowSafeView()
        {
            var safeStatusPanel = FindName("SafeStatusPanel") as StackPanel;
            if (safeStatusPanel != null)
                safeStatusPanel.Visibility = Visibility.Visible;

            var threatsPanel = FindName("ThreatsPanel") as StackPanel;
            if (threatsPanel != null)
                threatsPanel.Visibility = Visibility.Collapsed;

            var threatCounter = FindName("ThreatCounter") as Border;
            if (threatCounter != null)
                threatCounter.Visibility = Visibility.Collapsed;

            UpdateMainStatus("SYSTEMET ÄR SÄKERT", Colors.Green,
                $"0 hot funna • Auto-skydd {(_isProtectionActive ? "aktivt" : "inaktivt")}");
        }

        private void UpdateThreatCounter(int count)
        {
            var threatCounter = FindName("ThreatCounter") as Border;
            var threatCountText = FindName("ThreatCountText") as TextBlock;

            if (threatCounter != null && threatCountText != null)
            {
                threatCounter.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
                threatCountText.Text = $"{count} HOT";
            }
        }

        private void UpdateMainStatus(string statusText, Color color, string subText)
        {
            var statusIndicator = FindName("StatusIndicator") as Ellipse;
            if (statusIndicator != null)
                statusIndicator.Fill = new SolidColorBrush(color);

            var statusMainText = FindName("StatusMainText") as TextBlock;
            if (statusMainText != null)
            {
                statusMainText.Text = statusText;
                statusMainText.Foreground = new SolidColorBrush(color);
            }

            var statusSubText = FindName("StatusSubText") as TextBlock;
            if (statusSubText != null)
                statusSubText.Text = subText;
        }

        private void BuildThreatsTableEnhanced(List<ScanResult> threats)
        {
            var threatsList = FindName("ThreatsList") as StackPanel;
            if (threatsList == null) return;

            threatsList.Children.Clear();

            foreach (var threat in threats)
            {
                var row = CreateThreatRow(threat);
                threatsList.Children.Add(row);
            }
        }

        private Border CreateThreatRow(ScanResult threat)
        {
            var row = new Border
            {
                Background = TryFindResource("FK.Brush.Surface") as Brush ?? SystemColors.WindowBrush,
                BorderBrush = TryFindResource("FK.Brush.Border") as Brush ?? SystemColors.ControlDarkBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 8, 0, 8),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            // File name
            var fileName = new TextBlock
            {
                Text = threat.FileName,
                Style = TryFindResource("FK.Style.TableCell") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(fileName, 0);
            grid.Children.Add(fileName);

            // File type
            var fileType = new TextBlock
            {
                Text = GetFileTypeDisplay(threat.FileName),
                Style = TryFindResource("FK.Style.TableCell") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(fileType, 1);
            grid.Children.Add(fileType);

            // File size
            var fileSize = new TextBlock
            {
                Text = threat.FormattedSize ?? "Unknown",
                Style = TryFindResource("FK.Style.TableCell") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(fileSize, 2);
            grid.Children.Add(fileSize);

            // File path
            var filePath = new TextBlock
            {
                Text = Path.GetDirectoryName(threat.FilePath) ?? "",
                Style = TryFindResource("FK.Style.TableCell") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(filePath, 3);
            grid.Children.Add(filePath);

            // Created date
            var createdDate = new TextBlock
            {
                Text = threat.CreatedDate.ToString("yyyy-MM-dd"),
                Style = TryFindResource("FK.Style.TableCell") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(createdDate, 4);
            grid.Children.Add(createdDate);

            // Threat level badge
            var threatBadge = CreateThreatLevelBadge(threat.ThreatLevel);
            Grid.SetColumn(threatBadge, 5);
            grid.Children.Add(threatBadge);

            // Action button
            var actionButton = new Button
            {
                Content = "Ta bort",
                Style = TryFindResource("FK.Style.DangerButton") as Style,
                Tag = threat,
                Margin = new Thickness(4),
                Padding = new Thickness(8, 4, 8, 4)
            };
            actionButton.Click += DeleteThreatButton_Click;
            Grid.SetColumn(actionButton, 6);
            grid.Children.Add(actionButton);

            row.Child = grid;
            return row;
        }

        private async void DeleteThreatButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ScanResult threat)
            {
                try
                {
                    var result = MessageBox.Show(
                        $"Vill du ta bort denna fil permanent?\n\n{threat.FileName}\n\nDenna åtgärd kan inte ångras.",
                        "Bekräfta borttagning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        button.IsEnabled = false;
                        button.Content = "🔄 Tar bort...";

                        if (_quarantine != null)
                        {
                            var success = await _quarantine.DeleteFileAsync(threat);
                            if (success)
                            {
                                _currentThreats.Remove(threat);
                                UpdateThreatsDisplay(_currentThreats);

                                var threatsHandledText = FindName("ThreatsHandledText") as TextBlock;
                                if (threatsHandledText != null)
                                {
                                    var currentHandled = int.Parse(threatsHandledText.Text);
                                    threatsHandledText.Text = (currentHandled + 1).ToString();
                                }

                                ShowInAppNotification($"✅ {threat.FileName} har tagits bort", NotificationType.Success);

                                _logViewer?.AddLogEntry(LogLevel.Information, "ThreatAction",
                                    $"🗑️ Hot raderat: {threat.FileName}");
                            }
                            else
                            {
                                ShowInAppNotification($"❌ Kunde inte ta bort {threat.FileName}", NotificationType.Error);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid radering av hot: {ex.Message}");
                    ShowInAppNotification($"❌ Fel vid borttagning: {ex.Message}", NotificationType.Error);
                }
                finally
                {
                    button.IsEnabled = true;
                    button.Content = "Ta bort";
                }
            }
        }

        private Border CreateThreatLevelBadge(ThreatLevel level)
        {
            var badge = new Border
            {
                Style = TryFindResource("FK.Style.ThreatBadge") as Style,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var text = new TextBlock
            {
                Style = TryFindResource("FK.Style.BadgeText") as Style,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Use system brushes as fallbacks if custom resources don't exist
            switch (level)
            {
                case ThreatLevel.Critical:
                    badge.Background = TryFindResource("FK.Brush.Danger") as Brush ??
                                      new SolidColorBrush(Color.FromRgb(220, 53, 69));
                    text.Text = "KRITISK";
                    text.Foreground = Brushes.White;
                    break;
                case ThreatLevel.High:
                    badge.Background = TryFindResource("FK.Brush.Danger") as Brush ??
                                      new SolidColorBrush(Color.FromRgb(220, 53, 69));
                    text.Text = "HÖG";
                    text.Foreground = Brushes.White;
                    break;
                case ThreatLevel.Medium:
                    badge.Background = TryFindResource("FK.Brush.Warning") as Brush ??
                                      new SolidColorBrush(Color.FromRgb(255, 193, 7));
                    text.Text = "MEDIUM";
                    text.Foreground = Brushes.Black;
                    break;
                default:
                    badge.Background = TryFindResource("FK.Brush.StatusNeutral") as Brush ??
                                      new SolidColorBrush(Color.FromRgb(108, 117, 125));
                    text.Text = "LÅG";
                    text.Foreground = Brushes.White;
                    break;
            }

            badge.Child = text;
            return badge;
        }

        private string GetFileTypeDisplay(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToUpperInvariant();
            return extension switch
            {
                ".EXE" => "EXE",
                ".BAT" => "BAT",
                ".CMD" => "CMD",
                ".PS1" => "PS1",
                ".VBS" => "VBS",
                ".SCR" => "SCR",
                _ => extension?.Replace(".", "") ?? "FILE"
            };
        }

        // Table Sorting Implementation
        private void InitializeTableHeaders()
        {
            MakeHeaderSortable(FindName("FileNameHeader") as Button, "FileName");
            MakeHeaderSortable(FindName("TypeHeader") as Button, "FileType");
            MakeHeaderSortable(FindName("SizeHeader") as Button, "FileSize");
            MakeHeaderSortable(FindName("PathHeader") as Button, "FilePath");
            MakeHeaderSortable(FindName("DateHeader") as Button, "CreatedDate");
            MakeHeaderSortable(FindName("RiskHeader") as Button, "ThreatLevel");
        }

        private void MakeHeaderSortable(Button? header, string sortProperty)
        {
            if (header == null) return;

            header.Click += (s, e) => SortByColumn(sortProperty, header);
            header.Tag = sortProperty;
        }

        private void SortByColumn(string property, Button header)
        {
            ResetOtherHeaders(header);

            if (_currentSortColumn == property)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _currentSortColumn = property;
                _sortAscending = true;
            }

            UpdateHeaderSortIndicator(header, _sortAscending);
            SortThreats(property, _sortAscending);

            _logViewer?.AddLogEntry(LogLevel.Debug, "UI",
                $"Sorterat hot efter {property} ({(_sortAscending ? "stigande" : "fallande")})");
        }

        private void ResetOtherHeaders(Button activeHeader)
        {
            var headers = new[] {
                FindName("FileNameHeader") as Button,
                FindName("TypeHeader") as Button,
                FindName("SizeHeader") as Button,
                FindName("PathHeader") as Button,
                FindName("DateHeader") as Button,
                FindName("RiskHeader") as Button
            };

            foreach (var header in headers)
            {
                if (header != null && header != activeHeader)
                {
                    header.Tag = header.Tag?.ToString()?.Replace("SortAsc", "").Replace("SortDesc", "");
                }
            }
        }

        private void UpdateHeaderSortIndicator(Button header, bool ascending)
        {
            header.Tag = ascending ? "SortAsc" : "SortDesc";
        }

        private void SortThreats(string property, bool ascending)
        {
            try
            {
                IEnumerable<ScanResult> sortedThreats = property switch
                {
                    "FileName" => ascending
                        ? _currentThreats.OrderBy(t => t.FileName)
                        : _currentThreats.OrderByDescending(t => t.FileName),

                    "FileType" => ascending
                        ? _currentThreats.OrderBy(t => GetFileTypeDisplay(t.FileName))
                        : _currentThreats.OrderByDescending(t => GetFileTypeDisplay(t.FileName)),

                    "FileSize" => ascending
                        ? _currentThreats.OrderBy(t => t.FileSize)
                        : _currentThreats.OrderByDescending(t => t.FileSize),

                    "FilePath" => ascending
                        ? _currentThreats.OrderBy(t => Path.GetDirectoryName(t.FilePath))
                        : _currentThreats.OrderByDescending(t => Path.GetDirectoryName(t.FilePath)),

                    "CreatedDate" => ascending
                        ? _currentThreats.OrderBy(t => t.CreatedDate)
                        : _currentThreats.OrderByDescending(t => t.CreatedDate),

                    "ThreatLevel" => ascending
                        ? _currentThreats.OrderBy(t => (int)t.ThreatLevel)
                        : _currentThreats.OrderByDescending(t => (int)t.ThreatLevel),

                    _ => _currentThreats
                };

                _currentThreats.Clear();
                _currentThreats.AddRange(sortedThreats);

                BuildThreatsTableEnhanced(_currentThreats);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid sortering: {ex.Message}");
            }
        }

        // Utility Methods
        private async Task PerformEnhancedStartupScanAsync()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var scanningIndicator = FindName("ScanningIndicator") as StackPanel;
                    if (scanningIndicator != null)
                        scanningIndicator.Visibility = Visibility.Visible;

                    var scanProgress = FindName("ScanProgress") as System.Windows.Controls.ProgressBar;
                    if (scanProgress != null)
                        scanProgress.Value = 0;
                });

                _logViewer?.AddLogEntry(LogLevel.Information, "Startup", "🔍 Förbättrad uppstartsskanning startad");

                for (int i = 0; i <= 100; i += 10)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var scanProgress = FindName("ScanProgress") as System.Windows.Controls.ProgressBar;
                        if (scanProgress != null)
                            scanProgress.Value = i;
                    });
                    await Task.Delay(200);
                }

                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var scanningIndicator = FindName("ScanningIndicator") as StackPanel;
                        if (scanningIndicator != null)
                            scanningIndicator.Visibility = Visibility.Collapsed;

                        UpdateThreatsDisplay(threats);

                        var lastScanText = FindName("LastScanText") as TextBlock;
                        if (lastScanText != null)
                            lastScanText.Text = DateTime.Now.ToString("HH:mm");
                    });

                    if (threats.Any())
                    {
                        ShowInAppNotification($"⚠️ {threats.Count} hot upptäckta under uppstartsskanning", NotificationType.Warning);
                        _logViewer?.AddLogEntry(LogLevel.Warning, "Startup", $"⚠️ {threats.Count} hot funna vid uppstart");
                    }
                    else
                    {
                        ShowInAppNotification("✅ Uppstartsskanning slutförd - inga hot funna", NotificationType.Success);
                        _logViewer?.AddLogEntry(LogLevel.Information, "Startup", "✅ Uppstartsskanning: Inga hot funna");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Uppstartsskanning misslyckades: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var scanningIndicator = FindName("ScanningIndicator") as StackPanel;
                    if (scanningIndicator != null)
                        scanningIndicator.Visibility = Visibility.Collapsed;
                    ShowInAppNotification("❌ Uppstartsskanning misslyckades", NotificationType.Error);
                });
            }
        }

        private void UpdateSecurityStatus(bool isSecure, int threatsCount)
        {
            var statusText = isSecure ? "SYSTEMET ÄR SÄKERT" : "HOT UPPTÄCKTA";
            var statusColor = isSecure ? Colors.Green : Colors.Orange;
            var subText = isSecure
                ? $"0 hot funna • Auto-skydd {(_isProtectionActive ? "aktivt" : "inaktivt")}"
                : $"{threatsCount} hot kräver åtgärd";

            UpdateMainStatus(statusText, statusColor, subText);
        }

        private void UpdateLicenseDisplay(LicenseStatus status)
        {
            _logger.Information($"License status: {status}");
        }

        private void UpdateProtectionToggles()
        {
            var protectionToggle = FindName("ProtectionToggle") as ToggleButton;
            if (protectionToggle != null)
                protectionToggle.IsChecked = _isProtectionActive;

            var ipProtectionToggle = FindName("IpProtectionToggle") as ToggleButton;
            if (ipProtectionToggle != null)
                ipProtectionToggle.IsChecked = _isIpProtectionActive;
        }

        // Notification system
        public enum NotificationType
        {
            Success,
            Warning,
            Error,
            Info
        }

        private void ShowInAppNotification(string message, NotificationType type)
        {
            _logger.Information($"Notification ({type}): {message}");
        }

        private void ShowErrorDialog(string message, Exception ex)
        {
            var detailed = $"{message}\n\n{ex.GetType().Name}: {ex.Message}";
            MessageBox.Show(detailed, "FilKollen - Fel", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
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