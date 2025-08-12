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
using FilKollen.Windows;
using Serilog;
using System.Windows.Media;
using System.Windows.Input;
using System.Net.Http;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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

        // UI-element referenser för sortering och licenshantering
        private TextBlock? FileNameHeader => FindName("FileNameHeader") as TextBlock;
        private TextBlock? TypeHeader => FindName("TypeHeader") as TextBlock;
        private TextBlock? SizeHeader => FindName("SizeHeader") as TextBlock;
        private TextBlock? PathHeader => FindName("PathHeader") as TextBlock;
        private TextBlock? DateHeader => FindName("DateHeader") as TextBlock;
        private TextBlock? RiskHeader => FindName("RiskHeader") as TextBlock;
        private Border? LicenseStatusDisplay => FindName("LicenseStatusDisplay") as Border;
        private TextBlock? LicenseButtonText => FindName("LicenseButtonText") as TextBlock;

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

                if (logoExists && logoLoadable)
                {
                    if (BrandLogo != null && BrandFallback != null)
                    {
                        // Fix: BrandLogo is a Button, get its Image child
                        var image = BrandLogo.Content as Image;
                        if (image == null)
                        {
                            image = new Image();
                            BrandLogo.Content = image;
                        }
                        image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(logoPath, UriKind.RelativeOrAbsolute));
                        BrandLogo.Visibility = Visibility.Visible;
                        BrandFallback.Visibility = Visibility.Collapsed;
                        _logger.Information($"Branding logo visas: {logoPath}");
                    }
                }
                else
                {
                    if (BrandLogo != null && BrandFallback != null)
                    {
                        BrandLogo.Visibility = Visibility.Collapsed;
                        BrandFallback.Visibility = Visibility.Visible;
                        _logger.Information("Använder FILKOLLEN-text (ingen giltig logga hittades)");
                    }
                }

                // Load Hi-DPI logo if available
                LoadHiDPILogo();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte ladda branding: {ex.Message}");
                if (BrandLogo != null && BrandFallback != null)
                {
                    BrandLogo.Visibility = Visibility.Collapsed;
                    BrandFallback.Visibility = Visibility.Visible;
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
                    var image = BrandLogo?.Content as Image ?? new Image();
                    image.Source = new BitmapImage(new Uri(logoPath, UriKind.Relative));
                    if (BrandLogo != null)
                    {
                        BrandLogo.Content = image;
                        BrandLogo.Visibility = Visibility.Visible;
                    }
                    if (BrandFallback != null)
                    {
                        BrandFallback.Visibility = Visibility.Collapsed;
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
            if (_themeService != null && ThemeToggle != null)
            {
                var isDarkTheme = _themeService.Mode == ThemeMode.Dark ||
                                 (_themeService.Mode == ThemeMode.System && DetectSystemDarkMode());

                ThemeToggle.IsChecked = isDarkTheme;
                _themeService.ThemeChanged += OnThemeChanged;

                _logger.Information($"Tema initierat: {(isDarkTheme ? "Mörkt" : "Ljust")} tema");
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

                // Start connection monitoring
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

        // Continue with rest of methods...
        // [Include all other methods from the original code with proper organization]

        #region Enhanced UI Updates

        private void UpdateThreatsDisplayEnhanced(List<ScanResult> threats)
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

            // Initialize sorting after display update
            InitializeTableHeaders();
        }

        private void ShowThreatsView(List<ScanResult> threats)
        {
            if (SafeStatusPanel != null)
                SafeStatusPanel.Visibility = Visibility.Collapsed;
            if (ThreatsPanel != null)
                ThreatsPanel.Visibility = Visibility.Visible;

            // Update threat counter
            UpdateThreatCounter(threats.Count);

            // Update status
            UpdateMainStatus("HOT UPPTÄCKTA", Colors.Orange,
                $"{threats.Count} hot kräver åtgärd");

            BuildThreatsTableEnhanced(threats);
        }

        private void ShowSafeView()
        {
            if (SafeStatusPanel != null)
                SafeStatusPanel.Visibility = Visibility.Visible;
            if (ThreatsPanel != null)
                ThreatsPanel.Visibility = Visibility.Collapsed;
            if (ThreatCounter != null)
                ThreatCounter.Visibility = Visibility.Collapsed;

            UpdateMainStatus("SYSTEMET ÄR SÄKERT", Colors.Green,
                $"0 hot funna • Auto-skydd {(_isProtectionActive ? "aktivt" : "inaktivt")}");
        }

        private void UpdateThreatCounter(int count)
        {
            if (ThreatCounter != null && ThreatCountText != null)
            {
                ThreatCounter.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ThreatCountText.Text = $"{count} HOT";
            }
        }

        private void UpdateMainStatus(string statusText, Color color, string subText)
        {
            if (StatusIndicator != null)
                StatusIndicator.Fill = new SolidColorBrush(color);

            if (StatusMainText != null)
            {
                StatusMainText.Text = statusText;
                StatusMainText.Foreground = new SolidColorBrush(color);
            }

            if (StatusSubText != null)
                StatusSubText.Text = subText;
        }

        #endregion

        #region Table Sorting Implementation

        private void InitializeTableHeaders()
        {
            // Lägg till sortfunktionalitet för alla headers
            MakeHeaderSortable(FileNameHeader, "FileName");
            MakeHeaderSortable(TypeHeader, "FileType");
            MakeHeaderSortable(SizeHeader, "FileSize");
            MakeHeaderSortable(PathHeader, "FilePath");
            MakeHeaderSortable(DateHeader, "CreatedDate");
            MakeHeaderSortable(RiskHeader, "ThreatLevel");
        }

        private void MakeHeaderSortable(TextBlock? header, string sortProperty)
        {
            if (header == null) return;

            header.MouseLeftButtonDown += (s, e) => SortByColumn(sortProperty, header);
            header.Style = (Style)FindResource("FK.Style.SortableHeader");
            header.Tag = sortProperty; // Store property name for reference
        }

        private void SortByColumn(string property, TextBlock header)
        {
            // Reset andra headers
            ResetOtherHeaders(header);

            // Toggle sort direction om samma kolumn
            if (_currentSortColumn == property)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _currentSortColumn = property;
                _sortAscending = true;
            }

            // Update header appearance
            UpdateHeaderSortIndicator(header, _sortAscending);

            // Perform sort
            SortThreats(property, _sortAscending);

            _logViewer?.AddLogEntry(LogLevel.Debug, "UI",
                $"Sorterat hot efter {property} ({(_sortAscending ? "stigande" : "fallande")})");
        }

        private void ResetOtherHeaders(TextBlock activeHeader)
        {
            var headers = new[] { FileNameHeader, TypeHeader, SizeHeader, PathHeader, DateHeader, RiskHeader };

            foreach (var header in headers)
            {
                if (header != null && header != activeHeader)
                {
                    header.Tag = header.Tag?.ToString()?.Replace("SortAsc", "").Replace("SortDesc", "");
                }
            }
        }

        private void UpdateHeaderSortIndicator(TextBlock header, bool ascending)
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

                // Rebuild table with sorted data
                BuildThreatsTableEnhanced(_currentThreats);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid sortering: {ex.Message}");
            }
        }

        #endregion

        #region Connection Status Management

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

            if (ConnectionStatusText != null)
            {
                ConnectionStatusText.Text = text;
                ConnectionStatusText.Foreground = color;
            }

            // Update status dot
            if (FindName("StatusDot") is Ellipse statusDot)
            {
                statusDot.Fill = color;
            }

            // Update tooltip
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
                    await Task.Delay(30000); // Check every 30 seconds

