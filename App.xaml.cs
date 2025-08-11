using System;
using System.Windows;
using System.Threading.Tasks;
using FilKollen.Services;
using FilKollen.Windows;
using FilKollen.Models;
using Serilog;

namespace FilKollen
{
    public partial class App : Application
    {
        private ILogger? _logger;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Setup global exception handling
            SetupExceptionHandling();

            // Initialize logging
            InitializeLogging();

            base.OnStartup(e);

            // Initialize and show main window
            InitializeMainWindow();
        }

        private void SetupExceptionHandling()
        {
            this.DispatcherUnhandledException += (s, e) =>
            {
                _logger?.Error(e.Exception, "UI Exception");
                MessageBox.Show($"Ett fel uppstod: {e.Exception.Message}",
                    "FilKollen Fel", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                _logger?.Fatal(e.ExceptionObject as Exception, "Unhandled Exception");

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                _logger?.Error(e.Exception, "Task Exception");
                e.SetObserved();
            };
        }

        private void InitializeLogging()
        {
            try
            {
                // Create logs directory
                var logsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!System.IO.Directory.Exists(logsDir))
                {
                    System.IO.Directory.CreateDirectory(logsDir);
                }

                _logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(System.IO.Path.Combine(logsDir, "filkollen-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7)
                    .WriteTo.Console()
                    .CreateLogger();

                Log.Logger = _logger;
                _logger.Information("FilKollen v2.0 starting with new UI design");
            }
            catch (Exception ex)
            {
                // Fallback logging
                Console.WriteLine($"Failed to initialize logging: {ex.Message}");
                _logger = new LoggerConfiguration()
                    .WriteTo.Console()
                    .CreateLogger();
                Log.Logger = _logger;
            }
        }

        private void InitializeMainWindow()
        {
            try
            {
                _logger?.Information("Initializing core services...");

                // Initialize services safely
                var themeService = CreateThemeService();
                var licenseService = CreateLicenseService();
                var brandingService = CreateBrandingService();

                // Create and show main window
                var mainWindow = new MainWindow(licenseService, brandingService, themeService);

                // Set window title from branding
                var branding = brandingService?.GetCurrentBranding();
                if (branding != null)
                {
                    mainWindow.Title = $"{branding.ProductName} - {branding.CompanyName}";
                }

                // Check for startup parameters
                bool startMinimized = Array.Exists(Environment.GetCommandLineArgs(),
                    arg => arg == "--minimized" || arg == "--tray");

                if (startMinimized)
                {
                    mainWindow.WindowState = WindowState.Minimized;
                    mainWindow.Show();
                    mainWindow.Hide();
                    _logger?.Information("Started minimized to system tray");
                }
                else
                {
                    mainWindow.Show();
                }

                MainWindow = mainWindow;
                _logger?.Information("Main window created and shown successfully");
            }
            catch (Exception ex)
            {
                _logger?.Fatal(ex, "Failed to initialize main window");
                MessageBox.Show($"Kritiskt startfel:\n\n{ex.Message}",
                    "FilKollen", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private ThemeService? CreateThemeService()
        {
            try
            {
                var themeService = new ThemeService();
                _logger?.Information("ThemeService initialized successfully");
                return themeService;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"ThemeService initialization failed: {ex.Message}");
                return null;
            }
        }

        private LicenseService? CreateLicenseService()
        {
            try
            {
                var licenseService = new LicenseService(_logger);
                _logger?.Information("LicenseService initialized successfully");
                return licenseService;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"LicenseService initialization failed: {ex.Message}");
                return null;
            }
        }

        private BrandingService? CreateBrandingService()
        {
            try
            {
                var brandingService = new BrandingService(_logger ?? Log.Logger);
                _logger?.Information("BrandingService initialized successfully");
                return brandingService;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"BrandingService initialization failed: {ex.Message}");
                return null;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.Information("FilKollen shutting down");
                Log.CloseAndFlush();
            }
            catch { }

            base.OnExit(e);
        }
    }
}