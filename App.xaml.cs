using System;
using System.Windows;
using System.Linq;
using System.Threading.Tasks;
using FilKollen.Services;
using FilKollen.Windows;
using Serilog;

namespace FilKollen
{
    public partial class App : System.Windows.Application
    {
        private ILogger? _logger;
        private LicenseService? _licenseService;
        private BrandingService? _brandingService;
        private ThemeService? _themeService;

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                // 1. Initiera logging först
                _logger = new LoggerConfiguration()
                    .WriteTo.File("logs/filkollen-.log", 
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30)
                    .WriteTo.Console()
                    .CreateLogger();

                _logger.Information("=== FilKollen Real-time Security startar ===");
                _logger.Information("Command line args: {Args}", string.Join(" ", e.Args));

                // 2. Förhindra multiple instances
                if (IsAnotherInstanceRunning())
                {
                    _logger.Warning("Another instance is already running");
                    MessageBox.Show("FilKollen körs redan. Endast en instans tillåten.", 
                        "FilKollen", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }

                // 3. Initiera services med fel-hantering
                _logger.Information("Initializing services...");
                
                _themeService = new ThemeService();
                _logger.Information("✅ ThemeService initialized");

                _licenseService = new LicenseService(_logger);
                _logger.Information("✅ LicenseService initialized");

                _brandingService = new BrandingService(_logger);
                _logger.Information("✅ BrandingService initialized");

                // 4. Tillämpa tema och branding
                ApplySystemTheme();
                await ApplyBrandingAsync();

                // 5. Kontrollera licensstatus MED fel-hantering
                _logger.Information("Checking license status...");
                var licenseStatus = await SafeValidateLicenseAsync();
                _logger.Information($"License status determined: {licenseStatus}");

                // 6. FÖRENKLAD startup - hoppa över license check för debug
                _logger.Information("Starting main application (debug mode - skipping license validation)...");
                StartMainApplication();

                _logger.Information("=== FilKollen startup completed successfully ===");
            }
            catch (Exception ex)
            {
                // KRITISKT: Robust fel-hantering för startup
                var errorMsg = $"KRITISKT STARTUPFEL: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
                
                if (_logger != null)
                {
                    _logger.Fatal(ex, "Critical startup error");
                }
                
                // Skriv alltid till fil för debug
                System.IO.File.WriteAllText($"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log", errorMsg);

                try
                {
                    MessageBox.Show(errorMsg, "FilKollen - Kritiskt Startfel", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // Om MessageBox också misslyckas, bara avsluta
                }
                
                Shutdown();
            }
        }

        private bool IsAnotherInstanceRunning()
        {
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);
                return processes.Length > 1;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Could not check for other instances: {ex.Message}");
                return false;
            }
        }

        private void ApplySystemTheme()
        {
            try
            {
                _logger?.Information("Applying system theme...");
                var isDarkTheme = _themeService?.ShouldUseDarkTheme() ?? false;
                
                var bundledTheme = Resources.MergedDictionaries
                    .OfType<MaterialDesignThemes.Wpf.BundledTheme>()
                    .FirstOrDefault();
                
                if (bundledTheme != null)
                {
                    bundledTheme.BaseTheme = isDarkTheme ? 
                        MaterialDesignThemes.Wpf.BaseTheme.Dark : 
                        MaterialDesignThemes.Wpf.BaseTheme.Light;
                }
                
                _logger?.Information($"Theme applied: {(isDarkTheme ? "Dark" : "Light")}");
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to apply system theme: {ex.Message}");
            }
        }

        private async Task ApplyBrandingAsync()
        {
            try
            {
                _logger?.Information("Applying branding...");
                var currentBranding = _brandingService?.GetCurrentBranding();
                _logger?.Information($"Branding loaded: {currentBranding?.CompanyName} - {currentBranding?.ProductName}");
                
                // Sätt window title när main window skapas
                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to load branding: {ex.Message}");
            }
        }
        private async Task<Models.LicenseStatus> SafeValidateLicenseAsync()
        {
            try
            {
                _logger?.Information("Starting license validation...");
                var status = await _licenseService!.ValidateLicenseAsync();
                _logger?.Information($"License validation completed: {status}");
                return status;
            }
            catch (Exception ex)
            {
                _logger?.Error($"License validation failed: {ex.Message}");
                // Fallback till trial om licensvalidering misslyckas
                return Models.LicenseStatus.TrialActive;
            }
        }

        private void StartMainApplication()
        {
            try
            {
                _logger?.Information("Creating main window...");
                
                // KORRIGERAT: Null-kontroller för säkerhet
                if (_licenseService == null || _brandingService == null || _themeService == null)
                {
                    throw new InvalidOperationException("Services not properly initialized");
                }

                _logger?.Information("All services verified - creating MainWindow...");

                // Skapa huvudfönster med alla tre services
                var mainWindow = new MainWindow(_licenseService, _brandingService, _themeService);
                
                _logger?.Information("MainWindow created successfully");

                // Sätt branding title
                try
                {
                    var currentBranding = _brandingService.GetCurrentBranding();
                    if (currentBranding != null)
                    {
                        mainWindow.Title = $"{currentBranding.ProductName} - Modern Säkerhetsscanner";
                        _logger?.Information($"Window title set: {mainWindow.Title}");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Could not set window title: {ex.Message}");
                }
                
                // Kontrollera om start minimerat
                bool startMinimized = Environment.GetCommandLineArgs().Contains("--minimized");
                
                _logger?.Information($"Showing window (minimized: {startMinimized})...");

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
                _logger?.Information("✅ Main application window created and shown");
            }
            catch (Exception ex)
            {
                _logger?.Error($"CRITICAL: Failed to start main application: {ex.Message}");
                _logger?.Error($"Stack trace: {ex.StackTrace}");
                
                // Fallback - visa enkel felmeddelande istället för krasch
                try
                {
                    MessageBox.Show($"❌ FilKollen kunde inte starta korrekt:\n\n{ex.Message}\n\nKontrollera logs för mer information.",
                        "FilKollen Startfel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // Om även MessageBox misslyckas, skriv till fil
                    System.IO.File.WriteAllText($"critical-error-{DateTime.Now:yyyyMMdd-HHmmss}.log", 
                        $"CRITICAL STARTUP ERROR: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
                }
                
                Shutdown();
            }
        }
        private async Task ShowLicenseRegistrationAsync()
        {
            try
            {
                _logger?.Information("Showing license registration window...");
                
                // KORRIGERAT: Null-kontroller
                if (_licenseService == null || _logger == null)
                {
                    throw new InvalidOperationException("Services not properly initialized");
                }

                var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                var result = licenseWindow.ShowDialog();
                
                if (result == true)
                {
                    _logger?.Information("License registration successful - starting main application");
                    StartMainApplication();
                }
                else
                {
                    _logger?.Information("User closed license registration - shutting down");
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to show license registration: {ex.Message}");
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
                "Vill du starta om som administratör för fullständig säkerhet?\n\n" +
                "(Du kan också fortsätta utan admin-rättigheter för testning)",
                "Administratörsrättigheter Rekommenderade",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
        }

        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Could not check admin status: {ex.Message}");
                return false;
            }
        }

        private void RestartAsAdministrator()
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
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
            base.OnExit(e);
        }
    }
}