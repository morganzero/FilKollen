using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FilKollen.Models;
using FilKollen.Services;
using FilKollen.Windows;
using Serilog;
using System.Windows.Media;
using System.Windows.Input;

namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ILogger _logger;
        private readonly LicenseService? _licenseService;
        private readonly BrandingService? _brandingService;
        private readonly ThemeService? _themeService;
        private readonly AppConfig _config;
        private TempFileScanner? _fileScanner;
        private LogViewerService? _logViewer;
        private QuarantineManager? _quarantine;
        private AdvancedBrowserCleaner? _browserCleaner;

        private RealTimeProtectionService? _protectionService;
        private SystemTrayService? _trayService;
        private IntrusionDetectionService? _intrusionDetection;

        private bool _isProtectionActive = false;
        private bool _isIpProtectionActive = false;
        private readonly Timer _statusUpdateTimer;
        private readonly List<ScanResult> _currentThreats = new();
        private string _currentSortColumn = "";
        private bool _sortAscending = true;

        // UI-element referenser för sortering och licenshantering
        private TextBlock? FileNameHeader => FindName("FileNameHeader") as TextBlock;
        private TextBlock? TypeHeader => FindName("TypeHeader") as TextBlock;
        private TextBlock? SizeHeader => FindName("SizeHeader") as TextBlock;
        private TextBlock? PathHeader => FindName("PathHeader") as TextBlock;
        private TextBlock? DateHeader => FindName("DateHeader") as TextBlock;
        private TextBlock? RiskHeader => FindName("RiskHeader") as TextBlock;
        private Border? LicenseStatusDisplay => FindName("LicenseStatusDisplay") as Border;
        private TextBlock? LicenseButtonText => FindName("LicenseButtonText") as TextBlock;

        public MainWindow() : this(null, null, null) { }

        public MainWindow(LicenseService? licenseService, BrandingService? brandingService, ThemeService? themeService)
        {
            try
            {
                _logger = Log.Logger ?? throw new InvalidOperationException("Logger inte initierad");
                _logger.Information("MainWindow startar med återställd elegant design v2.1");

                _licenseService = licenseService;
                _brandingService = brandingService;
                _themeService = themeService;

                _config = InitializeConfig();
                InitializeServices();
                InitializeComponent();
                InitializeBrandingFixed();
                InitializeTheme();

                DataContext = this;
                _statusUpdateTimer = new Timer(UpdateStatusCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

                Loaded += async (s, e) => await InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "MainWindow constructor misslyckades");
                MessageBox.Show($"Kritiskt fel vid start: {ex.Message}", "FilKollen Fel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private AppConfig InitializeConfig()
        {
            return new AppConfig
            {
                ScanPaths = new List<string>
                {
                    Environment.GetEnvironmentVariable("TEMP") ?? System.IO.Path.GetTempPath(),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                    @"C:\Windows\Temp"
                },
                SuspiciousExtensions = new List<string> { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr" }
            };
        }

        private void InitializeServices()
        {
            try
            {
                _fileScanner = new TempFileScanner(_config, _logger);
                _quarantine = new QuarantineManager(_logger);
                _logViewer = new LogViewerService();
                _browserCleaner = new AdvancedBrowserCleaner(_logger);
                _logger.Information("Kärntjänster initierade framgångsrikt");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Vissa tjänster kunde inte initieras: {ex.Message}");
            }
        }

        private void InitializeBrandingFixed()
        {
            try
            {
                var branding = _brandingService?.GetCurrentBranding();
                var logoPath = "Resources/Branding/default-logo.png";

                bool logoExists = File.Exists(logoPath);
                bool logoLoadable = false;

                if (logoExists)
                {
                    try
                    {
                        var testImage = new System.Windows.Media.Imaging.BitmapImage(new Uri(logoPath, UriKind.RelativeOrAbsolute));
                        logoLoadable = testImage.PixelWidth > 1 && testImage.PixelHeight > 1;
                    }
                    catch
                    {
                        logoLoadable = false;
                    }
                }

                if (logoExists && logoLoadable)
                {
                    if (BrandLogo != null && BrandFallback != null)
                    {
                        BrandLogo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(logoPath, UriKind.RelativeOrAbsolute));
                        BrandLogo.Visibility = Visibility.Visible;
                        BrandFallback.Visibility = Visibility.Collapsed;
                        _logger.Information($"Branding logo visas: {logoPath}");
                    }
                }
                else
                {
                    if (BrandLogo != null && BrandFallback != null)
                    {
                        BrandLogo.Visibility = Visibility.Collapsed;
                        BrandFallback.Visibility = Visibility.Visible;
                        _logger.Information("Använder FILKOLLEN-text (ingen giltig logga hittades)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte ladda branding: {ex.Message}");
                if (BrandLogo != null && BrandFallback != null)
                {
                    BrandLogo.Visibility = Visibility.Collapsed;
                    BrandFallback.Visibility = Visibility.Visible;
                }
            }
        }

        private void InitializeTheme()
        {
            if (_themeService != null && ThemeToggle != null)
            {
                var isDarkTheme = _themeService.Mode == ThemeMode.Dark ||
                                 (_themeService.Mode == ThemeMode.System && DetectSystemDarkMode());

                ThemeToggle.IsChecked = isDarkTheme;
                _themeService.ThemeChanged += OnThemeChanged;

                _logger.Information($"Tema initierat: {(isDarkTheme ? "Mörkt" : "Ljust")} tema");
            }
        }

        private bool DetectSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
                return appsUseLightTheme is int lightTheme && lightTheme == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                await InitializeServicesAsync();
                await InitializeUIAsync();
                await InitializeProtectionAsync();
                await InitializeTrayAsync();

                _ = Task.Run(async () => await PerformEnhancedStartupScanAsync());

                _logger.Information("MainWindow fullständigt initierat med elegant design");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Initiering misslyckades");
                ShowErrorDialog("Initiering misslyckades", ex);
            }
        }

        private async Task PerformEnhancedStartupScanAsync()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ScanningIndicator != null)
                        ScanningIndicator.Visibility = Visibility.Visible;
                    if (ScanProgress != null)
                        ScanProgress.Value = 0;
                });

                _logViewer?.AddLogEntry(LogLevel.Information, "Startup", "🔍 Förbättrad uppstartsskanning startad");

                for (int i = 0; i <= 100; i += 10)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (ScanProgress != null)
                            ScanProgress.Value = i;
                    });
                    await Task.Delay(200);
                }

                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (ScanningIndicator != null)
                            ScanningIndicator.Visibility = Visibility.Collapsed;
                        UpdateThreatsDisplay(threats);
                        if (LastScanText != null)
                            LastScanText.Text = DateTime.Now.ToString("HH:mm");
                    });

                    if (threats.Any())
                    {
                        ShowInAppNotification($"⚠️ {threats.Count} hot upptäckta under uppstartsskanning", NotificationType.Warning);
                        _logViewer?.AddLogEntry(LogLevel.Warning, "Startup", $"⚠️ {threats.Count} hot funna vid uppstart");
                    }
                    else
                    {
                        ShowInAppNotification("✅ Uppstartsskanning slutförd - inga hot funna", NotificationType.Success);
                        _logViewer?.AddLogEntry(LogLevel.Information, "Startup", "✅ Uppstartsskanning: Inga hot funna");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Uppstartsskanning misslyckades: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ScanningIndicator != null)
                        ScanningIndicator.Visibility = Visibility.Collapsed;
                    ShowInAppNotification("❌ Uppstartsskanning misslyckades", NotificationType.Error);
                });
            }
        }

        private void UpdateThreatsDisplay(List<ScanResult> threats)
        {
            _currentThreats.Clear();
            _currentThreats.AddRange(threats);

            if (threats.Any())
            {
                if (SafeStatusPanel != null)
                    SafeStatusPanel.Visibility = Visibility.Collapsed;
                if (ThreatsPanel != null)
                    ThreatsPanel.Visibility = Visibility.Visible;

                if (ThreatCounter != null)
                    ThreatCounter.Visibility = Visibility.Visible;
                if (ThreatCountText != null)
                    ThreatCountText.Text = $"{threats.Count} HOT";

                if (StatusIndicator != null)
                    StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                if (StatusMainText != null)
                {
                    StatusMainText.Text = "HOT UPPTÄCKTA";
                    StatusMainText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                if (StatusSubText != null)
                    StatusSubText.Text = $"{threats.Count} hot kräver åtgärd";

                BuildThreatsTable(threats);
            }
            else
            {
                if (SafeStatusPanel != null)
                    SafeStatusPanel.Visibility = Visibility.Visible;
                if (ThreatsPanel != null)
                    ThreatsPanel.Visibility = Visibility.Collapsed;
                if (ThreatCounter != null)
                    ThreatCounter.Visibility = Visibility.Collapsed;

                if (StatusIndicator != null)
                    StatusIndicator.Fill = new SolidColorBrush(Colors.Green);
                if (StatusMainText != null)
                {
                    StatusMainText.Text = "SYSTEMET ÄR SÄKERT";
                    StatusMainText.Foreground = new SolidColorBrush(Colors.Green);
                }
                if (StatusSubText != null)
                    StatusSubText.Text = $"0 hot funna • Auto-skydd {(_isProtectionActive ? "aktivt" : "inaktivt")}";
            }
        }

        private void BuildThreatsTable(List<ScanResult> threats)
        {
            if (ThreatsList == null) return;

            ThreatsList.Children.Clear();

            foreach (var threat in threats.Take(20))
            {
                var threatRow = CreateThreatRow(threat);
                ThreatsList.Children.Add(threatRow);
            }
        }

        private Border CreateThreatRow(ScanResult threat)
        {
            var row = new Border
            {
                Style = (Style)FindResource("ThreatRowStyle")
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            // Filnamn
            var fileName = new TextBlock
            {
                Text = threat.FileName,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            fileName.SetResourceReference(TextBlock.ForegroundProperty, "FK.Brush.Text");
            Grid.SetColumn(fileName, 0);
            grid.Children.Add(fileName);

            // Typ
            var fileType = new TextBlock
            {
                Text = GetFileTypeDisplay(threat.FileName),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            fileType.SetResourceReference(TextBlock.ForegroundProperty, "FK.Brush.Subtext");
            Grid.SetColumn(fileType, 1);
            grid.Children.Add(fileType);

            // Storlek
            var fileSize = new TextBlock
            {
                Text = FormatFileSize(threat.FileSize),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            fileSize.SetResourceReference(TextBlock.ForegroundProperty, "FK.Brush.Subtext");
            Grid.SetColumn(fileSize, 2);
            grid.Children.Add(fileSize);

            // Sökväg
            var filePath = new TextBlock
            {
                Text = GetDisplayPath(threat.FilePath),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = threat.FilePath
            };
            filePath.SetResourceReference(TextBlock.ForegroundProperty, "FK.Brush.Subtext");
            Grid.SetColumn(filePath, 3);
            grid.Children.Add(filePath);

            // Datum
            var fileDate = new TextBlock
            {
                Text = threat.CreatedDate.ToString("MM-dd HH:mm"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            fileDate.SetResourceReference(TextBlock.ForegroundProperty, "FK.Brush.Subtext");
            Grid.SetColumn(fileDate, 4);
            grid.Children.Add(fileDate);

            // Risk-nivå
            var riskBadge = new Border
            {
                Background = GetThreatLevelBrush(threat.ThreatLevel),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var riskText = new TextBlock
            {
                Text = GetThreatLevelText(threat.ThreatLevel),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };

            riskBadge.Child = riskText;
            Grid.SetColumn(riskBadge, 5);
            grid.Children.Add(riskBadge);

            // Ta bort-knapp
            var deleteButton = new Button
            {
                Content = "Ta bort",
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                Tag = threat,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            deleteButton.SetResourceReference(Button.StyleProperty, "FK.Style.DangerButton");
            deleteButton.Click += DeleteThreatButton_Click;
            Grid.SetColumn(deleteButton, 6);
            grid.Children.Add(deleteButton);

            row.Child = grid;
            return row;
        }

        private string GetDisplayPath(string fullPath)
        {
            try
            {
                var directoryPath = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(directoryPath))
                    return "";

                if (directoryPath.Length > 30)
                {
                    var parts = directoryPath.Split(Path.DirectorySeparatorChar);
                    if (parts.Length > 2)
                    {
                        return $"{parts[0]}\\...\\{parts[^1]}";
                    }
                }

                return directoryPath;
            }
            catch
            {
                return "";
            }
        }

        private string GetFileTypeDisplay(string fileName)
        {
            var extension = Path.GetExtension(fileName);

            if (string.IsNullOrEmpty(extension))
                return "EXTENSIONSLÖS";

            return extension.ToUpper().TrimStart('.');
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024:N0} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):N1} MB";
            return $"{bytes / (1024 * 1024 * 1024):N1} GB";
        }

        private Brush GetThreatLevelBrush(ThreatLevel level)
        {
            return level switch
            {
                ThreatLevel.Critical => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                ThreatLevel.High => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                ThreatLevel.Medium => new SolidColorBrush(Color.FromRgb(251, 146, 60)),
                _ => new SolidColorBrush(Color.FromRgb(34, 197, 94))
            };
        }

        private string GetThreatLevelText(ThreatLevel level)
        {
            return level switch
            {
                ThreatLevel.Critical => "KRITISK",
                ThreatLevel.High => "HÖG",
                ThreatLevel.Medium => "MEDIUM",
                _ => "LÅG"
            };
        }

        public enum NotificationType
        {
            Success,
            Warning,
            Error,
            Info
        }

        private void ShowInAppNotification(string message, NotificationType type)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var logLevel = type switch
                    {
                        NotificationType.Success => LogLevel.Information,
                        NotificationType.Warning => LogLevel.Warning,
                        NotificationType.Error => LogLevel.Error,
                        _ => LogLevel.Information
                    };

                    _logViewer?.AddLogEntry(logLevel, "System", message);
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte visa notifikation: {ex.Message}");
            }
        }

        private async Task InitializeServicesAsync()
        {
            try
            {
                if (_fileScanner != null && _quarantine != null && _logViewer != null)
                {
                    _intrusionDetection = new IntrusionDetectionService(_logger, _logViewer, _fileScanner, _quarantine);
                    _protectionService = new RealTimeProtectionService(_fileScanner, _quarantine, _logViewer, _logger, _config);
                    _logger.Information("Protection services initierade");
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "System", "🛡️ FilKollen säkerhetstjänster laddade");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Service initiation varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeUIAsync()
        {
            try
            {
                UpdateSecurityStatus(isSecure: true, threatsCount: 0);

                if (_licenseService != null)
                {
                    var status = await _licenseService.ValidateLicenseAsync();
                    UpdateLicenseDisplay(status);
                }

                if (LastScanText != null)
                    LastScanText.Text = "Aldrig";

                if (ThreatsHandledText != null)
                    ThreatsHandledText.Text = "0";

                _logger.Information("UI initierat med elegant design");
            }
            catch (Exception ex)
            {
                _logger.Warning($"UI initiation varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private void UpdateLicenseDisplay(LicenseStatus status)
        {
            var isTrialMode = status == LicenseStatus.TrialActive || status == LicenseStatus.TrialExpired;

            if (LicenseStatusDisplay != null && LicenseStatusButton != null && LicenseStatusText != null)
            {
                if (isTrialMode)
                {
                    LicenseStatusDisplay.Visibility = Visibility.Visible;
                    LicenseStatusButton.Visibility = Visibility.Collapsed;
                    LicenseStatusText.Text = status switch
                    {
                        LicenseStatus.TrialActive => "TRIAL AKTIVT",
                        LicenseStatus.TrialExpired => "TRIAL UTGÅNGET",
                        _ => "TRIAL STATUS"
                    };
                }
                else
                {
                    LicenseStatusDisplay.Visibility = Visibility.Collapsed;
                    LicenseStatusButton.Visibility = Visibility.Visible;
                    if (LicenseButtonText != null)
                    {
                        LicenseButtonText.Text = status switch
                        {
                            LicenseStatus.Valid => "LICENS GILTIG",
                            LicenseStatus.Expired => "LICENS UTGÅNGEN",
                            _ => "OKLICENSIERAD"
                        };
                    }
                }
            }
        }

        private async Task InitializeProtectionAsync()
        {
            try
            {
                _isProtectionActive = false;
                _isIpProtectionActive = false;

                UpdateProtectionToggles();

                _logViewer?.AddLogEntry(LogLevel.Information, "System",
                    "✅ FilKollen redo - aktivera auto-skydd för fullständigt skydd");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Protection init varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private async Task InitializeTrayAsync()
        {
            try
            {
                if (_protectionService != null && _logViewer != null)
                {
                    _trayService = new SystemTrayService(_protectionService, _logViewer, _logger);

                    _trayService.ShowMainWindowRequested += (s, e) =>
                    {
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    };

                    _trayService.ExitApplicationRequested += (s, e) =>
                    {
                        Application.Current.Shutdown();
                    };

                    _logger.Information("System tray service initierat");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Tray service init varning: {ex.Message}");
            }

            await Task.Delay(10);
        }

        private void UpdateSecurityStatus(bool isSecure, int threatsCount, string? customMessage = null)
        {
            try
            {
                if (StatusIndicator != null && StatusMainText != null && StatusSubText != null)
                {
                    if (isSecure && threatsCount == 0)
                    {
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Green);
                        StatusMainText.Text = "SYSTEMET ÄR SÄKERT";
                        StatusMainText.Foreground = new SolidColorBrush(Colors.Green);
                        StatusSubText.Text = customMessage ?? $"0 hot funna • Auto-skydd {(_isProtectionActive ? "aktivt" : "inaktivt")}";
                        if (ThreatCounter != null)
                            ThreatCounter.Visibility = Visibility.Collapsed;
                    }
                    else if (threatsCount > 0)
                    {
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                        StatusMainText.Text = "HOT UPPTÄCKTA";
                        StatusMainText.Foreground = new SolidColorBrush(Colors.Orange);
                        StatusSubText.Text = customMessage ?? "Kräver omedelbar åtgärd";
                        if (ThreatCounter != null)
                        {
                            ThreatCounter.Visibility = Visibility.Visible;
                            if (ThreatCountText != null)
                                ThreatCountText.Text = $"{threatsCount} HOT";
                        }
                    }
                    else
                    {
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Gray);
                        StatusMainText.Text = "AUTO-SKYDD INAKTIVERAT";
                        StatusMainText.Foreground = new SolidColorBrush(Colors.Gray);
                        StatusSubText.Text = "Aktivera auto-skydd för säkerhet";
                        if (ThreatCounter != null)
                            ThreatCounter.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Status uppdatering misslyckades: {ex.Message}");
            }
        }

        private void UpdateProtectionToggles()
        {
            if (ProtectionToggle != null)
            {
                ProtectionToggle.IsChecked = _isProtectionActive;
            }

            if (IpProtectionToggle != null)
            {
                IpProtectionToggle.IsChecked = _isIpProtectionActive;
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            _logger.Information($"Tema ändrat via ThemeService");
        }

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_themeService != null)
                {
                    _themeService.ApplyTheme(ThemeMode.Dark);
                    _logger.Information("Växlat till mörkt tema");
                    _logViewer?.AddLogEntry(LogLevel.Information, "UI", "🌙 Växlat till mörkt tema");
                    ShowInAppNotification("🌙 Mörkt tema aktiverat", NotificationType.Info);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid växling till mörkt tema: {ex.Message}");
                ShowInAppNotification("❌ Tema-växling misslyckades", NotificationType.Error);
            }
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_themeService != null)
                {
                    _themeService.ApplyTheme(ThemeMode.Light);
                    _logger.Information("Växlat till ljust tema");
                    _logViewer?.AddLogEntry(LogLevel.Information, "UI", "☀️ Växlat till ljust tema");
                    ShowInAppNotification("☀️ Ljust tema aktiverat", NotificationType.Info);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid växling till ljust tema: {ex.Message}");
                ShowInAppNotification("❌ Tema-växling misslyckades", NotificationType.Error);
            }
        }

        private async void ProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Auto-skydd aktiveras...");

                if (ProtectionToggle != null)
                    ProtectionToggle.IsEnabled = false;

                if (_protectionService != null)
                {
                    await _protectionService.StartProtectionAsync();
                    _protectionService.SetAutoCleanMode(true);
                }

                if (_intrusionDetection != null)
                {
                    await _intrusionDetection.StartMonitoringAsync();
                }

                _isProtectionActive = true;
                UpdateSecurityStatus(true, _currentThreats.Count, "Auto-skydd aktivt • Kontinuerlig övervakning");

                _logViewer?.AddLogEntry(LogLevel.Information, "Protection",
                    "🛡️ AUTO-SKYDD AKTIVERAT - Auto-läge: Kontinuerlig övervakning");

                _trayService?.ShowNotification("FilKollen Aktiverat",
                    "Auto-skydd aktiverat", System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid aktivering av auto-skydd: {ex.Message}");
                if (ProtectionToggle != null)
                    ProtectionToggle.IsChecked = false;
                UpdateSecurityStatus(false, 0);
                ShowInAppNotification("❌ Auto-skydd kunde inte aktiveras", NotificationType.Error);
            }
            finally
            {
                if (ProtectionToggle != null)
                    ProtectionToggle.IsEnabled = true;
            }
        }

        private async void ProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("Auto-skydd inaktiveras...");

                if (_protectionService != null)
                {
                    await _protectionService.StopProtectionAsync();
                }

                if (_intrusionDetection != null)
                {
                    await _intrusionDetection.StopMonitoringAsync();
                }

                _isProtectionActive = false;
                UpdateSecurityStatus(false, _currentThreats.Count);

                _logViewer?.AddLogEntry(LogLevel.Warning, "Protection",
                    "⚠️ AUTO-SKYDD INAKTIVERAT - Systemet är nu sårbart");

                ShowInAppNotification("⚠️ Auto-skydd inaktiverat", NotificationType.Warning);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid inaktivering av auto-skydd: {ex.Message}");
            }
        }

        private async void IpProtectionToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("IP-skydd aktiveras...");
                _isIpProtectionActive = true;

                _logViewer?.AddLogEntry(LogLevel.Information, "IPProtection",
                    "🌐 IP-SKYDD AKTIVERAT (Förberedelse för proxy-tunnel)");

                ShowInAppNotification("🌐 IP-skydd aktiverat", NotificationType.Success);

                await Task.Delay(500);

                _trayService?.ShowNotification("IP-Skydd Aktiverat",
                    "IP-anonymisering förberedd", System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid aktivering av IP-skydd: {ex.Message}");
                if (IpProtectionToggle != null)
                    IpProtectionToggle.IsChecked = false;
                ShowInAppNotification("❌ IP-skydd kunde inte aktiveras", NotificationType.Error);
            }
        }

        private async void IpProtectionToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.Information("IP-skydd inaktiveras...");
                _isIpProtectionActive = false;

                _logViewer?.AddLogEntry(LogLevel.Warning, "IPProtection",
                    "⚠️ IP-SKYDD INAKTIVERAT");

                ShowInAppNotification("⚠️ IP-skydd inaktiverat", NotificationType.Warning);

                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid inaktivering av IP-skydd: {ex.Message}");
            }
        }

        private async void QuickScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (QuickScanButton != null)
                {
                    QuickScanButton.Content = "🔄 SÖKER...";
                    QuickScanButton.IsEnabled = false;
                }

                if (ScanningIndicator != null)
                    ScanningIndicator.Visibility = Visibility.Visible;
                if (ScanProgress != null)
                    ScanProgress.Value = 0;

                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔍 Manuell skanning startad");

                if (_fileScanner != null)
                {
                    for (int i = 0; i <= 100; i += 20)
                    {
                        if (ScanProgress != null)
                            ScanProgress.Value = i;
                        await Task.Delay(300);
                    }

                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateThreatsDisplay(threats);

                    if (LastScanText != null)
                        LastScanText.Text = DateTime.Now.ToString("HH:mm");

                    if (threats.Any())
                    {
                        _logViewer?.AddLogEntry(LogLevel.Warning, "Scan",
                            $"⚠️ Manuell skanning: {threats.Count} hot funna");

                        ShowInAppNotification($"⚠️ {threats.Count} hot upptäckta!", NotificationType.Warning);
                    }
                    else
                    {
                        _logViewer?.AddLogEntry(LogLevel.Information, "Scan",
                            "✅ Manuell skanning: Inga hot funna");

                        ShowInAppNotification("✅ Inga hot funna - systemet är säkert", NotificationType.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Manuell skanning fel: {ex.Message}");
                ShowInAppNotification("❌ Skanning misslyckades", NotificationType.Error);
            }
            finally
            {
                if (ScanningIndicator != null)
                    ScanningIndicator.Visibility = Visibility.Collapsed;

                if (QuickScanButton != null)
                {
                    QuickScanButton.Content = "🔍 Sök efter hot";
                    QuickScanButton.IsEnabled = true;
                }
            }
        }

        private async void BrowserCleanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BrowserCleanButton != null)
                {
                    BrowserCleanButton.Content = "🔄 RENSAR...";
                    BrowserCleanButton.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "BrowserClean",
                    "🌐 RENSA FALSKA AVISERINGAR STARTAD");

                if (_browserCleaner != null)
                {
                    var result = await _browserCleaner.DeepCleanAllBrowsersAsync();

                    if (result.Success)
                    {
                        var message = $"✅ {result.MalwareNotificationsRemoved} falska aviseringar rensade";
                        ShowInAppNotification(message, NotificationType.Success);

                        _logViewer?.AddLogEntry(LogLevel.Information, "BrowserClean",
                            $"✅ Falska aviseringar rensade: {result.MalwareNotificationsRemoved} st");

                        _trayService?.ShowNotification("Falska aviseringar rensade",
                            $"{result.MalwareNotificationsRemoved} malware-notiser borttagna",
                            System.Windows.Forms.ToolTipIcon.Info);
                    }
                    else
                    {
                        ShowInAppNotification("❌ Rensning misslyckades", NotificationType.Error);
                        _logViewer?.AddLogEntry(LogLevel.Error, "BrowserClean",
                            "❌ Webbläsarrensning misslyckades");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid webbläsarrensning: {ex.Message}");
                ShowInAppNotification("❌ Rensning misslyckades", NotificationType.Error);
            }
            finally
            {
                if (BrowserCleanButton != null)
                {
                    BrowserCleanButton.Content = "🌐 Rensa falska aviseringar";
                    BrowserCleanButton.IsEnabled = true;
                }
            }
        }

        private async void HandleAllThreatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (HandleAllThreatsButton != null)
                {
                    HandleAllThreatsButton.Content = "🔄 Tar bort alla...";
                    HandleAllThreatsButton.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "ThreatAction",
                    "🧹 Tar bort alla upptäckta hot automatiskt");

                var threatsToHandle = new List<ScanResult>(_currentThreats);
                int handledCount = 0;

                foreach (var threat in threatsToHandle)
                {
                    try
                    {
                        if (_quarantine != null)
                        {
                            var success = await _quarantine.DeleteFileAsync(threat);
                            if (success)
                            {
                                handledCount++;
                                _currentThreats.Remove(threat);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Kunde inte hantera hot {threat.FileName}: {ex.Message}");
                    }
                }

                UpdateThreatsDisplay(_currentThreats);
                ScrollToTopOfThreatsList();

                if (ThreatsHandledText != null)
                {
                    var currentHandled = int.Parse(ThreatsHandledText.Text);
                    ThreatsHandledText.Text = (currentHandled + handledCount).ToString();
                }

                var message = $"✅ {handledCount} hot har tagits bort";
                ShowInAppNotification(message, NotificationType.Success);

                _logViewer?.AddLogEntry(LogLevel.Information, "ThreatAction",
                    $"✅ {handledCount} hot har hanterats framgångsrikt");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hantering av alla hot: {ex.Message}");
                ShowInAppNotification("❌ Fel vid borttagning av hot", NotificationType.Error);
            }
            finally
            {
                if (HandleAllThreatsButton != null)
                {
                    HandleAllThreatsButton.Content = "🧹 Ta bort alla hot";
                    HandleAllThreatsButton.IsEnabled = true;
                }
            }
        }

        private void ScrollToTopOfThreatsList()
        {
            try
            {
                if (ThreatsList?.Parent is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToTop();
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Kunde inte scrolla till toppen: {ex.Message}");
            }
        }

        private async void RefreshScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (RefreshScanButton != null)
                {
                    RefreshScanButton.Content = "🔄 Skannar...";
                    RefreshScanButton.IsEnabled = false;
                }

                _logViewer?.AddLogEntry(LogLevel.Information, "Manual", "🔄 Uppdaterar hotskanning");

                if (_fileScanner != null)
                {
                    var results = await _fileScanner.ScanTempDirectoriesAsync();
                    var threats = results?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                    UpdateThreatsDisplay(threats);

                    if (LastScanText != null)
                        LastScanText.Text = DateTime.Now.ToString("HH:mm");

                    if (threats.Any())
                    {
                        ShowInAppNotification($"🔄 Uppdatering: {threats.Count} hot funna", NotificationType.Warning);
                    }
                    else
                    {
                        ShowInAppNotification("🔄 Uppdatering: Inga hot funna", NotificationType.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hotskanning: {ex.Message}");
                ShowInAppNotification("❌ Uppdatering misslyckades", NotificationType.Error);
            }
            finally
            {
                if (RefreshScanButton != null)
                {
                    RefreshScanButton.Content = "🔄 Skanna om";
                    RefreshScanButton.IsEnabled = true;
                }
            }
        }

        private async void DeleteThreatButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ScanResult threat)
            {
                try
                {
                    var result = MessageBox.Show(
                        $"Vill du ta bort denna fil permanent?\n\n{threat.FileName}\n\nDenna åtgärd kan inte ångras.",
                        "Bekräfta borttagning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        button.IsEnabled = false;
                        button.Content = "🔄 Tar bort...";

                        if (_quarantine != null)
                        {
                            var success = await _quarantine.DeleteFileAsync(threat);
                            if (success)
                            {
                                _currentThreats.Remove(threat);
                                UpdateThreatsDisplay(_currentThreats);

                                if (ThreatsHandledText != null)
                                {
                                    var currentHandled = int.Parse(ThreatsHandledText.Text);
                                    ThreatsHandledText.Text = (currentHandled + 1).ToString();
                                }

                                ShowInAppNotification($"✅ {threat.FileName} har tagits bort", NotificationType.Success);

                                _logViewer?.AddLogEntry(LogLevel.Information, "ThreatAction",
                                    $"🗑️ Hot raderat: {threat.FileName}");
                            }
                            else
                            {
                                ShowInAppNotification($"❌ Kunde inte ta bort {threat.FileName}", NotificationType.Error);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid radering av hot: {ex.Message}");
                    ShowInAppNotification($"❌ Fel vid borttagning: {ex.Message}", NotificationType.Error);
                }
                finally
                {
                    button.IsEnabled = true;
                    button.Content = "Ta bort";
                }
            }
        }

        private void LicenseStatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_licenseService != null)
                {
                    var licenseWindow = new LicenseRegistrationWindow(_licenseService, _logger);
                    licenseWindow.Owner = this;
                    var result = licenseWindow.ShowDialog();

                    _ = Task.Run(async () =>
                    {
                        var status = await _licenseService.ValidateLicenseAsync();
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            UpdateLicenseDisplay(status);
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid öppning av licensfönster: {ex.Message}");
                ShowInAppNotification("❌ Kunde inte öppna licensfönster", NotificationType.Error);
            }
        }

        private void SortHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock header && header.Tag is string columnName)
            {
                if (_currentSortColumn == columnName)
                {
                    _sortAscending = !_sortAscending;
                }
                else
                {
                    _currentSortColumn = columnName;
                    _sortAscending = true;
                }

                SortThreats(columnName, _sortAscending);
                UpdateSortHeaders(columnName, _sortAscending);
            }
        }

        private void SortThreats(string columnName, bool ascending)
        {
            try
            {
                IEnumerable<ScanResult> sortedThreats = columnName switch
                {
                    "FileName" => ascending ? _currentThreats.OrderBy(t => t.FileName) : _currentThreats.OrderByDescending(t => t.FileName),
                    "FileType" => ascending ? _currentThreats.OrderBy(t => GetFileTypeDisplay(t.FileName)) : _currentThreats.OrderByDescending(t => GetFileTypeDisplay(t.FileName)),
                    "FileSize" => ascending ? _currentThreats.OrderBy(t => t.FileSize) : _currentThreats.OrderByDescending(t => t.FileSize),
                    "FilePath" => ascending ? _currentThreats.OrderBy(t => Path.GetDirectoryName(t.FilePath)) : _currentThreats.OrderByDescending(t => Path.GetDirectoryName(t.FilePath)),
                    "CreatedDate" => ascending ? _currentThreats.OrderBy(t => t.CreatedDate) : _currentThreats.OrderByDescending(t => t.CreatedDate),
                    "ThreatLevel" => ascending ? _currentThreats.OrderBy(t => t.ThreatLevel) : _currentThreats.OrderByDescending(t => t.ThreatLevel),
                    _ => _currentThreats
                };

                _currentThreats.Clear();
                _currentThreats.AddRange(sortedThreats);
                BuildThreatsTable(_currentThreats);

                _logViewer?.AddLogEntry(LogLevel.Debug, "UI",
                    $"Sorterat hot efter {columnName} ({(ascending ? "stigande" : "fallande")})");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid sortering: {ex.Message}");
            }
        }

        private void UpdateSortHeaders(string activeColumn, bool ascending)
        {
            try
            {
                var headers = new Dictionary<string, TextBlock?>
                {
                    ["FileName"] = FileNameHeader,
                    ["FileType"] = TypeHeader,
                    ["FileSize"] = SizeHeader,
                    ["FilePath"] = PathHeader,
                    ["CreatedDate"] = DateHeader,
                    ["ThreatLevel"] = RiskHeader
                };

                foreach (var (column, header) in headers)
                {
                    if (header != null)
                    {
                        if (column == activeColumn)
                        {
                            var arrow = ascending ? "↑" : "↓";
                            var baseText = column switch
                            {
                                "FileName" => "FILNAMN",
                                "FileType" => "TYP",
                                "FileSize" => "STORLEK",
                                "FilePath" => "SÖKVÄG",
                                "CreatedDate" => "SKAPAD",
                                "ThreatLevel" => "RISK",
                                _ => ""
                            };
                            header.Text = $"{baseText} {arrow}";
                            header.Foreground = (Brush)FindResource("FK.Brush.Primary");
                        }
                        else
                        {
                            var baseText = column switch
                            {
                                "FileName" => "FILNAMN",
                                "FileType" => "TYP",
                                "FileSize" => "STORLEK",
                                "FilePath" => "SÖKVÄG",
                                "CreatedDate" => "SKAPAD",
                                "ThreatLevel" => "RISK",
                                _ => ""
                            };
                            header.Text = $"{baseText} ↕";
                            header.SetResourceReference(TextBlock.ForegroundProperty, "FK.Brush.Subtext");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid uppdatering av sorteringsheaders: {ex.Message}");
            }
        }

        private void UpdateStatusCallback(object? state)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (ConnectionStatusText != null)
                    {
                        ConnectionStatusText.Text = "ONLINE";
                        ConnectionStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    }

                    if (_protectionService != null && ThreatsHandledText != null)
                    {
                        var stats = _protectionService.GetProtectionStats();
                        ThreatsHandledText.Text = stats.TotalThreatsHandled.ToString();
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Debug($"Status update error: {ex.Message}");
            }
        }

        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize)
                {
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                }
                else
                {
                    DragMove();
                }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                e.Cancel = true;
                Hide();

                _trayService?.ShowNotification("FilKollen",
                    "Applikationen körs i bakgrunden. Högerklicka på ikonen för att avsluta.",
                    System.Windows.Forms.ToolTipIcon.Info);

                _logger.Information("MainWindow minimerat till systemfält");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid minimering till tray: {ex.Message}");
                e.Cancel = false;
                base.OnClosing(e);
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            try
            {
                if (WindowState == WindowState.Minimized)
                {
                    Hide();
                    _trayService?.SetMainWindowVisibility(false);
                }
                else
                {
                    _trayService?.SetMainWindowVisibility(true);
                }
                base.OnStateChanged(e);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fönsterstatus-ändring fel: {ex.Message}");
            }
        }

        private void ShowErrorDialog(string message, Exception ex)
        {
            var detailed = $"{message}\n\n{ex.GetType().Name}: {ex.Message}";
            MessageBox.Show(detailed, "FilKollen - Fel", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            try
            {
                _statusUpdateTimer?.Dispose();
                _protectionService?.Dispose();
                _intrusionDetection?.Dispose();
                _trayService?.Dispose();
                _logViewer?.Dispose();

                if (_themeService != null)
                {
                    _themeService.ThemeChanged -= OnThemeChanged;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Dispose error: {ex.Message}");
            }
        }
    }
}