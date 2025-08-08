using System;
using System.Windows;
using System.Windows.Forms;
using FilKollen.Services;
using FilKollen.Windows;
using Serilog;

namespace FilKollen
{
    public partial class App : System.Windows.Application
    {
        private ILogger _logger;
        private MainWindow _mainWindow;
        private LicenseService _licenseService;
        private BrandingService _brandingService;
        
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Initiera logging tidigt
            _logger = new LoggerConfiguration()
                .WriteTo.File("logs/filkollen-.log", 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            _logger.Information("FilKollen Real-time Security startar...");

            // Initiera services
            _licenseService = new LicenseService(_logger);
            _brandingService = new BrandingService(_logger);

            // Kontrollera och tillämpa branding först
            try
            {
                var currentBranding = _brandingService.GetCurrentBranding();
                _logger.Information($"Branding loaded: {currentBranding.CompanyName} - {currentBranding.ProductName}");
                
                // Uppdatera fönster-titel och andra branding-element här
                // Detta kan göras genom att sätta globala resurser eller tema-variabler
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load branding: {ex.Message}");
            }

            // Kontrollera licensstatus
            var licenseStatus = await _licenseService.ValidateLicenseAsync();
            _logger.Information($"License status: {licenseStatus}");

            switch (licenseStatus)
            {
                case Models.LicenseStatus.Valid:
                    _logger.Information("Valid license found - starting application");
                    StartMainApplication();
                    break;

                case Models.LicenseStatus.TrialActive:
                    _logger.Information("Trial license active - starting application");
                    StartMainApplication();
                    ShowTrialNotification();
                    break;

                case Models.LicenseStatus.Expired:
                case Models.LicenseStatus.TrialExpired:
                case Models.LicenseStatus.Invalid:
                case Models.LicenseStatus.NotFound:
                    _logger.Warning($"License issue: {licenseStatus} - showing registration window");
                    ShowLicenseRegistrationWindow();
                    break;

                default:
                    _logger.Error($"Unexpected license status: {licenseStatus}");
                    ShowLicenseRegistrationWindow();
                    break;
            }

            // Kontrollera admin-rättigheter (befintlig kod fortsätter här)
            if (!IsRunningAsAdministrator())
            {
                var result = System.Windows.MessageBox.Show(
                    "🛡️ FILKOLLEN REAL-TIME SECURITY\n\n" +
                    "🛡️ FILKOLLEN REAL-TIME SECURITY\n\n" +
                    "För optimal säkerhet behöver FilKollen administratörsrättigheter för att:\n\n" +
                    "🔒 Övervaka systemkataloger\n" +
                    "🗑️ Säkert radera skadlig kod\n" +
                    "⚙️ Sätta säkerhetspolicies\n" +
                    "🛡️ Blockera malware i realtid\n\n" +
                    "Vill du starta om som administratör för fullständig säkerhet?",
                    "Administratörsrättigheter Rekommenderade",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Shield);

                if (result == MessageBoxResult.Yes)
                {
                    RestartAsAdministrator();
                    return;
                }
            }

            base.OnStartup(e);
        }

