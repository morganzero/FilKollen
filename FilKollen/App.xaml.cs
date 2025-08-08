// App.xaml.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using FilKollen.Services;
using Serilog;

namespace FilKollen
{
    public partial class App : Application
    {
        private ILogger _logger;
        
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Initiera logging tidigt
            _logger = new LoggerConfiguration()
                .WriteTo.File("logs/filkollen-.log", 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            _logger.Information("FilKollen startar...");

            // Kontrollera admin-rättigheter
            if (!IsRunningAsAdministrator())
            {
                var result = MessageBox.Show(
                    "FilKollen behöver administratörsrättigheter för att fungera optimalt.\n\n" +
                    "Vill du starta om programmet som administratör?",
                    "Administratörsrättigheter krävs",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    RestartAsAdministrator();
                    return;
                }
            }

            // Kontrollera command line arguments för scheduled runs
            if (e.Args.Length > 0 && e.Args[0] == "--scheduled")
            {
                _logger.Information("Startar schemalagd skanning...");
                await RunScheduledScan();
                Shutdown();
                return;
            }

            // Sätt global exception handlers
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            base.OnStartup(e);
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
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                System.Diagnostics.Process.Start(processInfo);
                Shutdown();
            }
            catch (Exception ex)
            {
                _logger.Error($"Kunde inte starta som administratör: {ex.Message}");
                MessageBox.Show("Kunde inte starta programmet som administratör. " +
                               "Vissa funktioner kan vara begränsade.",
                    "Varning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task RunScheduledScan()
        {
            try
            {
                // Ladda konfiguration
                var config = LoadScheduledScanConfig();
                
                // Kör skanning
                var scanner = new FileScanner(config, _logger);
                var results = await scanner.ScanAsync();

                if (results.Any())
                {
                    var quarantineManager = new QuarantineManager(_logger);
                    
                    if (config.AutoDelete)
                    {
                        // Automatisk hantering
                        foreach (var result in results)
                        {
                            if (result.ThreatLevel >= ThreatLevel.High)
                            {
                                await quarantineManager.DeleteFileAsync(result);
                            }
                            else
                            {
                                await quarantineManager.QuarantineFileAsync(result);
                            }
                        }
                    }
                    else
                    {
                        // Sätt alla i karantän för manuell granskning
                        foreach (var result in results)
                        {
                            await quarantineManager.QuarantineFileAsync(result);
                        }
                    }

                    // Visa notifikation om hot hittades
                    if (config.ShowNotifications)
                    {
                        ShowScheduledScanNotification(results.Count);
                    }
                }

                _logger.Information($"Schemalagd skanning klar. {results.Count} hot hanterade.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid schemalagd skanning: {ex.Message}");
            }
        }

        private Models.AppConfig LoadScheduledScanConfig()
        {
            // Ladda konfiguration från fil eller använd defaults
            return new Models.AppConfig
            {
                ScanPaths = new() { "%TEMP%", "C:\\Windows\\Temp", "%LOCALAPPDATA%\\Temp" },
                SuspiciousExtensions = new() { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com", ".pif" },
                WhitelistPaths = new(),
                AutoDelete = false,
                QuarantineDays = 30,
                ShowNotifications = true
            };
        }

        private void ShowScheduledScanNotification(int threatCount)
        {
            // Implementera Windows Toast Notification
            try
            {
                var message = threatCount > 0 
                    ? $"FilKollen hittade {threatCount} hot som har karantänerats"
                    : "FilKollen-skanning klar - inga hot funna";

                // Enkelt MessageBox för nu - kan ersättas med Toast senare
                MessageBox.Show(message, "FilKollen", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Kunde inte visa notifikation: {ex.Message}");
            }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger.Error($"Ohanterat UI-fel: {e.Exception.Message}");
            _logger.Error($"Stack trace: {e.Exception.StackTrace}");
            
            MessageBox.Show($"Ett oväntat fel uppstod:\n{e.Exception.Message}\n\nSe loggfilen för mer information.",
                "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            _logger.Fatal($"Kritiskt fel: {e.ExceptionObject}");
            
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Ett kritiskt fel uppstod:\n{ex.Message}\n\nProgrammet kommer att avslutas.",
                    "Kritiskt fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.Information("FilKollen avslutas");
            _logger?.Dispose();
            base.OnExit(e);
        }
    }
}

// Program.cs (om du vill ha en explicit Main-metod)
using System;
using System.Threading;
using System.Windows;

namespace FilKollen
{
    public static class Program
    {
        [STAThread]
        public
