#nullable disable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using FilKollen.Models;
using FilKollen.Services;
using FilKollen.ViewModels;
using FilKollen.Windows;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Serilog;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly LicenseService _licenseService;
        private readonly BrandingService _brandingService;
        private readonly ThemeService _themeService;
        private readonly ILogger _logger;
        
        // Real services
        private readonly FileScanner _fileScanner;
        private readonly QuarantineManager _quarantineManager;
        private readonly BrowserCleaner _browserCleaner;
        private readonly LogViewerService _logViewer;
        private readonly RealTimeProtectionService _protectionService;
        private readonly SystemTrayService _trayService;
        private AppConfig _config;

        public ObservableCollection<ScanResultViewModel> ScanResults { get; set; }
        public ObservableCollection<ScanResultViewModel> PendingThreats { get; set; }
        
        // UI Properties för databinding
        public bool IsScanning { get; set; }
        public bool IsDarkTheme => _themeService?.IsDarkTheme ?? false;
        public string CurrentBrandingProductName { get; set; } = "FilKollen";
        public string CurrentBrandingLogoPath { get; set; } = "Resources/Branding/default-logo.png";

        // Konstruktor som tar services från App.xaml.cs
        public MainWindow(LicenseService licenseService, BrandingService brandingService, ThemeService themeService)
        {
            _licenseService = licenseService;
            _brandingService = brandingService;
            _themeService = themeService;

            // Initiera logging
            _logger = new LoggerConfiguration()
                .WriteTo.File("logs/filkollen-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            InitializeComponent();

            // Initiera UI först
            InitializeUI();
            
            // Ladda konfiguration
            _config = LoadConfiguration();

            // Initiera riktiga services
            InitializeServices();

            // Tillämpa branding och tema
            ApplyCurrentBranding();
            ApplyTheme();

            // Bind events
            BindEvents();

            // Sätt DataContext
            DataContext = this;

            // Uppdatera UI
            UpdateAllUI();

            _logger.Information("MainWindow initialized successfully with real services");
        }

        private void InitializeUI()
        {
            // Initiera collections
            ScanResults = new ObservableCollection<ScanResultViewModel>();
            PendingThreats = new ObservableCollection<ScanResultViewModel>();
            
            // Bind DataGrid när det är laddat
            Loaded += (s, e) => {
                if (ResultsDataGrid != null)
                    ResultsDataGrid.ItemsSource = ScanResults;
            };
        }

        private void InitializeServices()
        {
            try
            {
                _fileScanner = new FileScanner(_config, _logger);
                _quarantineManager = new QuarantineManager(_logger);
                _browserCleaner = new BrowserCleaner(_logger);
                _logViewer = new LogViewerService();
                
                _protectionService = new RealTimeProtectionService(
                    _fileScanner, _quarantineManager, _logViewer, _logger, _config);
                
                _trayService = new SystemTrayService(_protectionService, _logViewer, _logger);
                
                _logger.Information("All services initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to initialize services: {ex.Message}");
                MessageBox.Show($"Fel vid initiering av tjänster: {ex.Message}", 
                    "Startfel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BindEvents()
        {
            // Theme service events
            if (_themeService != null)
            {
                _themeService.PropertyChanged += (s, e) => {
                    OnPropertyChanged(nameof(IsDarkTheme));
                    UpdateThemeIcon();
                    ApplyTheme();
                };
            }

            // Protection service events
            if (_protectionService != null)
            {
                _protectionService.ProtectionStatusChanged += OnProtectionStatusChanged;
                _protectionService.ThreatDetected += OnThreatDetected;
            }

            // Tray service events
            if (_trayService != null)
            {
                _trayService.ShowMainWindowRequested += OnShowMainWindowRequested;
                _trayService.ExitApplicationRequested += OnExitApplicationRequested;
            }
        }

        #region Branding & Theme Management

        private void ApplyCurrentBranding()
        {
            try
            {
                var branding = _brandingService.GetCurrentBranding();

                CurrentBrandingProductName = branding.ProductName;
                CurrentBrandingLogoPath = branding.LogoPath;

                Title = $"{branding.ProductName} - Modern Säkerhetsscanner";

                // Uppdatera UI-element
                if (ProductNameHeader != null)
                    ProductNameHeader.Text = branding.ProductName;

                // Ladda logo
                LoadBrandingLogo(branding.LogoPath);

                OnPropertyChanged(nameof(CurrentBrandingProductName));
                OnPropertyChanged(nameof(CurrentBrandingLogoPath));

                _logger.Information($"Branding applied: {branding.CompanyName} - {branding.ProductName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply branding: {ex.Message}");
            }
        }

        private void LoadBrandingLogo(string logoPath)
        {
            try
            {
                if (File.Exists(logoPath) && CompanyLogoImage != null)
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(Path.GetFullPath(logoPath));
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    CompanyLogoImage.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to load logo: {ex.Message}");
            }
        }

        private void ApplyTheme()
        {
            try
            {
                // Tema tillämpas automatiskt genom databinding i XAML
                OnPropertyChanged(nameof(IsDarkTheme));
                UpdateThemeIcon();
                
                _logger.Information($"Theme applied: {(IsDarkTheme ? "Dark" : "Light")}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply theme: {ex.Message}");
            }
        }

        private void UpdateThemeIcon()
        {
            if (ThemeIcon != null)
            {
                ThemeIcon.Kind = IsDarkTheme ? PackIconKind.WeatherNight : PackIconKind.WeatherSunny;
            }
        }

        #endregion

        #region UI Updates

        private void UpdateAllUI()
        {
            UpdateDashboard();
            UpdateProtectionStatus();
            UpdateLicenseStatus();
        }

        private void UpdateDashboard()
        {
            try
            {
                if (_protectionService != null)
                {
                    var stats = _protectionService.GetProtectionStats();
                    
                    if (ThreatCountText != null)
                        ThreatCountText.Text = (ScanResults.Count + PendingThreats.Count).ToString();
                    
                    if (LastScanTimeText != null)
                        LastScanTimeText.Text = stats.LastScanTime != default ? 
                            stats.LastScanTime.ToString("HH:mm") : "Aldrig";
                    
                    if (ProtectionStatusDashboard != null)
                        ProtectionStatusDashboard.Text = stats.IsActive ? "Real-time" : "Inaktiv";

                    // Sidebar stats
                    if (MonitoredPathsText != null)
                        MonitoredPathsText.Text = $"{stats.MonitoredPaths} övervakade sökvägar";
                    if (ThreatsFoundText != null)
                        ThreatsFoundText.Text = $"{stats.TotalThreatsFound} hot funna";
                    if (ThreatsHandledText != null)
                        ThreatsHandledText.Text = $"{stats.TotalThreatsHandled} hot hanterade";
                }
                else
                {
                    // Fallback values
                    if (ThreatCountText != null) ThreatCountText.Text = "0";
                    if (LastScanTimeText != null) LastScanTimeText.Text = "Aldrig";
                    if (ProtectionStatusDashboard != null) ProtectionStatusDashboard.Text = "Inaktiv";
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update dashboard: {ex.Message}");
            }
        }

        private void UpdateProtectionStatus()
        {
            try
            {
                bool isActive = _protectionService?.GetProtectionStats().IsActive ?? false;

                if (ProtectionToggle != null)
                    ProtectionToggle.IsChecked = isActive;

                if (isActive)
                {
                    if (ProtectionStatusText != null)
                    {
                        ProtectionStatusText.Text = "AKTIVERAT";
                        ProtectionStatusText.Foreground = Brushes.Green;
                    }
                    if (ProtectionDetailsText != null)
                        ProtectionDetailsText.Text = "Real-time övervakning aktiv";
                    if (ProtectionStatusIcon != null)
                    {
                        ProtectionStatusIcon.Kind = PackIconKind.Shield;
                        ProtectionStatusIcon.Foreground = Brushes.Green;
                    }
                }
                else
                {
                    if (ProtectionStatusText != null)
                    {
                        ProtectionStatusText.Text = "INAKTIVERAT";
                        ProtectionStatusText.Foreground = Brushes.Red;
                    }
                    if (ProtectionDetailsText != null)
                        ProtectionDetailsText.Text = "Systemet är oskyddat";
                    if (ProtectionStatusIcon != null)
                    {
                        ProtectionStatusIcon.Kind = PackIconKind.ShieldOff;
                        ProtectionStatusIcon.Foreground = Brushes.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update protection status: {ex.Message}");
            }
        }

        private void UpdateLicenseStatus()
        {
            try
            {
                var license = _licenseService.GetCurrentLicense();
                if (license != null)
                {
                    var remainingTime = license.TimeRemaining;

                    if (license.Type == LicenseType.Lifetime)
                    {
                        if (StatusBarText != null)
                            StatusBarText.Text = $"✅ Livstidslicens aktiv - Registrerad på: {license.CustomerName}";
                    }
                    else if (remainingTime.TotalDays <= 30)
                    {
                        if (StatusBarText != null)
                            StatusBarText.Text = $"⚠️ Licens går ut om {license.FormattedTimeRemaining}";
                    }
                    else
                    {
                        if (StatusBarText != null)
                            StatusBarText.Text = $"✅ Licens aktiv till {license.ExpiryDate:yyyy-MM-dd}";
                    }
                }
                else
                {
                    var trialTime = _licenseService.GetRemainingTrialTime();
                    if (trialTime.HasValue && trialTime.Value > TimeSpan.Zero)
                    {
                        var trialTimeSpan = trialTime.Value;
                        if (StatusBarText != null)
                            StatusBarText.Text = $"⏰ Trial aktiv - {FormatTimeSpan(trialTimeSpan)} kvar";
                    }
                    else
                    {
                        if (StatusBarText != null)
                            StatusBarText.Text = "❌ Ingen giltig licens - Begränsad funktionalitet";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update license status: {ex.Message}");
            }
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} dagar";
            else if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} timmar";
            else
                return $"{(int)timeSpan.TotalMinutes} minuter";
        }

        #endregion

        #region Window Events

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Trigger animations när fönstret laddas
            try
            {
                var fadeIn = (System.Windows.Media.Animation.Storyboard)FindResource("FadeInAnimation");
                fadeIn?.Begin(this);
            }
            catch { }

            // Bind ListView efter loading
            if (LogListView != null && _logViewer != null)
                LogListView.ItemsSource = _logViewer.LogEntries;

            _logger.Information("MainWindow loaded and animated");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            }
            catch { }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
            Hide();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }

        #endregion

        #region Settings Menu

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsMenu = new ContextMenu();

            var licenseMenuItem = new MenuItem { Header = "🔑 Licenshantering" };
            licenseMenuItem.Click += LicenseManagementMenuItem_Click;
            settingsMenu.Items.Add(licenseMenuItem);

            var license = _licenseService.GetCurrentLicense();
            if (license?.Type == LicenseType.Lifetime || license?.Type == LicenseType.Yearly)
            {
                var brandingMenuItem = new MenuItem { Header = "🎨 Branding Management" };
                brandingMenuItem.Click += BrandingManagementMenuItem_Click;
                settingsMenu.Items.Add(brandingMenuItem);
            }

            settingsMenu.Items.Add(new Separator());

            var generalSettingsMenuItem = new MenuItem { Header = "⚙️ Allmänna Inställningar" };
            generalSettingsMenuItem.Click += GeneralSettingsMenuItem_Click;
            settingsMenu.Items.Add(generalSettingsMenuItem);

            var helpMenuItem = new MenuItem { Header = "❓ Hjälp & Support" };
            helpMenuItem.Click += HelpMenuItem_Click;
            settingsMenu.Items.Add(helpMenuItem);

            var aboutMenuItem = new MenuItem { Header = "ℹ️ Om FilKollen" };
            aboutMenuItem.Click += AboutMenuItem_Click;
            settingsMenu.Items.Add(aboutMenuItem);

            settingsMenu.PlacementTarget = SettingsButton;
            settingsMenu.IsOpen = true;
        }

        private void LicenseManagementMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                var result = licenseWindow.ShowDialog();

                if (result == true)
                {
                    UpdateLicenseStatus();
                    UpdateDashboard();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to open license management: {ex.Message}");
                MessageBox.Show($"Kunde inte öppna licenshantering: {ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrandingManagementMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var license = _licenseService.GetCurrentLicense();
                if (license?.Type != LicenseType.Lifetime && license?.Type != LicenseType.Yearly)
                {
                    MessageBox.Show(
                        "🎨 BRANDING MANAGEMENT\n\n" +
                        "Denna funktion kräver en Årslicens eller Livstidslicens.\n\n" +
                        $"Aktuell licens: {(license?.Type.ToString() ?? "Trial")}\n\n" +
                        "Uppgradera din licens för att få tillgång till branding-funktioner.",
                        "Premium-funktion",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var brandingWindow = new BrandingManagementWindow(_brandingService, _logger);
                brandingWindow.ShowDialog();

                ApplyCurrentBranding();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to open branding management: {ex.Message}");
                MessageBox.Show($"Kunde inte öppna branding-hantering: {ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GeneralSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("⚙️ Avancerade säkerhetsinställningar kommer snart!\n\n" +
                          "🔧 Planerade funktioner:\n" +
                          "• Real-time skanningsintervall\n" +
                          "• Anpassade övervakningssökvägar\n" +
                          "• Whitelist-hantering\n" +
                          "• Avancerade hotdetekteringsinställningar\n" +
                          "• Notification-preferenser",
                "Säkerhetsinställningar", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var license = _licenseService.GetCurrentLicense();
                var branding = _brandingService.GetCurrentBranding();

                var helpMessage =
                    $"🛡️ {branding.ProductName} HJÄLP & SUPPORT\n\n" +
                    $"Version: 2.0.0\n" +
                    $"Licens: {(license?.Type.ToString() ?? "Trial")}\n" +
                    $"Företag: {branding.CompanyName}\n\n" +
                    $"📧 Support: {branding.ContactEmail}\n" +
                    $"🌐 Webbsida: {branding.Website}\n\n" +
                    $"🔧 Vanliga problem:\n" +
                    $"• Starta som administratör för full funktionalitet\n" +
                    $"• Kontrollera internetanslutning för licensvalidering\n" +
                    $"• Uppdatera Windows Defender-undantag om nödvändigt\n\n" +
                    $"📝 Loggar finns i: logs/filkollen-*.log";

                MessageBox.Show(helpMessage, "Hjälp & Support",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to show help: {ex.Message}");
            }
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var license = _licenseService.GetCurrentLicense();
                var branding = _brandingService.GetCurrentBranding();

                var aboutMessage =
                    $"🛡️ {branding.ProductName}\n" +
                    $"Modern Säkerhetsscanner\n\n" +
                    $"Version: 2.0.0\n" +
                    $"Utvecklad av: {branding.CompanyName}\n" +
                    $"Copyright © 2025\n\n" +
                    $"Licensstatus: {(license?.Type.ToString() ?? "Trial")}\n";

                if (license != null)
                {
                    aboutMessage += $"Registrerad på: {license.CustomerName}\n";
                    if (license.Type != LicenseType.Lifetime)
                        aboutMessage += $"Giltig till: {license.ExpiryDate:yyyy-MM-dd}\n";
                }

                aboutMessage +=
                    $"\n🔧 Komponenter:\n" +
                    $"• Real-time säkerhetsskydd\n" +
                    $"• Intelligent hotdetektering\n" +
                    $"• Webbläsare-säkerhetsrensning\n" +
                    $"• Quarantine-hantering\n" +
                    $"• System tray-integration\n";

                if (license?.Type == LicenseType.Lifetime || license?.Type == LicenseType.Yearly)
                    aboutMessage += $"• White-label branding-stöd\n";

                MessageBox.Show(aboutMessage, $"Om {branding.ProductName}",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to show about dialog: {ex.Message}");
            }
        }

        #endregion

        #region Event Handlers

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _themeService?.ToggleTheme();
            _logger?.Information($"🎨 Tema ändrat till: {(_themeService?.ThemeDisplayName ?? "Unknown")}");
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsScanning) return;

            try
            {
                IsScanning = true;
                if (ScanProgressBar != null)
                {
                    ScanProgressBar.Visibility = Visibility.Visible;
                    ScanProgressBar.IsIndeterminate = true;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "Scanner", "🔍 Manuell skanning startad...");

                var results = await _fileScanner.ScanAsync();
                
                // Konvertera till ViewModels
                foreach (var result in results)
                {
                    var viewModel = new ScanResultViewModel
                    {
                        FileName = result.FileName,
                        FilePath = result.FilePath,
                        ThreatLevel = result.ThreatLevel.ToString(),
                        Reason = result.Reason,
                        FormattedSize = result.FormattedSize,
                        IsSelected = false
                    };
                    ScanResults.Add(viewModel);
                }

                UpdateDashboard();
                _logViewer?.AddLogEntry(LogLevel.Information, "Scanner", 
                    $"✅ Skanning slutförd: {results.Count} hot identifierade");
            }
            catch (Exception ex)
            {
                _logger.Error($"Scan failed: {ex.Message}");
                _logViewer?.AddLogEntry(LogLevel.Error, "Scanner", $"❌ Skanning misslyckades: {ex.Message}");
            }
            finally
            {
                IsScanning = false;
                if (ScanProgressBar != null)
                {
                    ScanProgressBar.Visibility = Visibility.Collapsed;
                    ScanProgressBar.IsIndeterminate = false;
                }
            }
        }

        private async void CleanBrowsersButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "🌐 WEBBLÄSARRENSNING\n\n" +
                    "Detta kommer att:\n" +
                    "• Stänga alla webbläsare\n" +
                    "• Rensa notification-behörigheter\n" +
                    "• Ta bort site permissions\n" +
                    "• Sätta säkerhetspolicies\n\n" +
                    "Fortsätt?",
                    "Bekräfta Webbläsarrensning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _logViewer?.AddLogEntry(LogLevel.Information, "BrowserCleaner", "🌐 Webbläsarrensning startad...");
                    
                    var cleanResult = await _browserCleaner.CleanAllBrowsersAsync();
                    
                    if (cleanResult.Success)
                    {
                        MessageBox.Show(
                            $"✅ WEBBLÄSARRENSNING SLUTFÖRD!\n\n" +
                            $"Chrome profiler rensade: {cleanResult.ChromeProfilesCleaned}\n" +
                            $"Edge profiler rensade: {cleanResult.EdgeProfilesCleaned}\n" +
                            $"Totalt: {cleanResult.TotalProfilesCleaned} profiler\n\n" +
                            $"Säkerhetspolicies har satts för framtida skydd.",
                            "Rensning Slutförd",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        
                        _logViewer?.AddLogEntry(LogLevel.Information, "BrowserCleaner", 
                            $"✅ Webbläsarrensning slutförd: {cleanResult.TotalProfilesCleaned} profiler rensade");
                    }
                    else
                    {
                        MessageBox.Show("❌ Webbläsarrensning misslyckades delvis. Se loggar för detaljer.",
                            "Rensningsfel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Browser cleaning failed: {ex.Message}");
                MessageBox.Show($"Fel vid webbläsarrensning: {ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ManageQuarantineButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("🏥 Karantänhantering kommer snart!\n\nFunktionen kommer att hantera:\n• Karantänerade filer\n• Återställning\n• Permanent radering",
                "Karantän", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Protection toggle events
        private async void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                await _protectionService?.StartProtectionAsync();
                UpdateProtectionStatus();
                _logger?.Information("Real-time protection enabled by user");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to start protection: {ex.Message}");
            }
        }

        private async void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                await _protectionService?.StopProtectionAsync();
                UpdateProtectionStatus();
                _logger?.Information("Real-time protection disabled by user");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to stop protection: {ex.Message}");
            }
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_protectionService != null)
            {
                bool autoMode = AutoModeRadio?.IsChecked ?? false;
                _protectionService.AutoCleanMode = autoMode;
                _logger?.Information($"Protection mode changed to: {(autoMode ? "Auto" : "Manual")}");
            }
        }

        // Protection service events
        private void OnProtectionStatusChanged(object sender, ProtectionStatusChangedEventArgs e)
        {
            Dispatcher.Invoke(() => {
                UpdateProtectionStatus();
                UpdateDashboard();
            });
        }

        private void OnThreatDetected(object sender, ThreatDetectedEventArgs e)
        {
            Dispatcher.Invoke(() => {
                // Lägg till hot till pending threats
                var viewModel = new ScanResultViewModel
                {
                    FileName = Path.GetFileName(e.Threat.FilePath),
                    FilePath = e.Threat.FilePath,
                    ThreatLevel = e.Threat.ThreatLevel.ToString(),
                    Reason = e.Threat.Reason,
                    FormattedSize = e.Threat.FormattedSize
                };
                
                PendingThreats.Add(viewModel);
                UpdateDashboard();
                
                _logger?.Information($"Threat detected: {e.Threat.FilePath} - {e.Threat.Reason}");
            });
        }

        // Tray service events
        private void OnShowMainWindowRequested(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        }

        private void OnExitApplicationRequested(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        // DataGrid action events
        private async void QuarantineSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ScanResults.Where(x => x.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("Inga filer valda för karantän.", "Ingen fil vald", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                foreach (var item in selectedItems)
                {
                    var scanResult = new ScanResult
                    {
                        FilePath = item.FilePath,
                        ThreatLevel = Enum.Parse<ThreatLevel>(item.ThreatLevel),
                        Reason = item.Reason,
                        FileSize = 0 // Will be calculated in service
                    };
                    
                    await _quarantineManager.QuarantineFileAsync(scanResult);
                    ScanResults.Remove(item);
                }
                
                MessageBox.Show($"✅ {selectedItems.Count} filer har karantänerats.",
                    "Karantän slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                
                UpdateDashboard();
            }
            catch (Exception ex)
            {
                _logger.Error($"Quarantine failed: {ex.Message}");
                MessageBox.Show($"Fel vid karantän: {ex.Message}",
                    "Karantänfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ScanResults.Where(x => x.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("Inga filer valda för radering.", "Ingen fil vald", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"⚠️ PERMANENT RADERING\n\n" +
                $"Är du säker på att du vill radera {selectedItems.Count} valda filer PERMANENT?\n\n" +
                $"Denna åtgärd kan INTE ångras!",
                "Bekräfta permanent radering", 
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    foreach (var item in selectedItems)
                    {
                        var scanResult = new ScanResult { FilePath = item.FilePath };
                        await _quarantineManager.DeleteFileAsync(scanResult);
                        ScanResults.Remove(item);
                    }
                    
                    MessageBox.Show($"✅ {selectedItems.Count} filer har raderats permanent.",
                        "Radering slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    UpdateDashboard();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Delete failed: {ex.Message}");
                    MessageBox.Show($"Fel vid radering: {ex.Message}",
                        "Raderingsfel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void QuarantineAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ScanResults.Any())
            {
                MessageBox.Show("Inga filer att karantäna.", "Inga filer", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var allItems = ScanResults.ToList();
                foreach (var item in allItems)
                {
                    var scanResult = new ScanResult
                    {
                        FilePath = item.FilePath,
                        ThreatLevel = Enum.Parse<ThreatLevel>(item.ThreatLevel),
                        Reason = item.Reason
                    };
                    
                    await _quarantineManager.QuarantineFileAsync(scanResult);
                }
                
                ScanResults.Clear();
                MessageBox.Show($"✅ Alla {allItems.Count} filer har karantänerats.",
                    "Karantän slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                
                UpdateDashboard();
            }
            catch (Exception ex)
            {
                _logger.Error($"Quarantine all failed: {ex.Message}");
                MessageBox.Show($"Fel vid karantän: {ex.Message}",
                    "Karantänfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ScanResults.Any())
            {
                MessageBox.Show("Inga filer att radera.", "Inga filer", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"⚠️ PERMANENT RADERING AV ALLA FILER\n\n" +
                $"Är du säker på att du vill radera ALLA {ScanResults.Count} filer PERMANENT?\n\n" +
                $"Denna åtgärd kan INTE ångras!",
                "Bekräfta permanent radering", 
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var allItems = ScanResults.ToList();
                    foreach (var item in allItems)
                    {
                        var scanResult = new ScanResult { FilePath = item.FilePath };
                        await _quarantineManager.DeleteFileAsync(scanResult);
                    }
                    
                    ScanResults.Clear();
                    MessageBox.Show($"✅ Alla {allItems.Count} filer har raderats permanent.",
                        "Radering slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    UpdateDashboard();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Delete all failed: {ex.Message}");
                    MessageBox.Show($"Fel vid radering: {ex.Message}",
                        "Raderingsfel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Log management events
        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logViewer?.ClearLogs();
                _logger?.Information("Log entries cleared by user");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to clear logs: {ex.Message}");
            }
        }

        private void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Exportera Loggar",
                    Filter = "Text-filer (*.txt)|*.txt|Alla filer (*.*)|*.*",
                    FileName = $"FilKollen_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    _logViewer?.ExportLogs(saveFileDialog.FileName);
                    MessageBox.Show($"Loggar exporterade till:\n{saveFileDialog.FileName}",
                        "Export slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to export logs: {ex.Message}");
                MessageBox.Show($"Kunde inte exportera loggar: {ex.Message}",
                    "Exportfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Utility Methods

        private AppConfig LoadConfiguration()
        {
            try
            {
                if (File.Exists("appsettings.json"))
                {
                    var json = File.ReadAllText("appsettings.json");
                    var config = System.Text.Json.JsonSerializer.Deserialize<AppConfigWrapper>(json);
                    return config?.AppSettings ?? CreateDefaultConfig();
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to load configuration: {ex.Message}");
            }

            return CreateDefaultConfig();
        }

        private AppConfig CreateDefaultConfig()
        {
            return new AppConfig
            {
                ScanPaths = new() { "%TEMP%", "C:\\Windows\\Temp", "%LOCALAPPDATA%\\Temp" },
                SuspiciousExtensions = new() { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com", ".pif" },
                WhitelistPaths = new(),
                AutoDelete = false,
                QuarantineDays = 30,
                ShowNotifications = true
            };
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    #region Support Classes

    // ViewModel för UI-binding
    public class ScanResultViewModel : INotifyPropertyChanged
    {
        public bool IsSelected { get; set; }
        public string ThreatLevel { get; set; } = "Medium";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FormattedSize { get; set; } = "0 B";
        public string Reason { get; set; } = "";

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Wrapper för appsettings.json
    public class AppConfigWrapper
    {
        public AppConfig AppSettings { get; set; } = new();
        public List<string> ScanPaths { get; set; } = new();
        public List<string> SuspiciousExtensions { get; set; } = new();
        public List<string> WhitelistPaths { get; set; } = new();
    }

    #endregion
}