        private void StartMainApplication()
        {
            try
            {
                _mainWindow = new MainWindow(_licenseService, _brandingService);
                
                // Kontrollera om användaren vill starta minimerat
                bool startMinimized = Environment.GetCommandLineArgs().Contains("--minimized");
                
                if (startMinimized)
                {
                    _mainWindow.WindowState = WindowState.Minimized;
                    _mainWindow.Show();
                    _mainWindow.Hide(); // Direkt till tray
                }
                else
                {
                    _mainWindow.Show();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to start main application: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"❌ Kunde inte starta FilKollen:\n\n{ex.Message}",
                    "Startfel", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void ShowLicenseRegistrationWindow()
        {
            try
            {
                var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                var result = licenseWindow.ShowDialog();
                
                if (result == true)
                {
                    // Licens registrerad eller trial fortsätter
                    StartMainApplication();
                }
                else
                {
                    // Användaren stängde fönstret utan registrering
                    _logger.Information("User closed license registration - shutting down");
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to show license registration: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"❌ Kunde inte visa licensregistrering:\n\n{ex.Message}",
                    "Licensfel", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void ShowTrialNotification()
        {
            try
            {
                var remainingTime = _licenseService.GetRemainingTrialTime();
                if (remainingTime.HasValue && remainingTime.Value.TotalDays <= 3)
                {
                    var daysLeft = (int)Math.Ceiling(remainingTime.Value.TotalDays);
                    _logger.Information($"Trial expires in {daysLeft} days - showing notification");
                    
                    System.Windows.MessageBox.Show(
                        $"⏰ TRIAL-PÅMINNELSE\n\n" +
                        $"Din FilKollen trial går ut om {daysLeft} dag{(daysLeft == 1 ? "" : "ar")}.\n\n" +
                        $"För att fortsätta använda alla säkerhetsfunktioner,\n" +
                        $"registrera en licens innan trial-perioden slutar.\n\n" +
                        $"Klicka på ⚙️ Inställningar → Licenshantering för att registrera.",
                        "Trial Går Ut Snart",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to show trial notification: {ex.Message}");
            }
        }

        // Resten av befintlig OnStartup-kod fortsätter här...
        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void RestartAsAdministrator()
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "--minimized"
                };

                System.Diagnostics.Process.Start(processInfo);
                Shutdown();
            }
            catch (Exception ex)
            {
                _logger.Error($"Kunde inte starta som administratör: {ex.Message}");
            }
        }
    }
}

// Uppdateringar för MainWindow.xaml.cs konstruktor
namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly LicenseService _licenseService;
        private readonly BrandingService _brandingService;
        // ... övriga fields

        public MainWindow(LicenseService licenseService, BrandingService brandingService)
        {
            InitializeComponent();
            
            _licenseService = licenseService;
            _brandingService = brandingService;
            
            // Initiera theme service
            _themeService = new ThemeService();
            
            // Initiera logging
            _logger = new LoggerConfiguration()
                .WriteTo.File("logs/filkollen-.log", 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            // Ladda konfiguration
            _config = LoadConfiguration();
            
            // Tillämpa branding
            ApplyCurrentBranding();
            
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
            
            // Initiera UI
            ScanResults = new ObservableCollection<ScanResultViewModel>();
            PendingThreats = new ObservableCollection<ScanResultViewModel>();
            ResultsDataGrid.ItemsSource = ScanResults;
            LogListView.ItemsSource = _logViewer.LogEntries;
            
            // Bind events
            _protectionService.ProtectionStatusChanged += OnProtectionStatusChanged;
            _protectionService.ThreatDetected += OnThreatDetected;
            _trayService.ShowMainWindowRequested += OnShowMainWindowRequested;
            _trayService.ExitApplicationRequested += OnExitApplicationRequested;
            
            DataContext = this;
            
            // Uppdatera UI
            UpdateProtectionStatus();
            UpdateDashboard();
            UpdateLicenseStatus();
            
            _logger.Information("FilKollen startad med real-time protection");
            
            // Starta protection automatiskt
            _ = Task.Run(async () => await _protectionService.StartProtectionAsync());
        }

        private void ApplyCurrentBranding()
        {
            try
            {
                var branding = _brandingService.GetCurrentBranding();
                
                // Uppdatera fönster-titel
                Title = $"{branding.ProductName} - Modern Säkerhetsscanner";
                
                // Uppdatera logo om det finns en anpassad
                if (File.Exists(branding.LogoPath))
                {
                    // Uppdatera logo-element i UI
                    // Detta kräver att du har ett Image-element för logon i XAML
                    // CompanyLogo.Source = new BitmapImage(new Uri(Path.GetFullPath(branding.LogoPath)));
                }
                
                // Uppdatera färger om anpassade
                if (!string.IsNullOrEmpty(branding.PrimaryColor))
                {
                    // Tillämpa anpassade färger till tema
                    // Detta kräver dynamisk uppdatering av MaterialDesign-färger
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
                    // Uppdatera UI med licensinformation
                    var remainingTime = license.TimeRemaining;
                    if (remainingTime.TotalDays <= 30 && license.Type != Models.LicenseType.Lifetime)
                    {
                        // Visa varning för licens som snart går ut
                        _logViewer.AddLogEntry(Services.LogLevel.Warning, "License", 
                            $"⚠️ Licensen går ut om {license.FormattedTimeRemaining}");
                    }
                }
                else
                {
                    // Kontrollera trial-status
                    var trialTime = _licenseService.GetRemainingTrialTime();
                    if (trialTime.HasValue)
                    {
                        var trialTimeSpan = trialTime.Value;
                        if (trialTimeSpan.TotalHours <= 24)
                        {
                            _logViewer.AddLogEntry(Services.LogLevel.Warning, "Trial", 
                                $"⏰ Trial går ut om {(int)trialTimeSpan.TotalHours} timmar");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update license status: {ex.Message}");
            }
        }

        // Lägg till nya menyalternativ för licens- och branding-hantering
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsMenu = new ContextMenu();
            
            // Licens Management
            var licenseMenuItem = new MenuItem
            {
                Header = "🔑 Licenshantering",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.Key }
            };
            licenseMenuItem.Click += LicenseManagementMenuItem_Click;
            settingsMenu.Items.Add(licenseMenuItem);
            
            // Branding Management (endast för återförsäljare med rätt licens)
            var license = _licenseService.GetCurrentLicense();
            if (license?.Type == Models.LicenseType.Lifetime || license?.Type == Models.LicenseType.Yearly)
            {
                var brandingMenuItem = new MenuItem
                {
                    Header = "🎨 Branding Management",
                    Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.Palette }
                };
                brandingMenuItem.Click += BrandingManagementMenuItem_Click;
                settingsMenu.Items.Add(brandingMenuItem);
            }
            
            settingsMenu.Items.Add(new Separator());
            
            // Övriga inställningar
            var generalSettingsMenuItem = new MenuItem
            {
                Header = "⚙️ Allmänna Inställningar",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.Settings }
            };
            generalSettingsMenuItem.Click += GeneralSettingsMenuItem_Click;
            settingsMenu.Items.Add(generalSettingsMenuItem);
            
            settingsMenu.PlacementTarget = SettingsButton;
            settingsMenu.IsOpen = true;
        }

        private void LicenseManagementMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                licenseWindow.ShowDialog();
                
                // Uppdatera licensstatus efter eventuell ändring
                UpdateLicenseStatus();
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
                var brandingWindow = new BrandingManagementWindow(_brandingService, _logger);
                brandingWindow.ShowDialog();
                
                // Uppdatera branding efter eventuell ändring
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

        private void ApplicationExit()
        {
            _logger?.Information("FilKollen Real-time Security avslutas");

            try
            {
                _mainWindow?.Close();
            }
            catch (Exception ex)
            {
                _logger?.Error($"Fel vid avstängning: {ex.Message}");
            }
        }

        // Hantera system shutdown/logoff
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            _logger?.Information($"System session avslutas: {e.ReasonSessionEnding}");

            try
            {
                // Säkerställ att alla säkerhetsoperationer slutförs innan shutdown
                if (_mainWindow != null)
                {
                    // Ge services tid att stänga av säkert
                    System.Threading.Tasks.Task.Delay(2000).Wait();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Fel vid session shutdown: {ex.Message}");
            }

            base.OnSessionEnding(e);
        }
    }
}