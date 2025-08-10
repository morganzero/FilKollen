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
                // KRITISKT: Säker logging-initiation som aldrig kraschar
                await InitializeLoggingSafelyAsync();
                
                _logger?.Information("=== FilKollen Real-time Security startar (SÄKER MODE) ===");

                // KRITISKT: Failsafe service initialization
                await InitializeServicesSafelyAsync();

                // KRITISKT: Säker tema-hantering
                ApplySystemThemeSafely();

                // KRITISKT: Säker licens-hantering (hoppa över för nu)
                _logger?.Information("Hoppar över licensvalidering för säker start");

                // KRITISKT: Säker huvudapplikation-start
                StartMainApplicationSafely();

                _logger?.Information("=== FilKollen startup completed successfully (SÄKER MODE) ===");
            }
            catch (Exception ex)
            {
                // ULTRA-SÄKER: Hantera även kritiska startup-fel
                await HandleCriticalStartupErrorAsync(ex);
            }
        }

        private async Task InitializeLoggingSafelyAsync()
        {
            try
            {
                // Skapa logs-mapp säkert
                var logsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!System.IO.Directory.Exists(logsDir))
                {
                    System.IO.Directory.CreateDirectory(logsDir);
                }

                _logger = new LoggerConfiguration()
                    .WriteTo.File(System.IO.Path.Combine(logsDir, "filkollen-.log"), 
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30)
                    .WriteTo.Console()
                    .CreateLogger();

                Log.Logger = _logger; // Sätt global logger
                await Task.Delay(1); // Yield
            }
            catch
            {
                // Om logging misslyckas helt, använd enkel console output
                System.Console.WriteLine("WARNING: Could not initialize file logging - using console only");
                _logger = new LoggerConfiguration()
                    .WriteTo.Console()
                    .CreateLogger();
            }
        }

// App.xaml.cs - KORRIGERING för CS8604 null reference warning
// Ersätt rad 112 i InitializeServicesSafelyAsync metoden

