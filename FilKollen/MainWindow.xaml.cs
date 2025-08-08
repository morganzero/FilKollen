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
        private readonly FileScanner _fileScanner;
        private readonly QuarantineManager _quarantineManager;
        private readonly BrowserCleaner _browserCleaner;
        private readonly LogViewerService _logViewer;
        private readonly ThemeService _themeService;
        private readonly RealTimeProtectionService _protectionService;
        private readonly SystemTrayService _trayService;
        private readonly LicenseService _licenseService;
        private readonly BrandingService _brandingService;
        private readonly ILogger _logger;
        private AppConfig _config;

        public ObservableCollection<ScanResultViewModel> ScanResults { get; set; }
        public ObservableCollection<ScanResultViewModel> PendingThreats { get; set; }
        public bool IsScanning { get; set; }

        // Branding properties för databinding
        public string CurrentBrandingProductName { get; set; } = "FilKollen";
        public string CurrentBrandingLogoPath { get; set; } = "Resources/Branding/default-logo.png";

        public MainWindow()
        {
            // Initiera logging först
            _logger = new LoggerConfiguration()
                .WriteTo.File("logs/filkollen-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            // Initiera licens- och branding-services
            _licenseService = new LicenseService(_logger);
            _brandingService = new BrandingService(_logger);

            InitializeComponent();

            // Tillämpa branding före resten av UI-initieringen
            ApplyCurrentBranding();

            // Initiera theme service
            _themeService = new ThemeService();

            // Ladda konfiguration
            _config = LoadConfiguration();

            // Initiera övriga services
            _fileScanner = new FileScanner(_config, _logger);
            _quarantineManager = new QuarantineManager(_logger);
            _browserCleaner = new BrowserCleaner(_logger);
            _logViewer = new LogViewerService();

            // Initiera real-time protection
            _protectionService = new RealTimeProtectionService(
                _fileScanner, _quarantineManager, _logViewer, _logger, _config);

            // Initiera system tray
            _trayService = new SystemTrayService(_protectionService, _logViewer, _logger);

            // Initiera UI collections
            ScanResults = new ObservableCollection<ScanResultViewModel>();
            PendingThreats = new ObservableCollection<ScanResultViewModel>();
            ResultsDataGrid.ItemsSource = ScanResults;
            LogListView.ItemsSource = _logViewer.LogEntries;

            // Bind theme events
            UpdateThemeIcon();
            _themeService.PropertyChanged += (s, e) => UpdateThemeIcon();

            // Bind protection events
            _protectionService.ProtectionStatusChanged += OnProtectionStatusChanged;
            _protectionService.ThreatDetected += OnThreatDetected;

            // Bind tray events
            _trayService.ShowMainWindowRequested += OnShowMainWindowRequested;
            _trayService.ExitApplicationRequested += OnExitApplicationRequested;

            DataContext = this;

            // Uppdatera UI status
            UpdateProtectionStatus();
            UpdateDashboard();
            UpdateLicenseStatus();

            _logger.Information("FilKollen startad med real-time protection och licenssystem");
            _logViewer.AddLogEntry(Services.LogLevel.Information, "FilKollen",
                "🛡️ FilKollen Real-time Security startad - kontinuerligt skydd aktivt");

            // Starta protection automatiskt
            _ = Task.Run(async () => await _protectionService.StartProtectionAsync());

            // Starta licens-monitoring
            StartLicenseMonitoring();
        }

        #region Branding & License Management

        private void ApplyCurrentBranding()
        {
            try
            {
                var branding = _brandingService.GetCurrentBranding();

                // Uppdatera databinding-properties
                CurrentBrandingProductName = branding.ProductName;
                CurrentBrandingLogoPath = branding.LogoPath;

                // Uppdatera fönster-titel
                Title = $"{branding.ProductName} - Modern Säkerhetsscanner";

                // Notifiera UI om ändringar
                OnPropertyChanged(nameof(CurrentBrandingProductName));
                OnPropertyChanged(nameof(CurrentBrandingLogoPath));

                // Ladda logo om den finns
                if (File.Exists(branding.LogoPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(Path.GetFullPath(branding.LogoPath));
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        // Om du har CompanyLogoImage i XAML
                        if (CompanyLogoImage != null)
                        {
                            CompanyLogoImage.Source = bitmap;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to load custom logo: {ex.Message}");
                    }
                }

                _logger.Information($"Branding applied: {branding.CompanyName} - {branding.ProductName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply branding: {ex.Message}");
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
                        StatusBarText.Text = $"✅ Livstidslicens aktiv - Registrerad på: {license.CustomerName}";
                    }
                    else if (remainingTime.TotalDays <= 30)
                    {
                        StatusBarText.Text = $"⚠️ Licens går ut om {license.FormattedTimeRemaining}";
                        _logViewer.AddLogEntry(Services.LogLevel.Warning, "License",
                            $"⚠️ Licensen går ut om {license.FormattedTimeRemaining} - förnya för fortsatt skydd");
                    }
                    else
                    {
                        StatusBarText.Text = $"✅ Licens aktiv till {license.ExpiryDate:yyyy-MM-dd}";
                    }
                }
                else
                {
                    var trialTime = _licenseService.GetRemainingTrialTime();
                    if (trialTime.HasValue && trialTime.Value > TimeSpan.Zero)
                    {
                        var trialTimeSpan = trialTime.Value;
                        StatusBarText.Text = $"⏰ Trial aktiv - {FormatTimeSpan(trialTimeSpan)} kvar";

                        if (trialTimeSpan.TotalHours <= 24)
                        {
                            _logViewer.AddLogEntry(Services.LogLevel.Warning, "Trial",
                                $"⏰ TRIAL VARNING: Endast {FormatTimeSpan(trialTimeSpan)} kvar - registrera licens!");
                        }
                    }
                    else
                    {
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

        private void StartLicenseMonitoring()
        {
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromHours(1);
            timer.Tick += async (s, e) =>
            {
                try
                {
                    var status = await _licenseService.ValidateLicenseAsync();
                    if (status == LicenseStatus.Expired || status == LicenseStatus.TrialExpired)
                    {
                        timer.Stop();
                        Dispatcher.Invoke(() =>
                        {
                            var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                            licenseWindow.ShowDialog();
                            UpdateLicenseStatus();
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"License monitoring error: {ex.Message}");
                }
            };
            timer.Start();
        }

        #endregion

        #region UI Updates

        private void UpdateThemeIcon()
        {
            ThemeIcon.Kind = _themeService.IsDarkTheme ?
                PackIconKind.WeatherNight :
                PackIconKind.WeatherSunny;
        }

        private void UpdateDashboard()
        {
            var stats = _protectionService.GetProtectionStats();

            // Uppdatera dashboard stats
            ThreatCountText.Text = (ScanResults.Count + PendingThreats.Count).ToString();
            LastScanTimeText.Text = stats.LastScanTime != default ?
                stats.LastScanTime.ToString("HH:mm") : "Aldrig";
            ProtectionStatusDashboard.Text = stats.IsActive ? "Real-time" : "Inaktiv";

            // Uppdatera sidebar stats
            MonitoredPathsText.Text = $"{stats.MonitoredPaths} övervakade sökvägar";
            ThreatsFoundText.Text = $"{stats.TotalThreatsFound} hot funna";
            ThreatsHandledText.Text = $"{stats.TotalThreatsHandled} hot hanterade";
        }

        private void UpdateProtectionStatus()
        {
            var stats = _protectionService.GetProtectionStats();

            ProtectionToggle.IsChecked = stats.IsActive;
            AutoModeRadio.IsChecked = stats.AutoCleanMode;
            ManualModeRadio.IsChecked = !stats.AutoCleanMode;

            if (stats.IsActive)
            {
                ProtectionStatusText.Text = "AKTIVERAT";
                ProtectionStatusText.Foreground = System.Windows.Media.Brushes.Green;
                ProtectionDetailsText.Text = "Real-time övervakning aktiv";
                ProtectionStatusIcon.Kind = PackIconKind.Shield;
                ProtectionStatusIcon.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                ProtectionStatusText.Text = "INAKTIVERAT";
                ProtectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
                ProtectionDetailsText.Text = "Systemet är oskyddat";
                ProtectionStatusIcon.Kind = PackIconKind.ShieldOff;
                ProtectionStatusIcon.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        #endregion

        #region Settings Menu

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsMenu = new ContextMenu();

            // Licens Management - alltid tillgänglig
            var licenseMenuItem = new MenuItem { Header = "🔑 Licenshantering" };
            licenseMenuItem.Click += LicenseManagementMenuItem_Click;
            settingsMenu.Items.Add(licenseMenuItem);

            // Branding Management - endast för premium-licenser
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
                    _logViewer.AddLogEntry(Services.LogLevel.Information, "License",
                        "🔑 Licensstatus uppdaterad via användarinterface");
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
                        "Aktuell licens: " + (license?.Type.ToString() ?? "Trial") + "\n\n" +
                        "Uppgradera din licens för att få tillgång till branding-funktioner.",
                        "Premium-funktion",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var brandingWindow = new BrandingManagementWindow(_brandingService, _logger);
                brandingWindow.ShowDialog();

                ApplyCurrentBranding();
                _logViewer.AddLogEntry(Services.LogLevel.Information, "Branding",
                    "🎨 Branding-hantering använd - eventuella ändringar tillämpade");
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

        #region Event Handlers (Resten av alla metoder)

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _themeService.ToggleTheme();
            _logViewer.AddLogEntry(Services.LogLevel.Information, "Theme",
                $"🎨 Tema ändrat till: {_themeService.ThemeDisplayName}");
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            // Förenklad för MVP - visa bara en placeholder
            MessageBox.Show("🔍 Skanning startar snart!\n\nFör nu visas denna placeholder.",
                "Skanning", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void CleanBrowsersButton_Click(object sender, RoutedEventArgs e)
        {
            // Förenklad för MVP
            MessageBox.Show("🌐 Webbläsarrensning kommer snart!\n\nFör nu visas denna placeholder.",
                "Webbläsarrensning", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ManageQuarantineButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("🏥 Karantänhantering kommer snart!",
                "Karantän", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Placeholder event handlers för UI-element som inte finns än
        private void ProtectionToggle_Checked(object sender, RoutedEventArgs e) { }
        private void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e) { }
        private void ModeRadio_Checked(object sender, RoutedEventArgs e) { }
        private void OnProtectionStatusChanged(object sender, ProtectionStatusChangedEventArgs e) { }
        private void OnThreatDetected(object sender, ThreatDetectedEventArgs e) { }
        private void OnShowMainWindowRequested(object sender, EventArgs e) { }
        private void OnExitApplicationRequested(object sender, EventArgs e) { }
        private void QuarantineSelectedButton_Click(object sender, RoutedEventArgs e) { }
        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e) { }
        private void QuarantineAllButton_Click(object sender, RoutedEventArgs e) { }
        private void DeleteAllButton_Click(object sender, RoutedEventArgs e) { }
        private void ClearLogsButton_Click(object sender, RoutedEventArgs e) { }
        private void ExportLogsButton_Click(object sender, RoutedEventArgs e) { }

        #endregion

        #region Window Events

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

        #region Utility Methods

        private AppConfig LoadConfiguration()
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

    // Placeholder converters för att undvika fel
    public class ThreatLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}