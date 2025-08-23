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
using System.Windows.Data;
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
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
    private ToastService? _toastService;

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

        private ICollectionView _threatsView;

        // ─────────────────────────────────────────────────────────────────────────────
        // Native interop för fönsterdrag/resize
        #region Native interop för fönsterdrag/resize
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;

        // HT-koder (non-client hit test)
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private void BeginResize(int htCode)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)htCode, IntPtr.Zero);
        }
        #endregion


        // === KANT-RESIZE (kopplad till fyra "osynliga" Borders i XAML) ===
        private void ResizeBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (sender is not System.Windows.Controls.Border b) return;

            switch (b.Name)
            {
                case "ResizeBorderTop":
                    BeginResize(HTTOP);
                    break;
                case "ResizeBorderBottom":
                    BeginResize(HTBOTTOM);
                    break;
                case "ResizeBorderLeft":
                    BeginResize(HTLEFT);
                    break;
                case "ResizeBorderRight":
                    BeginResize(HTRIGHT);
                    break;
                default:
                    break;
            }
        }

        // === Tangentgenvägar på fönsternivå ===
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // F1 = Hjälp
            if (e.Key == Key.F1)
            {
                HelpButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // Ctrl+W eller Esc = stäng
            if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W) || e.Key == Key.Escape)
            {
                Close_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // Ctrl+L = växla Ljus/Mörk (exempel)
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
            {
                // flippar toggle; utlös dina bef. handlers
                if (ThemeToggle != null)
                    ThemeToggle.IsChecked = !(ThemeToggle.IsChecked ?? false);
                e.Handled = true;
            }
        }

        // Om du vill ha särskild hantering på KeyDown (annat än Preview) – skicka vidare
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Låt PreviewKeyDown sköta allt för enkelhet:
            Window_PreviewKeyDown(sender, e);
        }

        // Property för trial mode binding
        public bool IsTrialMode { get; private set; }

        public MainWindow() : this(null, null, null) { }

        public MainWindow(LicenseService? licenseService, BrandingService? brandingService, ThemeService? themeService)
        {
            try
            {
                _logger = Log.Logger ?? throw new InvalidOperationException("Logger inte initierad");
                _logger.Information("MainWindow startar med förbättrad responsiv design v2.1");

                _licenseService = licenseService;
                _brandingService = brandingService;
                _themeService = themeService;
                _threatsView = CollectionViewSource.GetDefaultView(_currentThreats);
                _threatsView.Filter = null;
                _config = InitializeConfig();
                InitializeServices();
                InitializeComponent();
                        _toastService = new ToastService(this, _logger);
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
                if (_intrusionDetection != null)
{
    _intrusionDetection.IntrusionDetected += (s, e) => 
        _toastService?.ShowToast($"🚨 INTRÅNG: {e.ProcessName}", ToastType.Error);
}
                await InitializeUIAsync();
                await InitializeProtectionAsync();
                await InitializeTrayAsync();

                _ = Task.Run(async () => await MonitorConnectionStatus());
                _ = Task.Run(async () => await PerformEnhancedStartupScanAsync());

                _logger.Information("MainWindow fullständigt initierat med förbättrad responsiv design");
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
            
            // ✅ ENDAST EN GÅNG - koppla event handlers
            if (_intrusionDetection != null)
            {
                _intrusionDetection.IntrusionDetected += OnIntrusionDetected;
                _intrusionDetection.SecurityAlert += OnSecurityAlert;
            }
            
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

// ✅ Event handlers definierade UTANFÖR InitializeServicesAsync
private void OnIntrusionDetected(object? sender, IntrusionDetectedEventArgs e)
{
    ShowToast($"🚨 INTRÅNG: {e.ThreatType} - {e.ProcessName}", ToastType.Error);
}

private void OnSecurityAlert(object? sender, SecurityAlertEventArgs e)
{
    ShowToast($"⚠️ {e.AlertType}: {e.Message}", ToastType.Warning);
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

                // Uppdatera trial status
                CheckTrialStatus();

                _logger.Information("UI initierat med förbättrad responsiv design");
            }
            catch (Exception ex)
            {
                _logger.Warning($"UI initiation varning: {ex.Message}");
            }

            await Task.Delay(10);
        }
private async void SetupScheduledScanning()
{
    if (_config.EnableScheduling)
    {
        var scheduleManager = new ScheduleManager(_logger);
        await scheduleManager.CreateScheduledTaskAsync(_config);
    }
}
        // NY: Kontrollera trial status och uppdatera property
        private void CheckTrialStatus()
        {
            var trialTime = _licenseService?.GetRemainingTrialTime();
            IsTrialMode = trialTime.HasValue && trialTime.GetValueOrDefault() > TimeSpan.Zero;
            OnPropertyChanged(nameof(IsTrialMode));
        }

        // NY: Event handler för klickbar trial badge
        private void TrialBadgeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Öppnar licensregistreringsfönster från klickbar trial badge");

                if (_licenseService == null)
                {
                    _logger.Warning("LicenseService är null - kan inte öppna registreringsfönster");
                    ShowToast("❌ Licensservice inte tillgänglig", NotificationType.Error);
                    return;
                }

                var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                licenseWindow.Owner = this;

                var result = licenseWindow.ShowDialog();

                if (result == true)
                {
                    // Uppdatera UI efter licensregistrering
                    CheckTrialStatus();
                    UpdateTrialBadge();
                    _logger.Information("Licensregistrering genomförd från klickbar trial badge");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid öppning av licensregistrering från trial badge: {ex.Message}");
                ShowToast("❌ Kunde inte öppna licensregistrering", NotificationType.Error);
            }
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

                    ConnectionStatus status;
                    if (isOnline)
                    {
                        status = ConnectionStatus.Online;
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
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var response = await client.GetAsync("https://www.msftconnecttest.com/connecttest.txt");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // Trial Badge Management (förbättrad)
        private void UpdateTrialBadge()
        {
            var trialTime = _licenseService?.GetRemainingTrialTime();
            var isTrialMode = trialTime.HasValue && trialTime.GetValueOrDefault() > TimeSpan.Zero;

            // Uppdatera property för binding
            if (IsTrialMode != isTrialMode)
            {
                IsTrialMode = isTrialMode;
                OnPropertyChanged(nameof(IsTrialMode));
            }

            if (FindName("TrialBadgeText") is TextBlock trialText && isTrialMode && trialTime.HasValue)
            {
                var days = (int)trialTime.Value.TotalDays;
                var hours = trialTime.Value.Hours;

                trialText.Text = days > 0
                    ? $"PROVPERIOD: {days} DAGAR KVAR"
                    : $"PROVPERIOD: {hours} TIMMAR KVAR";

                // Tooltip för trial badge
                if (FindName("TrialBadgeButton") is Button trialButton)
                {
                    trialButton.ToolTip = new ToolTip
                    {
                        Content = new StackPanel
                        {
                            Children = {
                                new TextBlock { Text = "Provperiod aktiv - Klicka för att registrera licens", FontWeight = FontWeights.SemiBold },
                                new TextBlock { Text = $"{FormatTimeSpan(trialTime.Value)} kvar" },
                                new TextBlock { Text = $"Slutar: {DateTime.UtcNow.Add(trialTime.Value):yyyy-MM-dd HH:mm}", Opacity = 0.8 }
                            }
                        }
                    };
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

                        ShowToast($"⚠️ {threats.Count} hot upptäckta!", NotificationType.Warning);
                    }
                    else
                    {
                        _logViewer?.AddLogEntry(LogLevel.Information, "Scan",
                            "✅ Manuell skanning: Inga hot funna");

                        ShowToast("✅ Inga hot funna - systemet är säkert", NotificationType.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Manuell skanning fel: {ex.Message}");
                ShowToast("❌ Skanning misslyckades", NotificationType.Error);
            }
            finally
            {
                var scanningIndicator = FindName("ScanningIndicator") as StackPanel;
                if (scanningIndicator != null)
                    scanningIndicator.Visibility = Visibility.Collapsed;

                var quickScanButton = FindName("QuickScanButton") as Button;
                if (quickScanButton != null)
                {
                    quickScanButton.Content = "Sök efter hot";
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
                        ShowToast(message, NotificationType.Success);

                        _logViewer?.AddLogEntry(LogLevel.Information, "BrowserClean",
                            $"✅ Falska aviseringar rensade: {result.MalwareNotificationsRemoved} st");

                        _trayService?.ShowNotification("Falska aviseringar rensade",
                            $"{result.MalwareNotificationsRemoved} malware-notiser borttagna",
                            System.Windows.Forms.ToolTipIcon.Info);
                    }
                    else
                    {
                        ShowToast("❌ Rensning misslyckades", NotificationType.Error);
                        _logViewer?.AddLogEntry(LogLevel.Error, "BrowserClean",
                            "❌ Webbläsarrensning misslyckades");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid webbläsarrensning: {ex.Message}");
                ShowToast("❌ Rensning misslyckades", NotificationType.Error);
            }
            finally
            {
                var button = sender as Button ?? FindName("BrowserCleanTileButton") as Button;
                if (button != null)
                {
                    button.Content = "Rensa bluffnotiser";
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
                ShowToast(message, NotificationType.Success);

                _logViewer?.AddLogEntry(LogLevel.Information, "ThreatAction",
                    $"✅ {handledCount} hot har hanterats framgångsrikt");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hantering av alla hot: {ex.Message}");
                ShowToast("❌ Fel vid borttagning av hot", NotificationType.Error);
            }
            finally
            {
                var handleAllThreatsButton = FindName("HandleAllThreatsButton") as Button;
                if (handleAllThreatsButton != null)
                {
                    handleAllThreatsButton.Content = "Ta bort alla hot";
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
                        ShowToast($"🔄 Uppdatering: {threats.Count} hot funna", NotificationType.Warning);
                    }
                    else
                    {
                        ShowToast("🔄 Uppdatering: Inga hot funna", NotificationType.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hotskanning: {ex.Message}");
                ShowToast("❌ Uppdatering misslyckades", NotificationType.Error);
            }
            finally
            {
                var refreshScanButton = FindName("RefreshScanButton") as Button;
                if (refreshScanButton != null)
                {
                    refreshScanButton.Content = "Skanna om";
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
                Background = Application.Current.FindResource("FK.Brush.RowBackground") as Brush,
                BorderBrush = Application.Current.FindResource("FK.Brush.Border") as Brush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 10, 0, 10),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            // File name
            var fileName = new TextBlock
            {
                Text = threat.FileName,
                Style = TryFindResource("FK.Style.TableCell") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 8, 0)
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

            // File size - högerjusterad
            var fileSizeBlock = new TextBlock
            {
                Text = threat.FormattedSize ?? "Unknown",
                Style = TryFindResource("FK.Style.TableCell") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 16, 0)
            };
            Grid.SetColumn(fileSizeBlock, 2);
            grid.Children.Add(fileSizeBlock);

            // File path - med TextTrimming + ToolTip
            var filePath = new TextBlock
            {
                Text = Path.GetDirectoryName(threat.FilePath) ?? "",
                Style = TryFindResource("FK.Style.TableCell") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = threat.FilePath
            };
            Grid.SetColumn(filePath, 3);
            grid.Children.Add(filePath);

            // Created date - kort datumformat
            var createdDate = new TextBlock
            {
                Text = threat.CreatedDate.ToString("MM-dd HH:mm"),
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

            // Action button - kompaktare
            var actionButton = new Button
            {
                Content = "Ta bort",
                Style = TryFindResource("FK.Style.DangerButton") as Style,
                Tag = threat,
                Margin = new Thickness(8, 2, 8, 2),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            actionButton.Click += DeleteThreatButton_Click;
            Grid.SetColumn(actionButton, 6);
            grid.Children.Add(actionButton);

            // Hover-effekt med säker resource-hämtning
            row.MouseEnter += (s, e) =>
            {
                try
                {
                    row.Background = Application.Current.FindResource("FK.Brush.RowHover") as Brush;
                }
                catch
                {
                    row.Background = new SolidColorBrush(Color.FromRgb(240, 245, 249));
                }
            };

            row.MouseLeave += (s, e) =>
            {
                try
                {
                    row.Background = Application.Current.FindResource("FK.Brush.RowBackground") as Brush;
                }
                catch
                {
                    row.Background = new SolidColorBrush(Colors.White);
                }
            };

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
                        button.Content = "🔄";

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

                                ShowToast($"✅ {threat.FileName} har tagits bort", NotificationType.Success);

                                _logViewer?.AddLogEntry(LogLevel.Information, "ThreatAction",
                                    $"🗑️ Hot raderat: {threat.FileName}");
                            }
                            else
                            {
                                ShowToast($"❌ Kunde inte ta bort {threat.FileName}", NotificationType.Error);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid radering av hot: {ex.Message}");
                    ShowToast($"❌ Fel vid borttagning: {ex.Message}", NotificationType.Error);
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
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
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

        private List<ScanResult> GetSelectedThreats()
        {
            return new List<ScanResult>();
        }

        private void SortByColumn(string property, Button header)
        {
            if (_threatsView == null) return;

            var selectedItems = GetSelectedThreats();

            _threatsView.SortDescriptions.Clear();

            if (_currentSortColumn == property)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _currentSortColumn = property;
                _sortAscending = true;
            }

            var direction = _sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
            _threatsView.SortDescriptions.Add(new SortDescription(property, direction));

            UpdateHeaderSortIndicator(header, _sortAscending);

            RestoreSelectedThreats(selectedItems);

            _logger.Information($"Sorterat efter {property} ({direction})");
        }

        private void RestoreSelectedThreats(List<ScanResult> previouslySelected)
        {
            // Placeholder för selection-återställning
        }

        private void UpdateHeaderSortIndicator(Button header, bool ascending)
        {
            header.Tag = ascending ? "SortAsc" : "SortDesc";
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
                        ShowToast($"⚠️ {threats.Count} hot upptäckta under uppstartsskanning", NotificationType.Warning);
                        _logViewer?.AddLogEntry(LogLevel.Warning, "Startup", $"⚠️ {threats.Count} hot funna vid uppstart");
                    }
                    else
                    {
                        ShowToast("✅ Uppstartsskanning slutförd - inga hot funna", NotificationType.Success);
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
                    ShowToast("❌ Uppstartsskanning misslyckades", NotificationType.Error);
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

        private void ShowToast(string message, NotificationType type)
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

// MainWindow.xaml.cs - Förbättrade tabellmetoder
// Lägg till dessa metoder i MainWindow-klassen

private Border CreateEnhancedThreatRow(ScanResult threat)
{
    var row = new Border
    {
        Background = Application.Current.FindResource("FK.Brush.RowBackground") as Brush,
        BorderBrush = Application.Current.FindResource("FK.Brush.Border") as Brush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(0, 8, 0, 8), // Reducerat från 10
        Margin = new Thickness(0, 1, 0, 1),
        MinHeight = 44 // Kompaktare radhöjd
    };

    var grid = new Grid();

    // FÖRBÄTTRADE kolumndefinitioner
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // Filnamn
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); // Typ + ikon
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // Storlek
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) }); // Sökväg
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Datum
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); // Risk badge
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Åtgärd

    // Filnamn (kolumn 0) - Ökad vänstermarginal
    var fileName = new TextBlock
    {
        Text = threat.FileName,
        Style = TryFindResource("FK.Style.CompactTableCell") as Style,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(20, 0, 8, 0), // Ökad från 16 till 20
        FontWeight = FontWeights.Medium
    };
    Grid.SetColumn(fileName, 0);
    grid.Children.Add(fileName);

    // Filtyp med ikon (kolumn 1) - FÖRBÄTTRAT
    var typePanel = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0)
    };

    var typeIcon = new TextBlock
    {
        Text = GetFileTypeIcon(threat.FileName),
        FontSize = 16, // Större ikoner
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    typePanel.Children.Add(typeIcon);

    var typeText = new TextBlock
    {
        Text = GetFileTypeDisplay(threat.FileName),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Application.Current.FindResource("FK.Brush.Subtext") as Brush
    };
    typePanel.Children.Add(typeText);

    Grid.SetColumn(typePanel, 1);
    grid.Children.Add(typePanel);

    // Filstorlek (kolumn 2) - Högerjusterad
    var fileSizeBlock = new TextBlock
    {
        Text = threat.FormattedSize ?? "Okänd",
        Style = TryFindResource("FK.Style.CompactTableCell") as Style,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(8, 0, 16, 0),
        FontFamily = new FontFamily("Consolas, Monaco, monospace") // Monospace för siffror
    };
    Grid.SetColumn(fileSizeBlock, 2);
    grid.Children.Add(fileSizeBlock);

    // Sökväg (kolumn 3) - Med TextTrimming
    var filePath = new TextBlock
    {
        Text = Path.GetDirectoryName(threat.FilePath) ?? "",
        Style = TryFindResource("FK.Style.CompactTableCell") as Style,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0),
        TextTrimming = TextTrimming.CharacterEllipsis,
        ToolTip = threat.FilePath,
        Opacity = 0.8
    };
    Grid.SetColumn(filePath, 3);
    grid.Children.Add(filePath);

    // Datum (kolumn 4) - Kompakt format
    var createdDate = new TextBlock
    {
        Text = threat.CreatedDate.ToString("MM-dd HH:mm"),
        Style = TryFindResource("FK.Style.CompactTableCell") as Style,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0),
        FontFamily = new FontFamily("Consolas, Monaco, monospace")
    };
    Grid.SetColumn(createdDate, 4);
    grid.Children.Add(createdDate);

    // FÖRBÄTTRAD risk badge (kolumn 5)
    var threatBadge = CreateCompactThreatLevelBadge(threat.ThreatLevel);
    Grid.SetColumn(threatBadge, 5);
    grid.Children.Add(threatBadge);

    // KOMPAKT åtgärdsknapp (kolumn 6)
    var actionButton = new Button
    {
        Content = "Ta bort",
        Style = TryFindResource("FK.Style.CompactDangerButton") as Style,
        Tag = threat,
        Margin = new Thickness(8, 0, 8, 0),
        Padding = new Thickness(10, 6),
        FontSize = 11,
        Height = 32,
        MinWidth = 70,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    actionButton.Click += DeleteThreatButton_Click;
    Grid.SetColumn(actionButton, 6);
    grid.Children.Add(actionButton);

    // FÖRBÄTTRADE hover-effekter med varannan rad
    var rowIndex = _currentThreats.IndexOf(threat);
    var isEvenRow = rowIndex % 2 == 0;

    row.Background = isEvenRow
        ? Application.Current.FindResource("FK.Brush.RowBackground") as Brush
        : Application.Current.FindResource("FK.Brush.RowBackgroundAlt") as Brush;

    row.MouseEnter += (s, e) =>
    {
        row.Background = Application.Current.FindResource("FK.Brush.RowHover") as Brush;
        row.BorderBrush = Application.Current.FindResource("FK.Brush.Primary") as Brush;
    };

    row.MouseLeave += (s, e) =>
    {
        row.Background = isEvenRow
            ? Application.Current.FindResource("FK.Brush.RowBackground") as Brush
            : Application.Current.FindResource("FK.Brush.RowBackgroundAlt") as Brush;
        row.BorderBrush = Application.Current.FindResource("FK.Brush.Border") as Brush;
    };

    row.Child = grid;
    return row;
}

// FÖRBÄTTRAD metod för filtyps-ikoner
private string GetFileTypeIcon(string fileName)
{
    var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
    return extension switch
    {
        ".exe" => "⚙️", // Executable
        ".bat" => "📝", // Batch script
        ".cmd" => "📝", // Command script
        ".ps1" => "💻", // PowerShell
        ".vbs" => "📜", // VBScript
        ".scr" => "🖥️", // Screen saver
        ".msi" => "📦", // Installer
        ".jar" => "☕", // Java
        ".js" => "🟨", // JavaScript
        ".com" => "⚙️", // Command
        ".pif" => "🔗", // Program Information File
        _ => "📄" // Default file
    };
}

// FÖRBÄTTRAD kompakt risk badge
private Border CreateCompactThreatLevelBadge(ThreatLevel level)
{
    var badge = new Border
    {
        Style = TryFindResource("FK.Style.CompactThreatBadge") as Style,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    var text = new TextBlock
    {
        Style = TryFindResource("FK.Style.CompactBadgeText") as Style,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

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

// FÖRBÄTTRAD sorteringsmetod med visuell feedback
private void SortByColumnEnhanced(string property, Button header)
{
    if (_threatsView == null) return;

    // Rensa alla andra headers
    var allHeaders = new[] {
        FindName("FileNameHeader") as Button,
        FindName("TypeHeader") as Button,
        FindName("SizeHeader") as Button,
        FindName("PathHeader") as Button,
        FindName("DateHeader") as Button,
        FindName("RiskHeader") as Button
    };

    foreach (var otherHeader in allHeaders)
    {
        if (otherHeader != null && otherHeader != header)
        {
            otherHeader.Tag = null;
            UpdateHeaderSortIndicator(otherHeader, null);
        }
    }

    // Toggle sort direction
    if (_currentSortColumn == property)
    {
        _sortAscending = !_sortAscending;
    }
    else
    {
        _currentSortColumn = property;
        _sortAscending = true;
    }

    // Sortera data
    var sortedThreats = _sortAscending
        ? _currentThreats.OrderBy(GetSortValue)
        : _currentThreats.OrderByDescending(GetSortValue);

    // Uppdatera lista
    _currentThreats.Clear();
    foreach (var threat in sortedThreats)
    {
        _currentThreats.Add(threat);
    }

    // Uppdatera header visuellt
    header.Tag = _sortAscending ? "SortAsc" : "SortDesc";
    UpdateHeaderSortIndicator(header, _sortAscending);

    // Återbygg tabell
    BuildThreatsTableEnhanced(_currentThreats);

    _logger.Information($"Sorterat efter {property} ({(_sortAscending ? "stigande" : "fallande")})");

    // Lokal hjälpfunktion för sorteringsvärden
    object GetSortValue(ScanResult threat) => property switch
    {
        "FileName" => threat.FileName,
        "FileType" => Path.GetExtension(threat.FileName),
        "FileSize" => threat.FileSize,
        "FilePath" => threat.FilePath,
        "CreatedDate" => threat.CreatedDate,
        "ThreatLevel" => (int)threat.ThreatLevel,
        _ => threat.FileName
    };
}

private void UpdateHeaderSortIndicator(Button header, bool? ascending)
{
    // Hitta TextBlock med namnet "SortIndicator" i header template
    if (header.Template != null)
    {
        var indicator = header.Template.FindName("SortIndicator", header) as TextBlock;
        if (indicator != null)
        {
            if (ascending.HasValue)
            {
                indicator.Text = ascending.Value ? "▲" : "▼";
                indicator.Foreground = Application.Current.FindResource("FK.Brush.Primary") as Brush;
                indicator.Opacity = 1.0;
            }
            else
            {
                indicator.Text = "⇅";
                indicator.Foreground = Application.Current.FindResource("FK.Brush.Subtext") as Brush;
                indicator.Opacity = 0.5;
            }
        }
    }
}

// FÖRBÄTTRAD metod för att bygga hela tabellen
private void BuildEnhancedThreatsTable(List<ScanResult> threats)
{
    var threatsList = FindName("ThreatsList") as StackPanel;
    if (threatsList == null) return;

    threatsList.Children.Clear();

    // Använd virtualisering för stora listor
    if (threats.Count > 50)
    {
        // Visa endast första 50, lägg till "Visa fler" knapp
        var visibleThreats = threats.Take(50).ToList();
        foreach (var threat in visibleThreats)
        {
            var row = CreateEnhancedThreatRow(threat);
            threatsList.Children.Add(row);
        }

        if (threats.Count > 50)
        {
            var showMoreButton = new Button
            {
                Content = $"Visa {threats.Count - 50} fler hot...",
                Style = TryFindResource("FK.Style.SecondaryButton") as Style,
                Margin = new Thickness(16, 12, 16, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            showMoreButton.Click += (s, e) =>
            {
                // Visa alla hot
                threatsList.Children.Remove(showMoreButton);
                foreach (var threat in threats.Skip(50))
                {
                    var row = CreateEnhancedThreatRow(threat);
                    threatsList.Children.Add(row);
                }
            };
            threatsList.Children.Add(showMoreButton);
        }
    }
    else
    {
        // Visa alla hot direkt
        foreach (var threat in threats)
        {
            var row = CreateEnhancedThreatRow(threat);
            threatsList.Children.Add(row);
        }
    }
}

// Toast integration - ersätt ShowToast anrop
private ToastService? _toastService;

private void InitializeToastService()
{
    _toastService = new ToastService(this);
}

private void ShowToast(string message, ToastType type = ToastType.Info, int duration = 4000)
{
    _toastService?.ShowToast(message, type, duration);
}

// === FÖRBÄTTRAD THREAT ROW CREATION ===
private Border CreateThreatRow(ScanResult threat)
{
    var row = new Border
    {
        Background = Application.Current.FindResource("FK.Brush.RowBackground") as Brush,
        BorderBrush = Application.Current.FindResource("FK.Brush.Border") as Brush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(0, 8, 0, 8), // Reducerat från 10
        Margin = new Thickness(0, 1, 0, 1),
        MinHeight = 44 // Reducerat från implicit högre värde
    };

    var grid = new Grid();
    
    // Kolumndefinitioner med justerade bredder
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Typ + ikon
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // Storlek
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Datum
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); // Risk
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Åtgärd

    // Filnamn (kolumn 0)
    var fileName = new TextBlock
    {
        Text = threat.FileName,
        Style = TryFindResource("FK.Style.TableCell") as Style,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(20, 0, 8, 0) // Ökad vänstermarginal
    };
    Grid.SetColumn(fileName, 0);
    grid.Children.Add(fileName);

    // Filtyp med ikon (kolumn 1)
    var typePanel = new StackPanel 
    { 
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0)
    };

    // Lägg till filtyps-ikon
    var typeIcon = new TextBlock
    {
        Text = GetFileTypeIcon(threat.FileName),
        FontSize = 14,
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    typePanel.Children.Add(typeIcon);

    var typeText = new TextBlock
    {
        Text = GetFileTypeDisplay(threat.FileName),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center
    };
    typePanel.Children.Add(typeText);

    Grid.SetColumn(typePanel, 1);
    grid.Children.Add(typePanel);

    // Storlek - högerjusterad (kolumn 2)
    var fileSizeBlock = new TextBlock
    {
        Text = threat.FormattedSize ?? "Unknown",
        Style = TryFindResource("FK.Style.TableCell") as Style,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(8, 0, 16, 0)
    };
    Grid.SetColumn(fileSizeBlock, 2);
    grid.Children.Add(fileSizeBlock);

    // Sökväg med TextTrimming + ToolTip (kolumn 3)
    var filePath = new TextBlock
    {
        Text = Path.GetDirectoryName(threat.FilePath) ?? "",
        Style = TryFindResource("FK.Style.TableCell") as Style,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0),
        TextTrimming = TextTrimming.CharacterEllipsis,
        ToolTip = threat.FilePath
    };
    Grid.SetColumn(filePath, 3);
    grid.Children.Add(filePath);

    // Datum - kort format (kolumn 4)
    var createdDate = new TextBlock
    {
        Text = threat.CreatedDate.ToString("MM-dd HH:mm"),
        Style = TryFindResource("FK.Style.TableCell") as Style,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0)
    };
    Grid.SetColumn(createdDate, 4);
    grid.Children.Add(createdDate);

    // Förbättrad risk-badge (kolumn 5)
    var threatBadge = CreateCompactThreatLevelBadge(threat.ThreatLevel);
    Grid.SetColumn(threatBadge, 5);
    grid.Children.Add(threatBadge);

    // Kompakt åtgärdsknapp (kolumn 6)
    var actionButton = new Button
    {
        Content = "Ta bort",
        Style = TryFindResource("FK.Style.CompactDangerButton") as Style,
        Tag = threat,
        Margin = new Thickness(8, 0, 8, 0),
        Padding = new Thickness(10, 6, 10, 6),
        FontSize = 11,
        Height = 32,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    actionButton.Click += DeleteThreatButton_Click;
    Grid.SetColumn(actionButton, 6);
    grid.Children.Add(actionButton);

    // Hover-effekt med varannan rad
    var isEvenRow = _currentThreats.IndexOf(threat) % 2 == 0;
    row.Background = isEvenRow 
        ? Application.Current.FindResource("FK.Brush.RowBackground") as Brush
        : Application.Current.FindResource("FK.Brush.RowBackgroundAlt") as Brush;

    row.MouseEnter += (s, e) =>
    {
        row.Background = Application.Current.FindResource("FK.Brush.RowHover") as Brush;
    };

    row.MouseLeave += (s, e) =>
    {
        row.Background = isEvenRow 
            ? Application.Current.FindResource("FK.Brush.RowBackground") as Brush
            : Application.Current.FindResource("FK.Brush.RowBackgroundAlt") as Brush;
    };

    row.Child = grid;
    return row;
}

// === NYA METODER FÖR FILTYPS-IKONER ===
private string GetFileTypeIcon(string fileName)
{
    var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
    return extension switch
    {
        ".exe" => "⚙️",
        ".bat" => "📝",
        ".cmd" => "📝", 
        ".ps1" => "💻",
        ".vbs" => "📜",
        ".scr" => "🖥️",
        _ => "📄"
    };
}

// === FÖRBÄTTRAD KOMPAKT RISK-BADGE ===
private Border CreateCompactThreatLevelBadge(ThreatLevel level)
{
    var badge = new Border
    {
        CornerRadius = new CornerRadius(8), // Reducerat från 6
        Padding = new Thickness(8, 4, 8, 4), // Kompaktare
        Margin = new Thickness(4),
        HorizontalAlignment = HorizontalAlignment.Center,
        MinWidth = 60 // Reducerat från 70
    };

    var text = new TextBlock
    {
        FontWeight = FontWeights.Bold,
        FontSize = 9, // Mindre text
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    switch (level)
    {
        case ThreatLevel.Critical:
            badge.Background = TryFindResource("FK.Brush.Danger") as Brush;
            text.Text = "KRITISK";
            text.Foreground = Brushes.White;
            break;
        case ThreatLevel.High:
            badge.Background = TryFindResource("FK.Brush.Danger") as Brush;
            text.Text = "HÖG";
            text.Foreground = Brushes.White;
            break;
        case ThreatLevel.Medium:
            badge.Background = TryFindResource("FK.Brush.Warning") as Brush;
            text.Text = "MEDIUM";
            text.Foreground = Brushes.Black;
            break;
        default:
            badge.Background = TryFindResource("FK.Brush.StatusNeutral") as Brush;
            text.Text = "LÅG";
            text.Foreground = Brushes.White;
            break;
    }

    badge.Child = text;
    return badge;
}

// === FÖRBÄTTRAD SORTERING IMPLEMENTATION ===
private void SortByColumn(string property, Button header)
{
    // Rensa alla andra headers
    foreach (Button otherHeader in new[] { FileNameHeader, TypeHeader, SizeHeader, PathHeader, DateHeader, RiskHeader })
    {
        if (otherHeader != null && otherHeader != header)
        {
            otherHeader.Tag = null;
            UpdateHeaderSortIndicator(otherHeader, null);
        }
    }

    // Toggle sort direction
    if (_currentSortColumn == property)
    {
        _sortAscending = !_sortAscending;
    }
    else
    {
        _currentSortColumn = property;
        _sortAscending = true;
    }

    // Sortera data
    var sortedThreats = _sortAscending 
        ? _currentThreats.OrderBy(GetSortValue)
        : _currentThreats.OrderByDescending(GetSortValue);

    _currentThreats.Clear();
    foreach (var threat in sortedThreats)
    {
        _currentThreats.Add(threat);
    }

    // Uppdatera header visuellt
    header.Tag = _sortAscending ? "SortAsc" : "SortDesc";
    UpdateHeaderSortIndicator(header, _sortAscending);

    // Återbygg UI
    BuildThreatsTableEnhanced(_currentThreats);

    object GetSortValue(ScanResult threat) => property switch
    {
        "FileName" => threat.FileName,
        "FileType" => Path.GetExtension(threat.FileName),
        "FileSize" => threat.FileSize,
        "FilePath" => threat.FilePath,
        "CreatedDate" => threat.CreatedDate,
        "ThreatLevel" => (int)threat.ThreatLevel,
        _ => threat.FileName
    };
}

private void UpdateHeaderSortIndicator(Button header, bool? ascending)
{
    // Hitta sort indicator TextBlock i header
    if (header.Template?.FindName("SortIndicator", header) is TextBlock indicator)
    {
        if (ascending.HasValue)
        {
            indicator.Text = ascending.Value ? "▲" : "▼";
            indicator.Foreground = Application.Current.FindResource("FK.Brush.Primary") as Brush;
            indicator.Opacity = 1.0;
        }
        else
        {
            indicator.Text = "⇅";
            indicator.Foreground = Application.Current.FindResource("FK.Brush.Subtext") as Brush;
            indicator.Opacity = 0.5;
        }
    }
}

// === INITIALISERING AV SORTERBARA HEADERS ===
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