private async Task InitializeServicesSafelyAsync()
{
    try
    {
        _logger?.Information("Initializing core services...");

        // ThemeService - säker initiation
        try
        {
            _themeService = new ThemeService();
            _logger?.Information("✅ ThemeService initialized safely");
        }
        catch (Exception ex)
        {
            _logger?.Warning($"ThemeService init failed: {ex.Message} - using defaults");
            _themeService = null; // Kommer att använda fallback
        }

        // BrandingService - säker initiation med null-check
        try
        {
            // KORRIGERAT: Säker null-hantering för logger
            _brandingService = new BrandingService(_logger ?? Log.Logger ?? 
                new LoggerConfiguration().WriteTo.Console().CreateLogger());
            _logger?.Information("✅ BrandingService initialized safely");
        }
        catch (Exception ex)
        {
            _logger?.Warning($"BrandingService init failed: {ex.Message} - using defaults");
            _brandingService = null; // MainWindow kommer att hantera null
        }

        // LicenseService - säker initiation med null-check
        try
        {
            // KORRIGERAT: Säker null-hantering för logger (rad 112 fix)
            _licenseService = new LicenseService(_logger ?? Log.Logger ?? 
                new LoggerConfiguration().WriteTo.Console().CreateLogger());
            _logger?.Information("✅ LicenseService initialized safely");
        }
        catch (Exception ex)
        {
            _logger?.Warning($"LicenseService init failed: {ex.Message} - using trial mode");
            _licenseService = null; // MainWindow kommer att hantera null
        }

        await Task.Delay(10); // Yield
    }
    catch (Exception ex)
    {
        _logger?.Error($"Service initialization failed: {ex.Message}");
        // Fortsätt ändå - MainWindow kan hantera null services
    }
}

        private void ApplySystemThemeSafely()
        {
            try
            {
                if (_themeService != null)
                {
                    _logger?.Information("Applying system theme...");
                    var isDarkTheme = _themeService.ShouldUseDarkTheme();
                    
                    // Försök sätta tema via MaterialDesign
                    try
                    {
                        var bundledTheme = Resources.MergedDictionaries
                            .OfType<MaterialDesignThemes.Wpf.BundledTheme>()
                            .FirstOrDefault();
                        
                        if (bundledTheme != null)
                        {
                            bundledTheme.BaseTheme = isDarkTheme ? 
                                MaterialDesignThemes.Wpf.BaseTheme.Dark : 
                                MaterialDesignThemes.Wpf.BaseTheme.Light;
                            
                            _logger?.Information($"Theme applied: {(isDarkTheme ? "Dark" : "Light")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"MaterialDesign theme application failed: {ex.Message}");
                        // Fortsätt med standard-tema
                    }
                }
                else
                {
                    _logger?.Information("ThemeService not available - using default theme");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Theme application failed: {ex.Message} - using default");
            }
        }

        private void StartMainApplicationSafely()
        {
            try
            {
                _logger?.Information("Creating main window safely...");
                
                // SÄKER: Skapa MainWindow med null-kontroller
                var mainWindow = new MainWindow(
                    _licenseService, // Kan vara null
                    _brandingService, // Kan vara null  
                    _themeService // Kan vara null
                );
                
                _logger?.Information("MainWindow created successfully");

                // Säker titel-sättning
                try
                {
                    var branding = _brandingService?.GetCurrentBranding();
                    if (branding != null)
                    {
                        mainWindow.Title = $"{branding.ProductName} - Modern Säkerhetsscanner";
                    }
                    else
                    {
                        mainWindow.Title = "FilKollen - Real-time Security Suite v2.0";
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Could not set window title: {ex.Message}");
                    mainWindow.Title = "FilKollen Security Scanner";
                }
                
                // Kontrollera command line args säkert
                bool startMinimized = false;
                try
                {
                    startMinimized = Environment.GetCommandLineArgs().Contains("--minimized");
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Could not check command line args: {ex.Message}");
                }
                
                _logger?.Information($"Showing window (minimized: {startMinimized})...");

                // Visa fönster säkert
                try
                {
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
                    _logger?.Information("✅ Main application window created and shown successfully");
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Failed to show main window: {ex.Message}");
                    
                    // FALLBACK: Visa enkel felmeddelande
                    MessageBox.Show($"Kunde inte visa huvudfönster:\n\n{ex.Message}\n\nApplikationen kommer att avslutas.",
                        "FilKollen Startfel", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"CRITICAL: Failed to start main application: {ex.Message}");
                
                // KRITISK FALLBACK
                try
                {
                    MessageBox.Show($"❌ FilKollen kunde inte starta:\n\n{ex.Message}\n\nKontrollera att alla filer finns och försök igen.",
                        "FilKollen Kritiskt Startfel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // Om även MessageBox misslyckas, skriv till fil
                    try
                    {
                        var errorFile = $"critical-startup-error-{DateTime.Now:yyyyMMdd-HHmmss}.log";
                        System.IO.File.WriteAllText(errorFile, 
                            $"CRITICAL STARTUP ERROR: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
                    }
                    catch { }
                }
                
                Shutdown();
            }
        }

        private async Task HandleCriticalStartupErrorAsync(Exception ex)
        {
            var errorMsg = $"KRITISKT STARTUPFEL: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
            
            // Försök logga
            try
            {
                _logger?.Fatal(ex, "Critical startup error");
            }
            catch { }
            
            // Skriv alltid till crash-fil
            try
            {
                var crashFile = $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log";
                await System.IO.File.WriteAllTextAsync(crashFile, errorMsg);
            }
            catch { }

            // Visa användarvänligt felmeddelande
            try
            {
                MessageBox.Show(
                    "🚨 FilKollen kunde inte starta på grund av ett kritiskt fel.\n\n" +
                    "Möjliga lösningar:\n" +
                    "• Starta om som administratör\n" +
                    "• Kontrollera att .NET 6 är installerat\n" +
                    "• Radera gamla konfigurationsfiler\n" +
                    "• Kontakta support med crash-loggen\n\n" +
                    $"Feldetaljer: {ex.Message}",
                    "FilKollen - Kritiskt Startfel", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            
            Shutdown();
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

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.Information("FilKollen Real-time Security avslutas");
            }
            catch { }
            
            base.OnExit(e);
        }
    }
}