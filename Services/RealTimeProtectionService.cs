using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using FilKollen.Models;
using FilKollen.Services;
using Serilog;

namespace FilKollen.Services
{
    public class RealTimeProtectionService : IDisposable
    {
        private readonly FileScanner _fileScanner;
        private readonly QuarantineManager _quarantineManager;
        private readonly LogViewerService _logViewer;
        private readonly ILogger _logger;
        private readonly AppConfig _config;
        
        private System.Timers.Timer _scanTimer = null!;
        private readonly List<FileSystemWatcher> _fileWatchers;
        private readonly HashSet<string> _recentlyProcessed;
        private CancellationTokenSource _cancellationTokenSource = null!;
        
        public bool IsProtectionActive { get; private set; }
        public bool AutoCleanMode { get; set; } = false;
        public DateTime LastScanTime { get; private set; }
        public int TotalThreatsFound { get; private set; }
        public int TotalThreatsHandled { get; private set; }

        public event EventHandler<ThreatDetectedEventArgs>? ThreatDetected;
        public event EventHandler<ProtectionStatusChangedEventArgs>? ProtectionStatusChanged;

        public RealTimeProtectionService(FileScanner fileScanner, QuarantineManager quarantineManager, 
            LogViewerService logViewer, ILogger logger, AppConfig config)
        {
            _fileScanner = fileScanner;
            _quarantineManager = quarantineManager;
            _logViewer = logViewer;
            _logger = logger;
            _config = config;
            _fileWatchers = new List<FileSystemWatcher>();
            _recentlyProcessed = new HashSet<string>();
        }

        public async Task StartProtectionAsync()
        {
            if (IsProtectionActive) return;

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Starta periodisk skanning (var 10:e minut)
                StartPeriodicScanning();
                
                // Starta realtids file system monitoring
                await StartFileSystemWatchingAsync();
                
                IsProtectionActive = true;
                
                _logger.Information("Real-time protection aktiverat");
                _logViewer.AddLogEntry(LogLevel.Information, "Protection", 
                    "🛡️ Real-time säkerhetsskydd AKTIVERAT - kontinuerlig övervakning startad");
                
                ProtectionStatusChanged?.Invoke(this, new ProtectionStatusChangedEventArgs(true));
                
                // Kör första skanning direkt
                _ = Task.Run(async () => await PerformBackgroundScanAsync());
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid start av real-time protection: {ex.Message}");
                throw;
            }
        }

        public async Task StopProtectionAsync()
        {
            if (!IsProtectionActive) return;

            try
            {
                _cancellationTokenSource?.Cancel();
                
                // Stoppa timer
                _scanTimer?.Stop();
                _scanTimer?.Dispose();
                
                // Stoppa file watchers
                foreach (var watcher in _fileWatchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _fileWatchers.Clear();
                
                IsProtectionActive = false;
                
                _logger.Information("Real-time protection inaktiverat");
                _logViewer.AddLogEntry(LogLevel.Warning, "Protection", 
                    "⚠️ Real-time säkerhetsskydd INAKTIVERAT - systemet är nu oskyddat");
                
                ProtectionStatusChanged?.Invoke(this, new ProtectionStatusChangedEventArgs(false));
                
                await Task.Delay(100); // Yield
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid stopp av real-time protection: {ex.Message}");
            }
        }

        private void StartPeriodicScanning()
        {
            // Skanna var 10:e minut
            _scanTimer = new System.Timers.Timer(TimeSpan.FromMinutes(10).TotalMilliseconds);
            _scanTimer.Elapsed += async (sender, e) => await PerformBackgroundScanAsync();
            _scanTimer.AutoReset = true;
            _scanTimer.Start();
            
            _logViewer.AddLogEntry(LogLevel.Information, "Protection", 
                "⏰ Periodisk säkerhetsskanning aktiverad (10-minuters intervall)");
        }

        private async Task StartFileSystemWatchingAsync()
        {
            await Task.Yield();
            
            foreach (var path in _config.ScanPaths)
            {
                try
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(path);
                    if (!Directory.Exists(expandedPath))
                    {
                        _logger.Warning($"Sökväg finns inte för övervakning: {expandedPath}");
                        continue;
                    }

                    var watcher = new FileSystemWatcher(expandedPath)
                    {
                        NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnFileCreated;
                    watcher.Renamed += OnFileRenamed;
                    watcher.Changed += OnFileChanged;
                    
                    _fileWatchers.Add(watcher);
                    
                    _logViewer.AddLogEntry(LogLevel.Information, "Protection", 
                        $"👁️ Real-time övervakning aktiverad för: {expandedPath}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Kunde inte övervaka {path}: {ex.Message}");
                    _logViewer.AddLogEntry(LogLevel.Warning, "Protection", 
                        $"⚠️ Kunde inte övervaka: {path} - {ex.Message}");
                }
            }
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            await ProcessNewFile(e.FullPath, "skapad");
        }

        private async void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            await ProcessNewFile(e.FullPath, "omdöpt");
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Endast för nya filer eller betydande ändringar
            if (File.Exists(e.FullPath))
            {
                var fileInfo = new FileInfo(e.FullPath);
                if (DateTime.Now - fileInfo.CreationTime < TimeSpan.FromMinutes(5))
                {
                    await ProcessNewFile(e.FullPath, "ändrad");
                }
            }
        }