var isOnline = await CheckInternetConnection();
LicenseStatus? licenseStatus = null;
if (_licenseService != null)
    licenseStatus = await _licenseService.ValidateLicenseAsync();

var status = (isOnline, licenseStatus) switch
{
    (true, LicenseStatus.Valid) => ConnectionStatus.Online,
    (true, _)                   => ConnectionStatus.Online,
    (false, _)                  => ConnectionStatus.Offline,
    _                           => ConnectionStatus.Error
};

private void UpdateThreatsDisplay(List<ScanResult> threats)
    => UpdateThreatsDisplayEnhanced(threats);

private void BuildThreatsTable(List<ScanResult> threats)
    => BuildThreatsTableEnhanced(threats);

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

        #endregion

        #region Trial Badge Management

        private void UpdateTrialBadge()
        {
            var trialTime = _licenseService?.GetRemainingTrialTime();
            var isTrialMode = trialTime.HasValue && trialTime.GetValueOrDefault() > TimeSpan.Zero;

            if (FindName("TrialBadge") is Border trialBadge &&
                FindName("TrialBadgeText") is TextBlock trialText)
            {
                if (isTrialMode)
                {
                    trialBadge.Visibility = Visibility.Visible;

                    var days = (int)trialTime.Value.TotalDays;
                    var hours = trialTime.Value.Hours;

                    trialText.Text = days > 0
                        ? $"PROVPERIOD: {days} DAGAR KVAR"
                        : $"PROVPERIOD: {hours} TIMMAR KVAR";

                    // Update tooltip
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

        #endregion
        private async void QuickScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (QuickScanButton != null)
                {
                    QuickScanButton.Content = "🔄 SÖKER...";
                    QuickScanButton.IsEnabled = false;
                }

                if (ScanningIndicator != null)
                    ScanningIndicator.Visibility = Visibility.Visible;
                if (ScanProgress != null)
                    ScanProgress.Value = 0;

                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔍 Manuell skanning startad");

                if (_fileScanner != null)
                {
                    for (int i = 0; i <= 100; i += 20)
                    {
                        if (ScanProgress != null)
                            ScanProgress.Value = i;
                        await Task.Delay(300);
                    }

                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateThreatsDisplay(threats);

                    if (LastScanText != null)
                        LastScanText.Text = DateTime.Now.ToString("HH:mm");

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
                if (ScanningIndicator != null)
                    ScanningIndicator.Visibility = Visibility.Collapsed;

                if (QuickScanButton != null)
                {
                    QuickScanButton.Content = "🔍 Sök efter hot";
                    QuickScanButton.IsEnabled = true;
                }
            }
        }

        // Fix: Rename method to match XAML
        private async void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Find the correct button reference
                var button = sender as Button ?? BrowserCleanTileButton;
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
                var button = sender as Button ?? BrowserCleanTileButton;
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
                if (HandleAllThreatsButton != null)
                {
                    HandleAllThreatsButton.Content = "🔄 Tar bort alla...";
                    HandleAllThreatsButton.IsEnabled = false;
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
                ScrollToTopOfThreatsList();

                if (ThreatsHandledText != null)
                {
                    var currentHandled = int.Parse(ThreatsHandledText.Text);
                    ThreatsHandledText.Text = (currentHandled + handledCount).ToString();
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
                if (HandleAllThreatsButton != null)
                {
                    HandleAllThreatsButton.Content = "🧹 Ta bort alla hot";
                    HandleAllThreatsButton.IsEnabled = true;
                }
            }
        }

        private void ScrollToTopOfThreatsList()
        {
            try
            {
                if (ThreatsList?.Parent is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToTop();
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Kunde inte scrolla till toppen: {ex.Message}");
            }
        }

        private async void RefreshScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (RefreshScanButton != null)
                {
                    RefreshScanButton.Content = "🔄 Skannar...";
                    RefreshScanButton.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔄 Uppdaterar hotskanning");

                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateThreatsDisplay(threats);

                    if (LastScanText != null)
                        LastScanText.Text = DateTime.Now.ToString("HH:mm");

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
                if (RefreshScanButton != null)
                {
                    RefreshScanButton.Content = "🔄 Skanna om";
                    RefreshScanButton.IsEnabled = true;
                }
            }
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

                                if (ThreatsHandledText != null)
                                {
                                    var currentHandled = int.Parse(ThreatsHandledText.Text);
                                    ThreatsHandledText.Text = (currentHandled + 1).ToString();
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

        private void LicenseStatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_licenseService != null)
                {
                    var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                    licenseWindow.Owner = this;
                    var result = licenseWindow.ShowDialog();

                    _ = Task.Run(async () =>
                    {
                        var status = await _licenseService.ValidateLicenseAsync();
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            UpdateLicenseDisplay(status);
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid öppning av licensfönster: {ex.Message}");
                ShowInAppNotification("❌ Kunde inte öppna licensfönster", NotificationType.Error);
            }
        }

        private void SortHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock header && header.Tag is string columnName)
            {
                if (_currentSortColumn == columnName)
                {
                    _sortAscending = !_sortAscending;
                }
                else
                {
                    _currentSortColumn = columnName;
                    _sortAscending = true;
                }

                SortThreats(columnName, _sortAscending);
                UpdateSortHeaders(columnName, _sortAscending);
            }
        }

        private async Task PerformEnhancedStartupScanAsync()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ScanningIndicator != null)
                        ScanningIndicator.Visibility = Visibility.Visible;
                    if (ScanProgress != null)
                        ScanProgress.Value = 0;
                });

                _logViewer?.AddLogEntry(LogLevel.Information, "Startup", "🔍 Förbättrad uppstartsskanning startad");

                for (int i = 0; i <= 100; i += 10)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (ScanProgress != null)
                            ScanProgress.Value = i;
                    });
                    await Task.Delay(200);
                }

                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (ScanningIndicator != null)
                            ScanningIndicator.Visibility = Visibility.Collapsed;
                        UpdateThreatsDisplay(threats);
                        if (LastScanText != null)
                            LastScanText.Text = DateTime.Now.ToString("HH:mm");
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
                    if (ScanningIndicator != null)
                        ScanningIndicator.Visibility = Visibility.Collapsed;
                    ShowInAppNotification("❌ Uppstartsskanning misslyckades", NotificationType.Error);
                });
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

                if (LastScanText != null)
                    LastScanText.Text = "Aldrig";

                if (ThreatsHandledText != null)
                    ThreatsHandledText.Text = "0";

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
                    if (ConnectionStatusText != null)
                    {
                        ConnectionStatusText.Text = "ONLINE";
                        ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    }

                    if (_protectionService != null && ThreatsHandledText != null)
                    {
                        var stats = _protectionService.GetProtectionStats();
                        ThreatsHandledText.Text = stats.TotalThreatsHandled.ToString();
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Debug($"Status update error: {ex.Message}");
            }
        }
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