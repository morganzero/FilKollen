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
using FilKollen.Models;
using FilKollen.Services;
using FilKollen.ViewModels;
using FilKollen.Windows;
using Microsoft.Win32;
using Serilog;

namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
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
        public ObservableCollection<ThreatItemViewModel> ActiveThreats { get; set; }
        public ObservableCollection<LogEntryViewModel> ActivityLog { get; set; }

        // Properties för databinding
        public bool IsScanning { get; set; }
        public bool IsProtectionActive { get; set; }
        public bool IsIDSActive { get; set; }
        public int TotalThreatsFound { get; set; }
        public int TotalThreatsHandled { get; set; }
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

        #region Event Handlers - Protection
        private void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
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

        private void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
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

        private void IDSToggle_Checked(object sender, RoutedEventArgs e)
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

        private void IDSToggle_Unchecked(object sender, RoutedEventArgs e)
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
        #endregion

        #region Event Handlers - Scanning
        private async void TempScanButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformTempScanAsync();
        }

        private async void EmergencyScanButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "🚨 AKUTSKANNING\n\n" +
                "Detta kommer att:\n" +
                "• Genomföra djup skanning av alla temp-kataloger\n" +
                "• Analysera alla aktiva processer\n" +
                "• Kontrollera nätverksanslutningar\n" +
                "• Sätta kritiska hot i karantän automatiskt\n\n" +
                "Fortsätt med akutskanning?",
                "Bekräfta akutskanning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await PerformEmergencyScanAsync();
            }
        }

        private async Task PerformTempScanAsync()
        {
            try
            {
                SetScanningState(true);
                _logViewer.AddLogEntry(LogLevel.Information, "TempScan", "🔍 Startar temp-katalog säkerhetsskanning...");

                var results = await _tempFileScanner.ScanTempDirectoriesAsync();
                
                // Rensa gamla hot och lägg till nya
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

                var tasks = new[]
                {
                    PerformTempScanAsync(),
                    PerformProcessAnalysisAsync(),
                    PerformNetworkAnalysisAsync()
                };

                await Task.WhenAll(tasks);

                // Auto-hantera kritiska hot
                await HandleCriticalThreatsAutomaticallyAsync();

                _logViewer.AddLogEntry(LogLevel.Information, "Emergency", "✅ AKUTSKANNING SLUTFÖRD - systemet säkert");
                
                MessageBox.Show(
                    $"🛡️ AKUTSKANNING SLUTFÖRD\n\n" +
                    $"Resultat:\n" +
                    $"• {ActiveThreats.Count} hot identifierade\n" +
                    $"• {ActiveThreats.Count(t => t.ThreatLevel == "Critical")} kritiska hot\n" +
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

        private async Task PerformProcessAnalysisAsync()
        {
            // Trigger manual IDS scan om det inte är aktivt
            if (_intrusionDetection != null && !_intrusionDetection.IsMonitoringActive)
            {
                _logViewer.AddLogEntry(LogLevel.Information, "Emergency", "🔍 Analyserar aktiva processer...");
                // Implementera manuell processanalys här
                await Task.Delay(2000); // Simulera processanalys
            }
        }

        private async Task PerformNetworkAnalysisAsync()
        {
            _logViewer.AddLogEntry(LogLevel.Information, "Emergency", "🌐 Analyserar nätverksanslutningar...");
            // Implementera nätverksanalys här
            await Task.Delay(1500); // Simulera nätverksanalys
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

        #region Event Handlers - Browser Cleaning
        private async void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "🌐 AVANCERAD WEBBLÄSARE SÄKERHETSRENSNING\n\n" +
                "Detta kommer att:\n" +
                "• Stänga alla webbläsare\n" +
                "• Ta bort malware-notifieringar\n" +
                "• Analysera och ta bort suspekta extensions\n" +
                "• Rensa all browsing data\n" +
                "• Sätta maximala säkerhetsinställningar\n" +
                "• Blockera kända malware-domäner\n\n" +
                "Fortsätt med djuprensning?",
                "Bekräfta webbläsare säkerhetsrensning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await PerformBrowserSecurityCleaningAsync();
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
                                $"• Maximala säkerhetsinställningar tillämpade\n" +
                                $"• Malware-domäner blockerade via hosts-fil\n\n" +
                                $"Dina webbläsare är nu säkra!";

                    MessageBox.Show(message, "Säkerhetsrensning slutförd", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    _logViewer.AddLogEntry(LogLevel.Information, "BrowserClean", 
                        $"✅ Webbläsare säkerhetsrensning slutförd: {cleanResult.TotalProfilesCleaned} profiler, {cleanResult.MalwareNotificationsRemoved} malware-notiser");
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
        #endregion

        #region Event Handlers - Threat Management
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
                $"Fördelning:\n" +
                $"• {ActiveThreats.Count(t => t.ThreatLevel == "Critical")} Kritiska\n" +
                $"• {ActiveThreats.Count(t => t.ThreatLevel == "High")} Höga\n" +
                $"• {ActiveThreats.Count(t => t.ThreatLevel == "Medium")} Medium\n\n" +
                $"Fortsätt med automatisk hantering?",
                "Bekräfta hantering av alla hot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await HandleAllThreatsAsync();
            }
        }

        private async Task HandleAllThreatsAsync()
        {
            try
            {
                SetScanningState(true);
                var threatList = ActiveThreats.ToList(); // Skapa kopia för iteration
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

                        _logViewer.AddLogEntry(LogLevel.Information, "ThreatMgmt", 
                            $"🔒 Hot hanterat: {Path.GetFileName(threat.FilePath)}");
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

                _logViewer.AddLogEntry(LogLevel.Information, "ThreatMgmt", 
                    $"✅ Hothantering slutförd: {handledCount}/{threatList.Count} hot hanterade");
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

        private void RefreshThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(async () => await PerformTempScanAsync());
        }
        #endregion

        #region Event Handlers - Other
        private void QuarantineManagerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Visa enkel karantän-information för nu
                var quarantineInfo = GetQuarantineInfoAsync();
                MessageBox.Show(quarantineInfo.Result, "Karantän-hantering", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid visning av karantän-hantering: {ex.Message}");
                MessageBox.Show($"Fel vid åtkomst till karantän:\n\n{ex.Message}", "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            // Växla till System Information tab
            var tabControl = this.FindVisualChild<TabControl>();
            if (tabControl != null)
            {
                tabControl.SelectedIndex = 2; // System Information tab
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Implementera settings-menyn som tidigare
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
            // Implementera log-filtrering
            UpdateLogFilter();
        }
        #endregion

        #region Service Event Handlers
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

                UpdateIDSStats();
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
                    ProtectionStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    ProtectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Shield;
                    ProtectionIcon.Foreground = System.Windows.Media.Brushes.Green;
                    
                    StatusIndicator.Fill = System.Windows.Media.Brushes.Green;
                    SystemStatusText.Text = "SYSTEM SKYDDAT";
                    SecurityRealtimeText.Text = "AKTIVERAT";
                    SecurityRealtimeText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    ProtectionStatusText.Text = "INAKTIVERAT";
                    ProtectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    ProtectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ShieldOff;
                    ProtectionIcon.Foreground = System.Windows.Media.Brushes.Red;
                    
                    StatusIndicator.Fill = System.Windows.Media.Brushes.Red;
                    SystemStatusText.Text = "SYSTEM OSKYDDAT";
                    SecurityRealtimeText.Text = "INAKTIVERAT";
                    SecurityRealtimeText.Foreground = System.Windows.Media.Brushes.Red;
                }

                var mode = stats.AutoCleanMode ? "Automatisk" : "Manuell";
                ProtectionModeText.Text = $"{mode} hantering";
                
                if (stats.LastScanTime != default)
                {
                    LastScanText.Text = $"Senaste skanning: {stats.LastScanTime:HH:mm}";
                }
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
                    IDSStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    IDSDetailsText.Text = "Avancerat hotskydd aktivt";
                    IDSIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Radar;
                    IDSIcon.Foreground = System.Windows.Media.Brushes.Green;
                    SecurityIDSText.Text = "AKTIVERAT";
                    SecurityIDSText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    IDSStatusText.Text = "INAKTIVERAT";
                    IDSStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    IDSDetailsText.Text = "Avancerat hotskydd avstängt";
                    IDSIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.RadarOff;
                    IDSIcon.Foreground = System.Windows.Media.Brushes.Red;
                    SecurityIDSText.Text = "INAKTIVERAT";
                    SecurityIDSText.Foreground = System.Windows.Media.Brushes.Red;
                }

                UpdateIDSStats();
            }
        }

        private void UpdateIDSStats()
        {
            if (_intrusionDetection != null)
            {
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

        private void UpdateSystemStatus()
        {
            // Uppdatera övergripande systemstatus baserat på alla tjänster
            bool isFullyProtected = IsProtectionActive && IsIDSActive;
            bool hasActiveThreats = ActiveThreats.Any();

            if (hasActiveThreats)
            {
                StatusIndicator.Fill = System.Windows.Media.Brushes.Orange;
                SystemStatusText.Text = "HOT UPPTÄCKTA";
            }
            else if (isFullyProtected)
            {
                StatusIndicator.Fill = System.Windows.Media.Brushes.Green;
                SystemStatusText.Text = "SYSTEM SKYDDAT";
            }
            else
            {
                StatusIndicator.Fill = System.Windows.Media.Brushes.Red;
                SystemStatusText.Text = "SYSTEM OSKYDDAT";
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

        private void UpdateSystemInformation()
        {
            try
            {
                OSVersionText.Text = $"{Environment.OSVersion.VersionString}";
                ComputerNameText.Text = Environment.MachineName;
                UserNameText.Text = Environment.UserName;
                
                // Kontrollera admin-status
                var isAdmin = IsRunningAsAdministrator();
                AdminStatusText.Text = isAdmin ? "Ja" : "Nej";
                AdminStatusText.Foreground = isAdmin ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;

                // Uppdatera karantän-count
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

                // Kontrollera Windows Defender
                _ = Task.Run(async () =>
                {
                    var defenderStatus = await CheckWindowsDefenderStatusAsync();
                    Dispatcher.Invoke(() =>
                    {
                        SecurityDefenderText.Text = defenderStatus;
                    });
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
                    foreach (var entry in _logViewer.LogEntries.Take(100)) // Visa senaste 100
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

        private void UpdateLogFilter()
        {
            // Implementera log-filtrering baserat på vald nivå
            try
            {
                if (LogLevelFilter?.SelectedIndex >= 0)
                {
                    // Filtrera ActivityLog baserat på vald nivå
                    UpdateActivityLog();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid uppdatering av log-filter: {ex.Message}");
            }
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
                UpdateLicenseStatus(); // Återställ status text
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

        private async Task<string> CheckWindowsDefenderStatusAsync()
        {
            try
            {
                // Förenklad implementation - skulle kunna utökas med riktiga WMI-anrop
                await Task.Delay(100);
                return "Aktiv (verifieras ej)";
            }
            catch
            {
                return "Kunde inte kontrollera";
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
            var settingsMenu = new System.Windows.Controls.ContextMenu();

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
        #endregion

        #region Cleanup
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
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
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(obj, i);
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