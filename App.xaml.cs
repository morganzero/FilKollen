using System;
using System.Windows;
using System.Linq;
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
        private LicenseService? _licenseService;
        private BrandingService? _brandingService;
        private ThemeService? _themeService;

protected override void OnStartup(StartupEventArgs e)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.File("logs\\filkollen-.log", rollingInterval: RollingInterval.Day)
        .CreateLogger(); // eller .WriteTo.Debug() om du installerat paketet
    base.OnStartup(e);

    var theme = new ThemeService();
    var license = new LicenseService(Log.Logger);   // <— skicka loggern
    var branding = new BrandingService(Log.Logger); // <— skicka loggern

    var main = new MainWindow(license, branding, theme);
    main.Show();
}

public App()
{

    this.DispatcherUnhandledException += (s, e) =>
    {
        Log.Error(e.Exception, "UI-fel");
        MessageBox.Show(e.Exception.Message, "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // eller false om du vill låta appen stängas
    };

    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        Log.Fatal(e.ExceptionObject as Exception, "Ohanterat fel");

    TaskScheduler.UnobservedTaskException += (s, e) =>
    {
        Log.Error(e.Exception, "Task-fel");
        e.SetObserved();
    };
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
                System.Console.WriteLine("VARNING: Kunde inte initiera fil-logging - använder endast console");
                _logger = new LoggerConfiguration()
                    .WriteTo.Console()
                    .CreateLogger();
            }
        }

        private async Task InitializeServicesSafelyAsync()
        {
            try
            {
                _logger?.Information("Initierar kärntjänster...");

                // ThemeService - säker initiation (ny version)
                try
                {
                    _themeService = new ThemeService();
                    _logger?.Information("✅ ThemeService (ny version) initierad säkert");
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"ThemeService init misslyckades: {ex.Message} - använder standard");
                    _themeService = null;
                }

                // BrandingService - säker initiation med null-check
                try
                {
                    _brandingService = new BrandingService(_logger ?? Log.Logger ??
                        new LoggerConfiguration().WriteTo.Console().CreateLogger());
                    _logger?.Information("✅ BrandingService initierad säkert");
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"BrandingService init misslyckades: {ex.Message} - använder standard");
                    _brandingService = null;
                }

                // LicenseService - säker initiation med null-check
                try
                {
                    _licenseService = new LicenseService(_logger ?? Log.Logger ??
                        new LoggerConfiguration().WriteTo.Console().CreateLogger());
                    _logger?.Information("✅ LicenseService initierad säkert");
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"LicenseService init misslyckades: {ex.Message} - använder trial mode");
                    _licenseService = null;
                }

                await Task.Delay(10); // Yield
            }
            catch (Exception ex)
            {
                _logger?.Error($"Tjänstinitiering misslyckades: {ex.Message}");
                // Fortsätt ändå - MainWindow kan hantera null services
            }
        }

        private void ApplyInitialThemeSafely()
        {
            try
            {
                if (_themeService != null)
                {
                    _logger?.Information("Applicerar initial tema via nya ThemeService...");

                    // Den nya ThemeService hanterar automatiskt tema-tillämpning i sin konstruktor
                    // och laddar sparade inställningar från theme.json
                    _logger?.Information($"Tema tillämpat: {_themeService.ThemeDisplayName} (Mode: {_themeService.ModeDisplayName})");
                }
                else
                {
                    _logger?.Information("ThemeService inte tillgänglig - använder standardtema");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Tema-tillämpning misslyckades: {ex.Message} - använder standard");
            }
        }

        private async Task HandleLicensingSafelyAsync()
        {
            try
            {
                if (_licenseService != null)
                {
                    _logger?.Information("Kontrollerar licensstatus...");

                    var licenseStatus = await _licenseService.ValidateLicenseAsync();
                    _logger?.Information($"Licensstatus: {licenseStatus}");

                    // Om trial har gått ut eller ingen licens finns, visa licensfönster
                    if (licenseStatus == LicenseStatus.TrialExpired ||
                        licenseStatus == LicenseStatus.Expired ||
                        licenseStatus == LicenseStatus.Invalid)
                    {
                        _logger?.Information("Licensregistrering krävs - visar licensfönster");

                        var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger!);
                        var licenseResult = licenseWindow.ShowDialog();

                        if (licenseResult != true)
                        {
                            // Användaren avbröt eller stängde licensfönstret
                            _logger?.Warning("Licensregistrering avbruten - avslutar applikation");
                            Shutdown();
                            return;
                        }
                    }
                }
                else
                {
                    _logger?.Information("Ingen licensstjänst - kör i begränsat läge");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Licenshantering misslyckades: {ex.Message} - fortsätter med trial");
            }
        }

        private void StartMainApplicationSafely()
        {
            try
            {
                _logger?.Information("Skapar huvudfönster säkert...");

                // SÄKER: Skapa MainWindow med null-kontroller
                var mainWindow = new MainWindow(
                    _licenseService, // Kan vara null
                    _brandingService, // Kan vara null  
                    _themeService // Kan vara null (nya versionen)
                );

                _logger?.Information("MainWindow skapad framgångsrikt");

                // Säker titel-sättning
                try
                {
                    var branding = _brandingService?.GetCurrentBranding();
                    if (branding != null)
                    {
                        mainWindow.Title = $"{branding.ProductName} - {branding.CompanyName}";
                    }
                    else
                    {
                        mainWindow.Title = "FilKollen Säkerhetsscanner - Real-time Skydd v2.0";
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Kunde inte sätta fönstretitel: {ex.Message}");
                    mainWindow.Title = "FilKollen Säkerhetsscanner";
                }

                // Kontrollera command line args säkert
                bool startMinimized = false;
                try
                {
                    startMinimized = Environment.GetCommandLineArgs().Contains("--minimized") ||
                                   Environment.GetCommandLineArgs().Contains("--tray");
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Kunde inte kontrollera kommandoradsargument: {ex.Message}");
                }

                _logger?.Information($"Visar fönster (minimerat: {startMinimized})...");

                // Visa fönster säkert
                try
                {
                    if (startMinimized)
                    {
                        mainWindow.WindowState = WindowState.Minimized;
                        mainWindow.Show();
                        mainWindow.Hide(); // Direkt till tray
                        _logger?.Information("Applikation startad i systemfältet");
                    }
                    else
                    {
                        mainWindow.Show();
                        _logger?.Information("Huvudfönster visas");
                    }

                    MainWindow = mainWindow;
                    _logger?.Information("✅ Huvudapplikationsfönster skapat och visat framgångsrikt");
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Misslyckades att visa huvudfönster: {ex.Message}");

                    // FALLBACK: Visa enkel felmeddelande
                    MessageBox.Show($"Kunde inte visa huvudfönster:\n\n{ex.Message}\n\nApplikationen kommer att avslutas.",
                        "FilKollen Startfel", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"KRITISKT: Misslyckades att starta huvudapplikation: {ex.Message}");

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
                            $"KRITISKT STARTFEL: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
                    }
                    catch { }
                }

                Shutdown();
            }
        }

        private async Task HandleCriticalStartupErrorAsync(Exception ex)
        {
            var errorMsg = $"KRITISKT STARTFEL: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";

            // Försök logga
            try
            {
                _logger?.Fatal(ex, "Kritiskt startfel");
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
                _logger?.Warning($"Kunde inte kontrollera andra instanser: {ex.Message}");
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.Information("FilKollen Säkerhetsscanner avslutas");
            }
            catch { }

            base.OnExit(e);
        }
    }
}