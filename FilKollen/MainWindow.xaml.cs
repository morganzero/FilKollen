// MainWindow.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FilKollen.Models;
using FilKollen.Services;
using FilKollen.Views;
using Serilog;

namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly FileScanner _fileScanner;
        private readonly QuarantineManager _quarantineManager;
        private readonly ScheduleManager _scheduleManager;
        private readonly ILogger _logger;
        private AppConfig _config;

        public ObservableCollection<ScanResultViewModel> ScanResults { get; set; }
        public bool IsScanning { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            
            // Initiera logging
            _logger = new LoggerConfiguration()
                .WriteTo.File("logs/filkollen-.log", 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            // Ladda konfiguration
            _config = LoadConfiguration();
            
            // Initiera services
            _fileScanner = new FileScanner(_config, _logger);
            _quarantineManager = new QuarantineManager(_logger);
            _scheduleManager = new ScheduleManager(_logger);

            // Initiera UI
            ScanResults = new ObservableCollection<ScanResultViewModel>();
            ResultsDataGrid.ItemsSource = ScanResults;
            
            DataContext = this;
            
            // Uppdatera UI med nuvarande schema-status
            UpdateScheduleStatus();
            
            _logger.Information("FilKollen startad");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private AppConfig LoadConfiguration()
        {
            // För nu, använd default config - senare kan vi läsa från fil
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

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            await StartScanAsync();
        }

        private async Task StartScanAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            ScanButton.IsEnabled = false;
            ScanProgressBar.Visibility = Visibility.Visible;
            StatusBarText.Text = "Skannar filer...";

            try
            {
                ScanResults.Clear();
                var results = await _fileScanner.ScanAsync();
                
                foreach (var result in results)
                {
                    ScanResults.Add(new ScanResultViewModel(result));
                }

                ResultCountText.Text = $"{results.Count} suspekta filer funna";
                StatusBarText.Text = $"Skanning klar - {results.Count} hot identifierade";
                
                if (results.Any() && _config.ShowNotifications)
                {
                    ShowTrayNotification($"FilKollen hittade {results.Count} suspekta filer!");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid skanning: {ex.Message}");
                MessageBox.Show($"Ett fel uppstod vid skanningen: {ex.Message}", 
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusBarText.Text = "Skanning misslyckades";
            }
            finally
            {
                IsScanning = false;
                ScanButton.IsEnabled = true;
                ScanProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void QuarantineSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ScanResults.Where(r => r.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("Välj minst en fil att sätta i karantän.", 
                    "Ingen fil vald", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Sätt {selectedItems.Count} fil(er) i karantän?", 
                "Bekräfta karantän", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                await ProcessSelectedFiles(selectedItems, ProcessAction.Quarantine);
            }
        }

        private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ScanResults.Where(r => r.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("Välj minst en fil att radera.", 
                    "Ingen fil vald", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"VARNING: Radera {selectedItems.Count} fil(er) permanent?\n\nDenna åtgärd kan inte ångras!", 
                "Bekräfta radering", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                await ProcessSelectedFiles(selectedItems, ProcessAction.Delete);
            }
        }

        private async void QuarantineAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ScanResults.Any())
            {
                MessageBox.Show("Inga filer att sätta i karantän.", 
                    "Inga resultat", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Sätt alla {ScanResults.Count} fil(er) i karantän?", 
                "Bekräfta karantän", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                await ProcessSelectedFiles(ScanResults.ToList(), ProcessAction.Quarantine);
            }
        }

        private async void DeleteAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ScanResults.Any())
            {
                MessageBox.Show("Inga filer att radera.", 
                    "Inga resultat", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"VARNING: Radera alla {ScanResults.Count} fil(er) permanent?\n\nDenna åtgärd kan inte ångras!", 
                "Bekräfta radering", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                await ProcessSelectedFiles(ScanResults.ToList(), ProcessAction.Delete);
            }
        }

        private async Task ProcessSelectedFiles(System.Collections.Generic.List<ScanResultViewModel> items, ProcessAction action)
        {
            var processed = 0;
            var failed = 0;

            foreach (var item in items)
            {
                try
                {
                    bool success = action switch
                    {
                        ProcessAction.Quarantine => await _quarantineManager.QuarantineFileAsync(item.ScanResult),
                        ProcessAction.Delete => await _quarantineManager.DeleteFileAsync(item.ScanResult),
                        _ => false
                    };

                    if (success)
                    {
                        processed++;
                        ScanResults.Remove(item);
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid bearbetning av {item.FileName}: {ex.Message}");
                    failed++;
                }
            }

            ResultCountText.Text = $"{ScanResults.Count} suspekta filer funna";
            
            var actionText = action == ProcessAction.Quarantine ? "karantänerade" : "raderade";
            StatusBarText.Text = $"{processed} filer {actionText}" + (failed > 0 ? $", {failed} misslyckades" : "");
            
            if (failed > 0)
            {
                MessageBox.Show($"Bearbetning klar:\n{processed} filer {actionText}\n{failed} misslyckades", 
                    "Resultat", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (AutoModeRadio?.IsChecked == true)
            {
                UpdateScheduleStatus();
            }
        }

        private async void SaveScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var frequency = FrequencyCombo.SelectedIndex switch
                {
                    0 => ScheduleFrequency.Daily,
                    1 => ScheduleFrequency.Weekly,
                    2 => ScheduleFrequency.Monthly,
                    _ => ScheduleFrequency.Daily
                };

                _config.Frequency = frequency;
                _config.ScheduledTime = ScheduleTimePicker.SelectedTime ?? TimeSpan.FromHours(2);
                _config.EnableScheduling = true;

                var success = await _scheduleManager.CreateScheduledTaskAsync(_config);
                
                if (success)
                {
                    MessageBox.Show("Schema sparat och aktiverat!", 
                        "Schema", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateScheduleStatus();
                }
                else
                {
                    MessageBox.Show("Kunde inte skapa schema. Kontrollera att programmet körs som administratör.", 
                        "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid skapande av schema: {ex.Message}");
                MessageBox.Show($"Fel vid skapande av schema: {ex.Message}", 
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateScheduleStatus()
        {
            if (_scheduleManager.IsTaskScheduled())
            {
                var nextRun = _scheduleManager.GetNextRunTime();
                NextScanText.Text = nextRun.HasValue ? 
                    $"Nästa skanning: {nextRun.Value:yyyy-MM-dd HH:mm}" : 
                    "Schema aktivt";
                ProtectionStatusText.Text = "Schema aktivt";
            }
            else
            {
                NextScanText.Text = "Ingen schemalagd skanning";
                ProtectionStatusText.Text = "Manuellt läge";
            }
        }

        private void ManageQuarantineButton_Click(object sender, RoutedEventArgs e)
        {
            var quarantineWindow = new QuarantineWindow(_quarantineManager, _logger);
            quarantineWindow.Owner = this;
            quarantineWindow.ShowDialog();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_config);
            settingsWindow.Owner = this;
            if (settingsWindow.ShowDialog() == true)
            {
                // Konfiguration uppdaterad - återskapa scanner med nya inställningar
                // _fileScanner = new FileScanner(_config, _logger); // Implementera senare
            }
        }

        private void ShowTrayNotification(string message)
        {
            // Implementera system tray notifikation
            // Kan använda NotifyIcon från Windows Forms eller egen implementation
        }

        private enum ProcessAction
        {
            Quarantine,
            Delete
        }
    }
}

// ViewModels/ScanResultViewModel.cs
using System.ComponentModel;
using FilKollen.Models;

namespace FilKollen
{
    public class ScanResultViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public ScanResult ScanResult { get; }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
        
        public string FileName => ScanResult.FileName;
        public string FilePath => ScanResult.FilePath;
        public string FormattedSize => ScanResult.FormattedSize;
        public DateTime LastModified => ScanResult.LastModified;
        public ThreatLevel ThreatLevel => ScanResult.ThreatLevel;
        public string Reason => ScanResult.Reason;

        public ScanResultViewModel(ScanResult scanResult)
        {
            ScanResult = scanResult;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
