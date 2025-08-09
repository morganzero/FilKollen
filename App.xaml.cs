using System;
using System.Windows;
using System.Linq;
using System.Threading.Tasks; // TILLAGD
using FilKollen.Services;
using FilKollen.Windows;
using Serilog;

namespace FilKollen
{
    public partial class App : System.Windows.Application
    {
        private ILogger? _logger;  // Gör nullable
        private LicenseService? _licenseService;  // Gör nullable
        private BrandingService? _brandingService;  // Gör nullable
        private ThemeService? _themeService;  // Gör nullable
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Förhindra multiple instances
            if (IsAnotherInstanceRunning())
            {
                MessageBox.Show("FilKollen körs redan. Endast en instans tillåten.", 
                    "FilKollen", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Initiera logging tidigt
            _logger = new LoggerConfiguration()
                .WriteTo.File("logs/filkollen-.log", 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            _logger.Information("FilKollen Real-time Security startar...");

            try
            {
                // Initiera tema-service först
                _themeService = new ThemeService();
                ApplySystemTheme();

                // Initiera services
                _licenseService = new LicenseService(_logger);
                _brandingService = new BrandingService(_logger);

                // Kontrollera och tillämpa branding
                await ApplyBrandingAsync();

                // Kontrollera admin-rättigheter först
                if (!IsRunningAsAdministrator())
                {
                    var result = ShowAdminPrompt();
                    if (result == MessageBoxResult.Yes)
                    {
                        RestartAsAdministrator();
                        return;
                    }
                }

                // Kontrollera licensstatus
                var licenseStatus = await _licenseService.ValidateLicenseAsync();
                _logger.Information($"License status: {licenseStatus}");

                switch (licenseStatus)
                {
                    case Models.LicenseStatus.Valid:
                    case Models.LicenseStatus.TrialActive:
                        StartMainApplication();
                        break;

                    case Models.LicenseStatus.Expired:
                    case Models.LicenseStatus.TrialExpired:
                    case Models.LicenseStatus.Invalid:
                    case Models.LicenseStatus.NotFound:
                        await ShowLicenseRegistrationAsync();
                        break;

                    default:
                        _logger.Error($"Unexpected license status: {licenseStatus}");
                        await ShowLicenseRegistrationAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Startup error: {ex.Message}");
                MessageBox.Show($"❌ Startfel:\n\n{ex.Message}", 
                    "FilKollen Startfel", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }

            // VIKTIGT: Kalla INTE base.OnStartup(e) här - det orsakar dubbel-öppning
        }

        private bool IsAnotherInstanceRunning()
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);
            return processes.Length > 1;
        }

        private void ApplySystemTheme()
        {
            try
            {
                // Följ systemets tema-inställning
                var isDarkTheme = _themeService.ShouldUseDarkTheme();
                
                var bundledTheme = Resources.MergedDictionaries
                    .OfType<MaterialDesignThemes.Wpf.BundledTheme>()
                    .FirstOrDefault();
                
                if (bundledTheme != null)
                {
                    bundledTheme.BaseTheme = isDarkTheme ? 
                        MaterialDesignThemes.Wpf.BaseTheme.Dark : 
                        MaterialDesignThemes.Wpf.BaseTheme.Light;
                }
                
                _logger.Information($"Theme applied: {(isDarkTheme ? "Dark" : "Light")}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to apply system theme: {ex.Message}");
            }
        }

        private async Task ApplyBrandingAsync()
        {
            await Task.Yield(); // TILLAGD för att fixa async warning
            
            try
            {
                var currentBranding = _brandingService?.GetCurrentBranding();
                _logger?.Information($"Branding loaded: {currentBranding?.CompanyName} - {currentBranding?.ProductName}");
                
                if (MainWindow != null && currentBranding != null)
                {
                    MainWindow.Title = $"{currentBranding.ProductName} - Modern Säkerhetsscanner";
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to load branding: {ex.Message}");
            }
        }

        private void StartMainApplication()
        {
            try
            {
                // Skapa huvudfönster med alla tre services
                var mainWindow = new MainWindow(_licenseService, _brandingService, _themeService);
                
                // Kontrollera om start minimerat
                bool startMinimized = Environment.GetCommandLineArgs().Contains("--minimized");
                
                if (startMinimized)
                {
                    mainWindow.WindowState = WindowState.Minimized;
                    mainWindow.Show();
                    mainWindow.Hide(); // Direkt till tray
                }
                else
                {
                    mainWindow.Show();
                }

                MainWindow = mainWindow;
                _logger.Information("Main application window created and shown");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to start main application: {ex.Message}");
                MessageBox.Show($"❌ Kunde inte starta FilKollen:\n\n{ex.Message}",
                    "Startfel", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private async Task ShowLicenseRegistrationAsync()
        {
            try
            {
                var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                var result = licenseWindow.ShowDialog();
                
                if (result == true)
                {
                    StartMainApplication();
                }
                else
                {
                    _logger.Information("User closed license registration - shutting down");
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to show license registration: {ex.Message}");
                MessageBox.Show($"❌ Kunde inte visa licensregistrering:\n\n{ex.Message}",
                    "Licensfel", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private MessageBoxResult ShowAdminPrompt()
        {
            return MessageBox.Show(
                "🛡️ FILKOLLEN REAL-TIME SECURITY\n\n" +
                "För optimal säkerhet behöver FilKollen administratörsrättigheter för att:\n\n" +
                "🔒 Övervaka systemkataloger\n" +
                "🗑️ Säkert radera skadlig kod\n" +
                "⚙️ Sätta säkerhetspolicies\n" +
                "🛡️ Blockera malware i realtid\n\n" +
                "Vill du starta om som administratör för fullständig säkerhet?",
                "Administratörsrättigheter Rekommenderade",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question); // ÄNDRAT från Shield till Question
        }

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
                    // FIX: Använd AppContext.BaseDirectory istället för Assembly.Location
                    FileName = System.IO.Path.Combine(System.AppContext.BaseDirectory, "FilKollen.exe"),
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "--minimized"
                };

                System.Diagnostics.Process.Start(processInfo);
                Shutdown();
            }
            catch (Exception ex)
            {
                _logger?.Error($"Kunde inte starta som administratör: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.Information("FilKollen Real-time Security avslutas");
            // ÄNDRAT: ILogger har ingen Dispose - ta bort denna rad
            base.OnExit(e);
        }
    }
}