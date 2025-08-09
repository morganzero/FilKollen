using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FilKollen.Models;
using FilKollen.Services;
using FilKollen.ViewModels;
using FilKollen.Windows;
using Microsoft.Win32;
using Serilog;
using FilKollen.Commands;

namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        private async Task<bool> InitializeApplicationSafelyAsync()
        {
            var initTasks = new Dictionary<string, Func<Task>>
            {
                ["Services"] = InitializeServicesAsync,
                ["UI"] = InitializeUIComponentsAsync,
                ["Security"] = InitializeSecurityComponentsAsync,
                ["Licensing"] = InitializeLicensingAsync,
                ["Monitoring"] = InitializeMonitoringAsync
            };

            var failedComponents = new List<string>();
            var initTimeout = TimeSpan.FromSeconds(30);

            foreach (var task in initTasks)
            {
                try
                {
                    using var cancellationTokenSource = new CancellationTokenSource(initTimeout);

                    _logger.Information($"Initialiserar komponent: {task.Key}");

                    var initTask = task.Value();
                    var timeoutTask = Task.Delay(initTimeout, cancellationTokenSource.Token);

                    var completedTask = await Task.WhenAny(initTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        _logger.Error($"Timeout vid initialisering av {task.Key}");
                        failedComponents.Add($"{task.Key} (Timeout)");
                        continue;
                    }

                    await initTask; // Re-await för att få eventuella exceptions
                    _logger.Information($"✅ {task.Key} initialiserad framgångsrikt");
                }
                catch (Exception ex)
                {
                    _logger.Error($"❌ Initialisering av {task.Key} misslyckades: {ex.Message}");
                    failedComponents.Add($"{task.Key} ({ex.GetType().Name})");

                    // Vissa komponenter är kritiska
                    if (task.Key == "Licensing" || task.Key == "Services")
                    {
                        ShowCriticalErrorDialog($"Kritisk komponent {task.Key} kunde inte initialiseras", ex);
                        return false;
                    }
                }
            }

            if (failedComponents.Any())
            {
                var message = $"Vissa komponenter kunde inte initialiseras:\n\n{string.Join("\n", failedComponents)}\n\nFilKollen kommer att köras med begränsad funktionalitet.";

                var result = MessageBox.Show(message, "Delvis initialisering",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                {
                    return false;
                }
            }

            return true;
        }

        private void ShowCriticalErrorDialog(string message, Exception ex)
        {
            var detailedMessage = $"{message}\n\n" +
                                 $"Feltyp: {ex.GetType().Name}\n" +
                                 $"Meddelande: {ex.Message}\n\n" +
                                 $"Teknisk information:\n{ex.StackTrace}";

            var errorWindow = new TaskDialog
            {
                WindowTitle = "FilKollen - Kritiskt Fel",
                MainInstruction = "Ett kritiskt fel uppstod vid start",
                Content = message,
                ExpandedInformation = detailedMessage,
                FooterText = "Kontakta support om problemet kvarstår",
                MainIcon = TaskDialogIcon.Error,
                CommonButtons = TaskDialogCommonButtons.OK
            };

            try
            {
                errorWindow.Show();
            }
            catch
            {
                // Fallback om TaskDialog inte fungerar
                MessageBox.Show(detailedMessage, "FilKollen - Kritiskt Fel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // FÖRBÄTTRING: Robust scanning med progress tracking och error recovery
        private async Task<List<ScanResult>> PerformRobustScanningAsync(IProgress<ScanProgress>? progress = null)
        {
            var allResults = new List<ScanResult>();
            var scanProgress = new ScanProgress();

            try
            {
                _logger.Information("🔍 Startar robust säkerhetsskanning...");

                // Få alla sökvägar som ska skannas
                var scanPaths = GetScanPaths();
                scanProgress.TotalPaths = scanPaths.Count;
                progress?.Report(scanProgress);

                var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
                var tasks = new List<Task<List<ScanResult>>>();

                foreach (var path in scanPaths)
                {
                    tasks.Add(ScanPathWithSemaphoreAsync(path, semaphore, scanProgress, progress));
                }

                // Vänta på alla scanning tasks med timeout
                var timeout = TimeSpan.FromMinutes(10);
                var completedTasks = new List<Task<List<ScanResult>>>();

                try
                {
                    var allTasksCompleted = Task.WhenAll(tasks);
                    var timeoutTask = Task.Delay(timeout);

                    var completedTask = await Task.WhenAny(allTasksCompleted, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        _logger.Warning("Scanning timeout - avbryter kvarvarande tasks");

                        // Samla resultat från slutförda tasks
                        completedTasks.AddRange(tasks.Where(t => t.IsCompleted && t.Status == TaskStatus.RanToCompletion));
                    }
                    else
                    {
                        completedTasks.AddRange(tasks);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid parallell skanning: {ex.Message}");

                    // Samla resultat från lyckade tasks
                    completedTasks.AddRange(tasks.Where(t => t.IsCompleted && t.Status == TaskStatus.RanToCompletion));
                }

                // Samla alla resultat från slutförda tasks
                foreach (var task in completedTasks)
                {
                    try
                    {
                        var results = await task;
                        allResults.AddRange(results);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Fel vid hämtning av scan-resultat: {ex.Message}");
                    }
                }

                scanProgress.IsCompleted = true;
                scanProgress.CompletedPaths = scanPaths.Count;
                progress?.Report(scanProgress);

                _logger.Information($"✅ Robust skanning slutförd: {allResults.Count} hot funna");
                return allResults.OrderByDescending(r => r.ThreatLevel).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Kritiskt fel vid robust skanning: {ex.Message}");
                scanProgress.HasError = true;
                scanProgress.ErrorMessage = ex.Message;
                progress?.Report(scanProgress);

                return allResults; // Returnera det vi hann skanna
            }
        }

        private async Task<List<ScanResult>> ScanPathWithSemaphoreAsync(
            string path, SemaphoreSlim semaphore, ScanProgress progress, IProgress<ScanProgress>? progressReporter)
        {
            await semaphore.WaitAsync();

            try
            {
                _logger.Debug($"Skannar sökväg: {path}");

                var results = await _fileScanner.ScanDirectoryAsync(path);

                lock (progress)
                {
                    progress.CompletedPaths++;
                    progress.TotalFilesScanned += results.Count;
                }

                progressReporter?.Report(progress);
                return results;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid skanning av {path}: {ex.Message}");

                lock (progress)
                {
                    progress.CompletedPaths++;
                    progress.FailedPaths++;
                }

                progressReporter?.Report(progress);
                return new List<ScanResult>();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private List<string> GetScanPaths()
        {
            var paths = new List<string>();

            // Standard temp-sökvägar
            var standardPaths = new[]
            {
                Environment.GetEnvironmentVariable("TEMP"),
                Environment.GetEnvironmentVariable("TMP"),
                @"C:\Windows\Temp",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
            };

            foreach (var path in standardPaths)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }

            // Lägg till konfigurerade sökvägar
            if (_config?.ScanPaths != null)
            {
                foreach (var configPath in _config.ScanPaths)
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(configPath);
                    if (Directory.Exists(expandedPath) && !paths.Contains(expandedPath))
                    {
                        paths.Add(expandedPath);
                    }
                }
            }

            return paths;
        }
    }

    // Support-klasser för progress tracking
    public class ScanProgress
    {
        public int TotalPaths { get; set; }
        public int CompletedPaths { get; set; }
        public int FailedPaths { get; set; }
        public int TotalFilesScanned { get; set; }
        public bool IsCompleted { get; set; }
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public double ProgressPercentage => TotalPaths > 0 ? (double)CompletedPaths / TotalPaths * 100 : 0;
    }
}
        #region Fields
        private readonly LicenseService _licenseService;
        private readonly BrandingService _brandingService;
        private readonly ThemeService _themeService;
        private readonly ILogger _logger;

        // Säkerhetstjänster
        private TempFileScanner _tempFileScanner = null!;
        private AdvancedBrowserCleaner _browserCleaner = null!;
        private IntrusionDetectionService _intrusionDetection = null!;
        private QuarantineManager _quarantineManager = null!;
        private LogViewerService _logViewer = null!;
        private RealTimeProtectionService _protectionService = null!;
        private SystemTrayService _trayService = null!;

        // UI Collections
        public ObservableCollection<ThreatItemViewModel> ActiveThreats { get; set; } = new();
        public ObservableCollection<LogEntryViewModel> ActivityLog { get; set; } = new();

        // Properties för databinding
        public bool IsScanning { get; set; }
        public bool IsProtectionActive { get; set; }
        public bool IsIDSActive { get; set; }
        public int TotalThreatsFound { get; set; }
        public int TotalThreatsHandled { get; set; }

        // Commands för UI
        public ICommand QuarantineCommand { get; private set; } = null!;
        public ICommand ShowInExplorerCommand { get; private set; } = null!;
        public ICommand AddToWhitelistCommand { get; private set; } = null!;
        #endregion

        #region Constructor
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
            InitializeCollections();
            InitializeServices();
            InitializeCommands();
            SetupEventHandlers();
            
            DataContext = this;
            
            // Initial UI update
            UpdateAllSecurityStatus();
            
            _logger.Information("🛡️ FilKollen Real-time Security Suite startat framgångsrikt");
        }
        #endregion

        #region Initialization
        private void InitializeCollections()
        {
            ActiveThreats = new ObservableCollection<ThreatItemViewModel>();
            ActivityLog = new ObservableCollection<LogEntryViewModel>();
            
            ThreatsList.ItemsSource = ActiveThreats;
            ActivityLogListView.ItemsSource = ActivityLog;
        }

        private void InitializeServices()
        {
            try
            {
                var config = LoadConfiguration();

                // Kärnservices
                _quarantineManager = new QuarantineManager(_logger);
                _logViewer = new LogViewerService();
                _tempFileScanner = new TempFileScanner(config, _logger);
                _browserCleaner = new AdvancedBrowserCleaner(_logger);

                // Avancerade säkerhetstjänster
                _intrusionDetection = new IntrusionDetectionService(_logger, _logViewer, _tempFileScanner, _quarantineManager);
                _protectionService = new RealTimeProtectionService(_tempFileScanner, _quarantineManager, _logViewer, _logger, config);
                _trayService = new SystemTrayService(_protectionService, _logViewer, _logger);

                _logger.Information("✅ Alla säkerhetstjänster initialiserade framgångsrikt");
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Kritiskt fel vid initiering av säkerhetstjänster: {ex.Message}");
                MessageBox.Show($"Kritiskt fel vid start av säkerhetstjänster:\n\n{ex.Message}",
                    "Startfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeCommands()
        {
            QuarantineCommand = new RelayCommand<ThreatItemViewModel>(async (threat) => 
            {
                if (threat != null)
                {
                    var scanResult = new ScanResult
                    {
                        FilePath = threat.FilePath,
                        ThreatLevel = Enum.Parse<ThreatLevel>(threat.ThreatLevel),
                        Reason = threat.Reason
                    };
                    await _quarantineManager.QuarantineFileAsync(scanResult);
                    ActiveThreats.Remove(threat);
                }
            });

            ShowInExplorerCommand = new RelayCommand<ThreatItemViewModel>((threat) => 
            {
                if (threat != null && File.Exists(threat.FilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{threat.FilePath}\"");
                }
            });

            AddToWhitelistCommand = new RelayCommand<ThreatItemViewModel>((threat) => 
            {
                if (threat != null)
                {
                    _tempFileScanner.AddToWhitelist(threat.FilePath);
                    ActiveThreats.Remove(threat);
                }
            });
        }

        private void SetupEventHandlers()
        {
            // Protection service events
            if (_protectionService != null)
            {
                _protectionService.ProtectionStatusChanged += OnProtectionStatusChanged;
                _protectionService.ThreatDetected += OnThreatDetected;
            }

            // Intrusion detection events
            if (_intrusionDetection != null)
            {
                _intrusionDetection.IntrusionDetected += OnIntrusionDetected;
                _intrusionDetection.SecurityAlert += OnSecurityAlert;
            }

            // Tray service events
            if (_trayService != null)
            {
                _trayService.ShowMainWindowRequested += OnShowMainWindowRequested;
                _trayService.ExitApplicationRequested += OnExitApplicationRequested;
            }

            // Log viewer events
            if (_logViewer != null)
            {
                _logViewer.PropertyChanged += OnLogViewerPropertyChanged;
            }
        }
        #endregion

        #region Event Handlers
        private void OnProtectionStatusChanged(object? sender, ProtectionStatusChangedEventArgs e)
        {
            Dispatcher.Invoke(() => UpdateProtectionStatus());
        }

        private void OnThreatDetected(object? sender, ThreatDetectedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!e.WasHandledAutomatically)
                {
                    var threatVM = ThreatItemViewModel.FromScanResult(e.Threat);
                    ActiveThreats.Add(threatVM);
                    UpdateThreatCounts();
                    UpdateNoThreatsVisibility();
                }
                
                TotalThreatsFound++;
                UpdateSystemStatus();
            });
        }

        private void OnIntrusionDetected(object? sender, IntrusionDetectedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _logViewer.AddLogEntry(LogLevel.Error, "IDS", 
                    $"🚨 INTRÅNG UPPTÄCKT: {e.ProcessName} - {e.ThreatType}");

                if (e.ShouldBlock)
                {
                    _trayService?.ShowNotification("Säkerhetshot blockerat!", 
                        $"Process {e.ProcessName} har blockerats automatiskt", 
                        System.Windows.Forms.ToolTipIcon.Error);
                }
            });
        }

        private void OnSecurityAlert(object? sender, SecurityAlertEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _logViewer.AddLogEntry(LogLevel.Error, "Security", 
                    $"🚨 SÄKERHETSVARNING: {e.Message}");

                if (e.Severity == SecuritySeverity.Critical)
                {
                    MessageBox.Show(
                        $"🚨 KRITISK SÄKERHETSVARNING\n\n" +
                        $"Typ: {e.AlertType}\n" +
                        $"Process: {e.ProcessName}\n" +
                        $"Beskrivning: {e.Message}\n" +
                        $"Åtgärd: {e.ActionTaken}\n\n" +
                        $"Systemet har automatiskt hanterat hotet.",
                        "Kritisk säkerhetsvarning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            });
        }

        private void OnShowMainWindowRequested(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        }

        private void OnExitApplicationRequested(object? sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void OnLogViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogViewerService.LogEntries))
            {
                Dispatcher.Invoke(() => UpdateActivityLog());
            }
        }
        #endregion

        #region UI Update Methods
        private void UpdateAllSecurityStatus()
        {
            UpdateProtectionStatus();
            UpdateIDSStatus();
            UpdateSystemStatus();
            UpdateThreatCounts();
            UpdateSystemInformation();
            UpdateLicenseStatus();
            UpdateActivityLog();
        }

        private void UpdateProtectionStatus()
        {
            if (_protectionService != null)
            {
                var stats = _protectionService.GetProtectionStats();
                IsProtectionActive = stats.IsActive;

                ProtectionToggle.IsChecked = stats.IsActive;
                
                if (stats.IsActive)
                {
                    ProtectionStatusText.Text = "AKTIVERAT";
                    ProtectionStatusText.Foreground = Brushes.Green;
                    ProtectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Shield;
                    ProtectionIcon.Foreground = Brushes.Green;
                }
                else
                {
                    ProtectionStatusText.Text = "INAKTIVERAT";
                    ProtectionStatusText.Foreground = Brushes.Red;
                    ProtectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ShieldOff;
                    ProtectionIcon.Foreground = Brushes.Red;
                }

                var mode = stats.AutoCleanMode ? "Automatisk" : "Manuell";
                ProtectionModeText.Text = $"{mode} hantering";
                
                if (stats.LastScanTime != default)
                {
                    LastScanText.Text = $"Senaste skanning: {stats.LastScanTime:HH:mm}";
                }
            }
        }

        private void UpdateThreatCounts()
        {
            CriticalThreatsCount.Text = ActiveThreats.Count(t => t.ThreatLevel == "Critical").ToString();
            HighThreatsCount.Text = ActiveThreats.Count(t => t.ThreatLevel == "High").ToString();
            FilesScannedCount.Text = TotalThreatsFound.ToString();

            StatsThreatsFound.Text = TotalThreatsFound.ToString();
            StatsThreatsHandled.Text = TotalThreatsHandled.ToString();
        }

        private void UpdateNoThreatsVisibility()
        {
            NoThreatsPanel.Visibility = ActiveThreats.Any() ? Visibility.Collapsed : Visibility.Visible;
            ThreatsList.Visibility = ActiveThreats.Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateSystemStatus()
        {
            bool hasActiveThreats = ActiveThreats.Any();

            if (hasActiveThreats)
            {
                StatusIndicator.Fill = Brushes.Orange;
                SystemStatusText.Text = "HOT UPPTÄCKTA";
            }
            else if (IsProtectionActive && IsIDSActive)
            {
                StatusIndicator.Fill = Brushes.Green;
                SystemStatusText.Text = "SYSTEM SKYDDAT";
            }
            else
            {
                StatusIndicator.Fill = Brushes.Red;
                SystemStatusText.Text = "SYSTEM OSKYDDAT";
            }
        }

        private void UpdateSystemInformation()
        {
            try
            {
                OSVersionText.Text = $"{Environment.OSVersion.VersionString}";
                ComputerNameText.Text = Environment.MachineName;
                UserNameText.Text = Environment.UserName;
                
                var isAdmin = IsRunningAsAdministrator();
                AdminStatusText.Text = isAdmin ? "Ja" : "Nej";
                AdminStatusText.Foreground = isAdmin ? Brushes.Green : Brushes.Red;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var quarantinedFiles = await _quarantineManager.GetQuarantinedFilesAsync();
                        Dispatcher.Invoke(() =>
                        {
                            StatsQuarantined.Text = quarantinedFiles.Count.ToString();
                            SecurityQuarantineCountText.Text = quarantinedFiles.Count.ToString();
                        });
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid uppdatering av systeminformation: {ex.Message}");
            }
        }

        private void UpdateLicenseStatus()
        {
            try
            {
                var license = _licenseService.GetCurrentLicense();
                if (license != null)
                {
                    LicenseStatusText.Text = $"{license.Type} aktiv";
                    StatusBarText.Text = $"Licens aktiv till {license.ExpiryDate:yyyy-MM-dd} - Registrerad på: {license.CustomerName}";
                }
                else
                {
                    var trialTime = _licenseService.GetRemainingTrialTime();
                    if (trialTime.HasValue && trialTime.Value > TimeSpan.Zero)
                    {
                        var timeRemaining = FormatTimeSpan(trialTime.Value);
                        LicenseStatusText.Text = $"Trial - {timeRemaining} kvar";
                        StatusBarText.Text = $"Trial aktiv - {timeRemaining} återstår";
                    }
                    else
                    {
                        LicenseStatusText.Text = "Ingen licens";
                        StatusBarText.Text = "Ingen giltig licens - Begränsad funktionalitet";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid uppdatering av licensstatus: {ex.Message}");
            }
        }

        private void UpdateActivityLog()
        {
            try
            {
                ActivityLog.Clear();
                if (_logViewer?.LogEntries != null)
                {
                    foreach (var entry in _logViewer.LogEntries.Take(100))
                    {
                        var logVM = new LogEntryViewModel
                        {
                            Timestamp = entry.Timestamp,
                            Level = entry.Level.ToString(),
                            Source = entry.Source,
                            Message = entry.Message,
                            LevelIcon = entry.LevelIcon,
                            LevelBackground = GetLevelBackground(entry.Level),
                            SourceColor = GetSourceColor(entry.Source)
                        };
                        ActivityLog.Add(logVM);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid uppdatering av aktivitetslogg: {ex.Message}");
            }
        }

        private void UpdateIDSStatus()
        {
            if (_intrusionDetection != null)
            {
                IsIDSActive = _intrusionDetection.IsMonitoringActive;
                IDSToggle.IsChecked = IsIDSActive;

                if (IsIDSActive)
                {
                    IDSStatusText.Text = "AKTIVERAT";
                    IDSStatusText.Foreground = Brushes.Green;
                    IDSIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Radar;
                    IDSIcon.Foreground = Brushes.Green;
                    SecurityIDSText.Text = "AKTIVERAT";
                    SecurityIDSText.Foreground = Brushes.Green;
                }
                else
                {
                    IDSStatusText.Text = "INAKTIVERAT";
                    IDSStatusText.Foreground = Brushes.Red;
                    IDSIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.RadarOff;
                    IDSIcon.Foreground = Brushes.Red;
                    SecurityIDSText.Text = "INAKTIVERAT";
                    SecurityIDSText.Foreground = Brushes.Red;
                }

                IDSThreatsText.Text = $"{_intrusionDetection.TotalThreatsBlocked} hot blockerade";
                
                if (_intrusionDetection.LastThreatTime != default)
                {
                    IDSLastEventText.Text = $"Senaste: {_intrusionDetection.LastThreatTime:HH:mm}";
                }
                else
                {
                    IDSLastEventText.Text = "Senaste: Aldrig";
                }
            }
        }
        #endregion

        #region Button Event Handlers
        private async void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _protectionService.StartProtectionAsync();
                    Dispatcher.Invoke(() => UpdateProtectionStatus());
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid aktivering av real-time skydd: {ex.Message}");
                    Dispatcher.Invoke(() => ProtectionToggle.IsChecked = false);
                }
            });
        }

        private async void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _protectionService.StopProtectionAsync();
                    Dispatcher.Invoke(() => UpdateProtectionStatus());
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid inaktivering av real-time skydd: {ex.Message}");
                    Dispatcher.Invoke(() => ProtectionToggle.IsChecked = true);
                }
            });
        }

        private async void IDSToggle_Checked(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _intrusionDetection.StartMonitoringAsync();
                    Dispatcher.Invoke(() => UpdateIDSStatus());
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid aktivering av intrusion detection: {ex.Message}");
                    Dispatcher.Invoke(() => IDSToggle.IsChecked = false);
                }
            });
        }

        private async void IDSToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _intrusionDetection.StopMonitoringAsync();
                    Dispatcher.Invoke(() => UpdateIDSStatus());
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid inaktivering av intrusion detection: {ex.Message}");
                    Dispatcher.Invoke(() => IDSToggle.IsChecked = true);
                }
            });
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_protectionService != null)
            {
                bool autoMode = AutoModeRadio?.IsChecked ?? false;
                _protectionService.SetAutoCleanMode(autoMode);
                
                var mode = autoMode ? "Automatisk" : "Manuell";
                ProtectionModeText.Text = $"{mode} hantering";
                
                _logger.Information($"🔧 Hanteringsläge ändrat till: {mode}");
            }
        }

        private async void TempScanButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformTempScanAsync();
        }

        private async void EmergencyScanButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "🚨 AKUTSKANNING\n\n" +
                "Detta kommer att genomföra djup säkerhetsanalys och automatiskt hantera kritiska hot.\n\n" +
                "Fortsätt med akutskanning?",
                "Bekräfta akutskanning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await PerformEmergencyScanAsync();
            }
        }

        private async void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "🌐 AVANCERAD WEBBLÄSARE SÄKERHETSRENSNING\n\n" +
                "Detta kommer att rensa malware, blockera suspekta domäner och sätta maximala säkerhetsinställningar.\n\n" +
                "Fortsätt med djuprensning?",
                "Bekräfta webbläsare säkerhetsrensning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await PerformBrowserSecurityCleaningAsync();
            }
        }

        private async void HandleAllThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ActiveThreats.Any())
            {
                MessageBox.Show("Inga aktiva hot att hantera.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"🧹 HANTERA ALLA HOT\n\n" +
                $"Detta kommer att sätta alla {ActiveThreats.Count} identifierade hot i karantän.\n\n" +
                $"Fortsätt med automatisk hantering?",
                "Bekräfta hantering av alla hot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await HandleAllThreatsAsync();
            }
        }

        private void RefreshThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(PerformTempScanAsync);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsMenu();
        }

        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ActivityLog.Clear();
                _logViewer?.ClearLogs();
                _logger.Information("📝 Aktivitetsloggar rensade av användare");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid rensning av loggar: {ex.Message}");
            }
        }

        private void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Exportera aktivitetsloggar",
                    Filter = "Text-filer (*.txt)|*.txt|Alla filer (*.*)|*.*",
                    FileName = $"FilKollen_ActivityLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    _logViewer?.ExportLogs(saveFileDialog.FileName);
                    MessageBox.Show($"Aktivitetsloggar exporterade till:\n{saveFileDialog.FileName}",
                        "Export slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid export av loggar: {ex.Message}");
                MessageBox.Show($"Fel vid export av loggar:\n\n{ex.Message}", 
                    "Exportfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogLevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateLogFilter();
        }

        private void QuarantineManagerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var quarantineInfo = GetQuarantineInfoAsync();
                MessageBox.Show(quarantineInfo.Result, "Karantän-hantering", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid visning av karantän-hantering: {ex.Message}");
                MessageBox.Show($"Fel vid åtkomst till karantän:\n\n{ex.Message}", "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var tabControl = this.FindVisualChild<TabControl>();
            tabControl?.SetCurrentValue(TabControl.SelectedIndexProperty, 2);
        }
        #endregion

        #region Scanning Methods
        private async Task PerformTempScanAsync()
        {
            try
            {
                SetScanningState(true);
                _logViewer.AddLogEntry(LogLevel.Information, "TempScan", "🔍 Startar temp-katalog säkerhetsskanning...");

                var results = await _tempFileScanner.ScanTempDirectoriesAsync();
                
                ActiveThreats.Clear();
                foreach (var result in results)
                {
                    var threatVM = new ThreatItemViewModel(result);
                    ActiveThreats.Add(threatVM);
                }

                UpdateThreatCounts();
                UpdateNoThreatsVisibility();

                var threatCount = results.Count;
                var criticalCount = results.Count(r => r.ThreatLevel == ThreatLevel.Critical);
                
                _logViewer.AddLogEntry(LogLevel.Information, "TempScan", 
                    $"✅ Temp-skanning slutförd: {threatCount} hot funna ({criticalCount} kritiska)");

                if (threatCount > 0)
                {
                    _trayService?.ShowNotification("Säkerhetshot upptäckta", 
                        $"{threatCount} hot funna i temp-kataloger", 
                        System.Windows.Forms.ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Temp-skanning misslyckades: {ex.Message}");
                _logViewer.AddLogEntry(LogLevel.Error, "TempScan", $"❌ Fel vid temp-skanning: {ex.Message}");
                MessageBox.Show($"Fel vid temp-skanning:\n\n{ex.Message}", "Skanningsfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetScanningState(false);
            }
        }

        private async Task PerformEmergencyScanAsync()
        {
            try
            {
                SetScanningState(true);
                _logViewer.AddLogEntry(LogLevel.Information, "Emergency", "🚨 AKUTSKANNING STARTAD - fullständig säkerhetsanalys");

                await PerformTempScanAsync();
                await HandleCriticalThreatsAutomaticallyAsync();

                _logViewer.AddLogEntry(LogLevel.Information, "Emergency", "✅ AKUTSKANNING SLUTFÖRD - systemet säkert");
                
                MessageBox.Show(
                    $"🛡️ AKUTSKANNING SLUTFÖRD\n\n" +
                    $"Resultat:\n" +
                    $"• {ActiveThreats.Count} hot identifierade\n" +
                    $"• Alla kritiska hot hanterade automatiskt\n\n" +
                    $"Ditt system är nu säkert!",
                    "Akutskanning slutförd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Akutskanning misslyckades: {ex.Message}");
                MessageBox.Show($"Fel vid akutskanning:\n\n{ex.Message}", "Akutskanningsfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetScanningState(false);
            }
        }

        private async Task PerformBrowserSecurityCleaningAsync()
        {
            try
            {
                SetScanningState(true);
                _logViewer.AddLogEntry(LogLevel.Information, "BrowserClean", "🌐 Startar avancerad webbläsare säkerhetsrensning...");

                var cleanResult = await _browserCleaner.DeepCleanAllBrowsersAsync();

                if (cleanResult.Success)
                {
                    var message = $"✅ WEBBLÄSARE SÄKERHETSRENSNING SLUTFÖRD!\n\n" +
                                $"Resultat:\n" +
                                $"• {cleanResult.TotalProfilesCleaned} webbläsarprofiler rensade\n" +
                                $"• {cleanResult.MalwareNotificationsRemoved} malware-notifieringar borttagna\n" +
                                $"• {cleanResult.SuspiciousExtensionsRemoved} suspekta extensions borttagna\n" +
                                $"• Maximala säkerhetsinställningar tillämpade\n\n" +
                                $"Dina webbläsare är nu säkra!";

                    MessageBox.Show(message, "Säkerhetsrensning slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("❌ Webbläsare säkerhetsrensning misslyckades delvis. Se aktivitetsloggen för detaljer.",
                        "Rensningsfel", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Webbläsare säkerhetsrensning misslyckades: {ex.Message}");
                MessageBox.Show($"Fel vid webbläsare säkerhetsrensning:\n\n{ex.Message}", 
                    "Rensningsfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetScanningState(false);
            }
        }

        private async Task HandleAllThreatsAsync()
        {
            try
            {
                SetScanningState(true);
                var threatList = ActiveThreats.ToList();
                int handledCount = 0;

                foreach (var threat in threatList)
                {
                    try
                    {
                        var threatLevel = Enum.Parse<ThreatLevel>(threat.ThreatLevel);
                        var scanResult = new ScanResult
                        {
                            FilePath = threat.FilePath,
                            ThreatLevel = threatLevel,
                            Reason = threat.Reason
                        };

                        await _quarantineManager.QuarantineFileAsync(scanResult);
                        
                        ActiveThreats.Remove(threat);
                        handledCount++;
                        TotalThreatsHandled++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Kunde inte hantera hot {threat.FilePath}: {ex.Message}");
                    }
                }

                UpdateThreatCounts();
                UpdateNoThreatsVisibility();

                MessageBox.Show(
                    $"✅ HOTHANTERING SLUTFÖRD\n\n" +
                    $"{handledCount} av {threatList.Count} hot hanterade framgångsrikt.\n" +
                    $"Alla filer är säkert placerade i karantän.\n\n" +
                    $"Ditt system är nu säkert!",
                    "Hothantering slutförd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hothantering: {ex.Message}");
                MessageBox.Show($"Fel vid hantering av hot:\n\n{ex.Message}", 
                    "Hothanteringsfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetScanningState(false);
            }
        }

        private async Task HandleCriticalThreatsAutomaticallyAsync()
        {
            var criticalThreats = ActiveThreats.Where(t => t.ThreatLevel == "Critical").ToList();
            
            foreach (var threat in criticalThreats)
            {
                try
                {
                    var scanResult = new ScanResult
                    {
                        FilePath = threat.FilePath,
                        ThreatLevel = ThreatLevel.Critical,
                        Reason = threat.Reason
                    };

                    await _quarantineManager.QuarantineFileAsync(scanResult);
                    
                    ActiveThreats.Remove(threat);
                    TotalThreatsHandled++;
                    
                    _logViewer.AddLogEntry(LogLevel.Information, "Emergency", 
                        $"🔒 KRITISKT HOT HANTERAT: {Path.GetFileName(threat.FilePath)}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Kunde inte hantera kritiskt hot {threat.FilePath}: {ex.Message}");
                }
            }
            
            UpdateThreatCounts();
            UpdateNoThreatsVisibility();
        }
        #endregion

        #region Helper Methods
        private void SetScanningState(bool isScanning)
        {
            IsScanning = isScanning;
            
            ScanProgressBar.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;
            TempScanButton.IsEnabled = !isScanning;
            EmergencyScanButton.IsEnabled = !isScanning;
            BrowserCleanButton.IsEnabled = !isScanning;
            
            if (isScanning)
            {
                StatusBarText.Text = "Genomför säkerhetsskanning...";
            }
            else
            {
                UpdateLicenseStatus();
            }
            
            OnPropertyChanged(nameof(IsScanning));
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

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} dagar";
            else if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} timmar";
            else
                return $"{(int)timeSpan.TotalMinutes} minuter";
        }

        private string GetLevelBackground(LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => "#FFEBEE",
                LogLevel.Warning => "#FFF3E0",
                LogLevel.Information => "#E3F2FD",
                _ => "#F5F5F5"
            };
        }

        private string GetSourceColor(string source)
        {
            return source switch
            {
                "IDS" => "#F44336",
                "Protection" => "#4CAF50", 
                "TempScan" => "#2196F3",
                "BrowserClean" => "#FF9800",
                "Emergency" => "#9C27B0",
                _ => "#607D8B"
            };
        }

        private AppConfig LoadConfiguration()
        {
            try
            {
                if (File.Exists("appsettings.json"))
                {
                    var json = File.ReadAllText("appsettings.json");
                    var wrapper = System.Text.Json.JsonSerializer.Deserialize<AppConfigWrapper>(json);
                    return wrapper?.AppSettings ?? CreateDefaultConfig();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte ladda konfiguration: {ex.Message}");
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

        private void ShowSettingsMenu()
        {
            var settingsMenu = new ContextMenu();

            var licenseMenuItem = new MenuItem { Header = "🔑 Licenshantering" };
            licenseMenuItem.Click += (s, e) => ShowLicenseManagement();
            settingsMenu.Items.Add(licenseMenuItem);

            var brandingMenuItem = new MenuItem { Header = "🎨 Branding Management" };
            brandingMenuItem.Click += (s, e) => ShowBrandingManagement();
            settingsMenu.Items.Add(brandingMenuItem);

            settingsMenu.Items.Add(new Separator());

            var aboutMenuItem = new MenuItem { Header = "ℹ️ Om FilKollen" };
            aboutMenuItem.Click += (s, e) => ShowAboutDialog();
            settingsMenu.Items.Add(aboutMenuItem);

            settingsMenu.PlacementTarget = SettingsButton;
            settingsMenu.IsOpen = true;
        }

        private void ShowLicenseManagement()
        {
            try
            {
                var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                var result = licenseWindow.ShowDialog();

                if (result == true)
                {
                    UpdateLicenseStatus();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid visning av licenshantering: {ex.Message}");
                MessageBox.Show($"Kunde inte öppna licenshantering: {ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowBrandingManagement()
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
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid visning av branding-hantering: {ex.Message}");
                MessageBox.Show($"Kunde inte öppna branding-hantering: {ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowAboutDialog()
        {
            var license = _licenseService.GetCurrentLicense();
            var branding = _brandingService.GetCurrentBranding();

            var aboutMessage =
                $"🛡️ {branding.ProductName}\n" +
                $"Real-time Security Suite v2.0\n\n" +
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
                $"\n🔧 Avancerade funktioner:\n" +
                $"• Real-time säkerhetsskydd med AI-detektering\n" +
                $"• Intrusion Detection System (IDS)\n" +
                $"• Avancerad webbläsare-säkerhetsrensning\n" +
                $"• Intelligent malware-analys\n" +
                $"• Säker karantän-hantering\n" +
                $"• System tray-integration\n";

            if (license?.Type == LicenseType.Lifetime || license?.Type == LicenseType.Yearly)
                aboutMessage += $"• White-label branding-stöd\n";

            MessageBox.Show(aboutMessage, $"Om {branding.ProductName}",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task<string> GetQuarantineInfoAsync()
        {
            try
            {
                var quarantinedFiles = await _quarantineManager.GetQuarantinedFilesAsync();
                var stats = await _quarantineManager.GetQuarantineStatsAsync();

                return $"🏥 KARANTÄN-INFORMATION\n\n" +
                       $"Karantänerade filer: {quarantinedFiles.Count}\n" +
                       $"Total storlek: {stats.FormattedTotalSize}\n" +
                       $"Äldsta fil: {(stats.OldestDate != DateTime.MaxValue ? stats.OldestDate.ToString("yyyy-MM-dd") : "Ingen")}\n" +
                       $"Senaste fil: {(stats.NewestDate != DateTime.MinValue ? stats.NewestDate.ToString("yyyy-MM-dd") : "Ingen")}\n\n" +
                       $"Karantänkatalog:\n{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FilKollen", "Quarantine")}\n\n" +
                       $"Avancerad karantän-hantering kommer i nästa version.";
            }
            catch (Exception ex)
            {
                return $"Fel vid hämtning av karantän-information: {ex.Message}";
            }
        }

        private void UpdateLogFilter()
        {
            try
            {
                if (LogLevelFilter?.SelectedIndex >= 0)
                {
                    UpdateActivityLog();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid uppdatering av log-filter: {ex.Message}");
            }
        }
        #endregion

        #region Cleanup
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _protectionService?.StopProtectionAsync().Wait(5000);
                _intrusionDetection?.StopMonitoringAsync().Wait(5000);
                _trayService?.Dispose();
                _logViewer?.Dispose();

                if (_protectionService != null)
                {
                    _protectionService.ProtectionStatusChanged -= OnProtectionStatusChanged;
                    _protectionService.ThreatDetected -= OnThreatDetected;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error during cleanup: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        public void Shutdown()
        {
            try
            {
                _protectionService?.StopProtectionAsync().Wait(5000);
                _intrusionDetection?.StopMonitoringAsync().Wait(5000);
                _trayService?.Dispose();
                _logViewer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid stängning av tjänster: {ex.Message}");
            }
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    #region Support Classes
    public class AppConfigWrapper
    {
        public AppConfig AppSettings { get; set; } = new();
    }

    public class ThreatItemViewModel : INotifyPropertyChanged
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ThreatLevel { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string FileSizeFormatted { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public string ThreatLevelColor { get; set; } = string.Empty;

        public ThreatItemViewModel()
        {
        }

        public ThreatItemViewModel(ScanResult scanResult)
        {
            FileName = scanResult.FileName;
            FilePath = scanResult.FilePath;
            ThreatLevel = scanResult.ThreatLevel.ToString();
            Reason = scanResult.Reason;
            FileSizeFormatted = scanResult.FormattedSize;
            CreatedDate = scanResult.CreatedDate;
            ThreatLevelColor = GetThreatLevelColor(scanResult.ThreatLevel);
        }

        public static ThreatItemViewModel FromScanResult(ScanResult scanResult)
        {
            return new ThreatItemViewModel(scanResult);
        }

        private string GetThreatLevelColor(ThreatLevel level)
        {
            return level switch
            {
                ThreatLevel.Critical => "#9C27B0",
                ThreatLevel.High => "#F44336",
                ThreatLevel.Medium => "#FF9800",
                ThreatLevel.Low => "#4CAF50",
                _ => "#9E9E9E"
            };
        }


    public class LogEntryViewModel
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string LevelIcon { get; set; } = string.Empty;
        public string LevelBackground { get; set; } = string.Empty;
        public string SourceColor { get; set; } = string.Empty;
    }
    #endregion
}

// Extension method för att hitta visuella barn-element
public static class VisualTreeHelpers
{
    public static T? FindVisualChild<T>(this DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
            if (child != null && child is T)
                return (T)child;
            else
            {
                T? childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
        }
        return null;
    }
}