        private async Task ProcessNewFile(string filePath, string action)
        {
            try
            {
                // Undvik duplicerad bearbetning
                var key = $"{filePath}_{action}";
                if (_recentlyProcessed.Contains(key)) return;
                
                _recentlyProcessed.Add(key);
                
                // Ta bort från cache efter 30 sekunder
                _ = Task.Delay(30000).ContinueWith(t => _recentlyProcessed.Remove(key));
                
                // Vänta lite för att filen ska skrivas klart
                await Task.Delay(1000);
                
                if (!File.Exists(filePath)) return;
                
                _logViewer.AddLogEntry(LogLevel.Debug, "RealTime", 
                    $"🔍 Real-time analys: {Path.GetFileName(filePath)} ({action})");
                
                // Analysera filen direkt
                var scanResult = await _fileScanner.ScanSingleFileAsync(filePath);
                if (scanResult != null)
                {
                    await HandleThreatDetected(scanResult, isRealTime: true);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid real-time analys av {filePath}: {ex.Message}");
            }
        }

        private async Task PerformBackgroundScanAsync()
        {
            if (_cancellationTokenSource?.Token.IsCancellationRequested == true) return;

            try
            {
                LastScanTime = DateTime.Now;
                
                _logViewer.AddLogEntry(LogLevel.Information, "Protection", 
                    "🔍 Startar bakgrundssäkerhetsskanning...");
                
                var results = await _fileScanner.ScanAsync();
                
                if (results.Any())
                {
                    _logViewer.AddLogEntry(LogLevel.Warning, "Protection", 
                        $"⚠️ Bakgrundsskanning: {results.Count} hot identifierade");
                    
                    foreach (var result in results)
                    {
                        await HandleThreatDetected(result, isRealTime: false);
                    }
                }
                else
                {
                    _logViewer.AddLogEntry(LogLevel.Information, "Protection", 
                        "✅ Bakgrundsskanning: Inga hot funna - systemet säkert");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid bakgrundsskanning: {ex.Message}");
                _logViewer.AddLogEntry(LogLevel.Error, "Protection", 
                    $"❌ Fel vid bakgrundsskanning: {ex.Message}");
            }
        }

        private async Task HandleThreatDetected(ScanResult threat, bool isRealTime)
        {
            TotalThreatsFound++;
            
            var scanType = isRealTime ? "REAL-TIME" : "BAKGRUND";
            _logViewer.AddLogEntry(LogLevel.Warning, "Security", 
                $"🚨 HOT IDENTIFIERAT ({scanType}): {Path.GetFileName(threat.FilePath)} ({threat.ThreatLevel})");
            
            // Notifiera användaren
            ThreatDetected?.Invoke(this, new ThreatDetectedEventArgs(threat, AutoCleanMode));
            
            // Hantera automatiskt om auto-läge är aktivt
            if (AutoCleanMode)
            {
                await HandleThreatAutomatically(threat, isRealTime);
            }
            else if (isRealTime && threat.ThreatLevel >= ThreatLevel.High)
            {
                // För kritiska real-time hot, föreslå omedelbar åtgärd
                _logViewer.AddLogEntry(LogLevel.Error, "Critical", 
                    $"🚨 KRITISKT HOT KRÄVER OMEDELBAR ÅTGÄRD: {Path.GetFileName(threat.FilePath)}");
            }
        }

        private async Task HandleThreatAutomatically(ScanResult threat, bool isRealTime)
        {
            try
            {
                bool success = false;
                string action = "";
                
                // Hantera baserat på hotnivå och typ
                if (threat.ThreatLevel >= ThreatLevel.Critical || 
                    (isRealTime && threat.ThreatLevel >= ThreatLevel.High))
                {
                    // Kritiska hot: Radera direkt
                    success = await _quarantineManager.DeleteFileAsync(threat);
                    action = "RADERAT";
                }
                else
                {
                    // Lägre hot: Karantän
                    success = await _quarantineManager.QuarantineFileAsync(threat);
                    action = "KARANTÄNERAT";
                }
                
                if (success)
                {
                    TotalThreatsHandled++;
                    var source = isRealTime ? "RealTimeAuto" : "BackgroundAuto";
                    _logViewer.AddLogEntry(LogLevel.Information, source, 
                        $"🤖 AUTO-RENSNING: {Path.GetFileName(threat.FilePath)} {action} ({threat.ThreatLevel})");
                }
                else
                {
                    _logViewer.AddLogEntry(LogLevel.Error, "AutoClean", 
                        $"❌ AUTO-RENSNING MISSLYCKADES: {Path.GetFileName(threat.FilePath)}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid automatisk hothantering: {ex.Message}");
                _logViewer.AddLogEntry(LogLevel.Error, "AutoClean", 
                    $"❌ Fel vid auto-rensning av {Path.GetFileName(threat.FilePath)}: {ex.Message}");
            }
        }

        public ProtectionStats GetProtectionStats()
        {
            return new ProtectionStats
            {
                IsActive = IsProtectionActive,
                AutoCleanMode = AutoCleanMode,
                LastScanTime = LastScanTime,
                TotalThreatsFound = TotalThreatsFound,
                TotalThreatsHandled = TotalThreatsHandled,
                MonitoredPaths = _fileWatchers.Count
            };
        }

        // Manuell metod för att uppdatera auto-clean mode
        public void SetAutoCleanMode(bool autoMode)
        {
            AutoCleanMode = autoMode;
            _logViewer.AddLogEntry(LogLevel.Information, "Settings", 
                $"🔧 Automatisk rensning {(autoMode ? "AKTIVERAD" : "INAKTIVERAD")}");
        }

        // Metod för att få detaljer om aktuell övervakning
        public List<string> GetMonitoredPaths()
        {
            return _fileWatchers
                .Where(w => w.EnableRaisingEvents)
                .Select(w => w.Path)
                .ToList();
        }

        // Metod för att manuellt trigga en skanning
        public async Task TriggerManualScanAsync()
        {
            if (!IsProtectionActive)
            {
                _logViewer.AddLogEntry(LogLevel.Warning, "Manual", 
                    "⚠️ Manuell skanning: Real-time skydd är inaktiverat");
            }
            
            await PerformBackgroundScanAsync();
        }

        public void Dispose()
        {
            try
            {
                StopProtectionAsync().Wait(5000);
                _cancellationTokenSource?.Dispose();
                
                foreach (var watcher in _fileWatchers)
                {
                    watcher?.Dispose();
                }
                _fileWatchers.Clear();
                
                _scanTimer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid dispose av RealTimeProtectionService: {ex.Message}");
            }
        }
    }

    public class ThreatDetectedEventArgs : EventArgs
    {
        public ScanResult Threat { get; }
        public bool WasHandledAutomatically { get; }
        public DateTime DetectionTime { get; }
        
        public ThreatDetectedEventArgs(ScanResult threat, bool wasHandledAutomatically)
        {
            Threat = threat;
            WasHandledAutomatically = wasHandledAutomatically;
            DetectionTime = DateTime.Now;
        }
    }

    public class ProtectionStatusChangedEventArgs : EventArgs
    {
        public bool IsActive { get; }
        public DateTime StatusChangeTime { get; }
        
        public ProtectionStatusChangedEventArgs(bool isActive)
        {
            IsActive = isActive;
            StatusChangeTime = DateTime.Now;
        }
    }

    public class ProtectionStats
    {
        public bool IsActive { get; set; }
        public bool AutoCleanMode { get; set; }
        public DateTime LastScanTime { get; set; }
        public int TotalThreatsFound { get; set; }
        public int TotalThreatsHandled { get; set; }
        public int MonitoredPaths { get; set; }
        
        public double ThreatHandlingRate => TotalThreatsFound > 0 ? 
            (double)TotalThreatsHandled / TotalThreatsFound * 100 : 0;
            
        public TimeSpan TimeSinceLastScan => DateTime.Now - LastScanTime;
    }
}