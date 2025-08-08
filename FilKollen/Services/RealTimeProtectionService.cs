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
    public class RealTimeProtectionService
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
                StartFileSystemWatching();
                
                IsProtectionActive = true;
                
                _logger.Information("Real-time protection aktiverat");
                _logViewer.AddLogEntry(LogLevel.Information, "Protection", 
                    "🛡️ Real-time säkerhetsskydd AKTIVERAT - kontinuerlig övervakning startad");
                
                ProtectionStatusChanged?.Invoke(this, new ProtectionStatusChangedEventArgs(true));
                
                // Kör första skanning direkt
                await PerformBackgroundScanAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid start av real-time protection: {ex.Message}");
                throw;
            }
        }

        public async Task StopProtectionAsync()
        {
            await System.Threading.Tasks.Task.Yield();

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

        private void StartFileSystemWatching()
        {
            foreach (var path in _config.ScanPaths)
            {
                try
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(path);
                    if (!Directory.Exists(expandedPath)) continue;

                    var watcher = new FileSystemWatcher(expandedPath)
                    {
                        NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnFileCreated;
                    watcher.Renamed += OnFileRenamed;
                    
                    _fileWatchers.Add(watcher);
                    
                    _logViewer.AddLogEntry(LogLevel.Information, "Protection", 
                        $"👁️ Real-time övervakning aktiverad för: {expandedPath}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Kunde inte övervaka {path}: {ex.Message}");
                }
            }
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            await ProcessNewFile(e.FullPath);
        }

        private async void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            await ProcessNewFile(e.FullPath);
        }

        private async Task ProcessNewFile(string filePath)
        {
            try
            {
                // Undvik duplicerad bearbetning
                if (_recentlyProcessed.Contains(filePath)) return;
                _recentlyProcessed.Add(filePath);
                
                // Ta bort från cache efter 30 sekunder
                _ = Task.Delay(30000).ContinueWith(t => _recentlyProcessed.Remove(filePath));
                
                // Vänta lite för att filen ska skrivas klart
                await Task.Delay(1000);
                
                if (!File.Exists(filePath)) return;
                
                _logViewer.AddLogEntry(LogLevel.Debug, "Protection", 
                    $"🔍 Real-time analys: {Path.GetFileName(filePath)}");
                
                // Analysera filen direkt
                var scanResult = await AnalyzeFileRealTime(filePath);
                if (scanResult != null)
                {
                    await HandleThreatDetected(scanResult);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid real-time analys av {filePath}: {ex.Message}");
            }
        }

        private async Task<ScanResult> AnalyzeFileRealTime(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name.ToLowerInvariant();
                var extension = fileInfo.Extension.ToLowerInvariant();
                
                // Snabb hotanalys
                var threatLevel = ThreatLevel.Low;
                var reasons = new List<string>();
                
                // Kolla suspekta extensions
                if (_config.SuspiciousExtensions.Contains(extension))
                {
                    threatLevel = ThreatLevel.Medium;
                    reasons.Add($"Suspekt filtyp: {extension}");
                }
                
                // Kolla kända hackerverktyg
                var suspiciousNames = new[] { "nircmd", "psexec", "netcat", "nc", "mimikatz", "procdump" };
                if (suspiciousNames.Any(name => fileName.Contains(name)))
                {
                    threatLevel = ThreatLevel.Critical;
                    reasons.Add("Känt hackerverktyg");
                }
                
                // Kolla dubbla extensions
                if (fileName.Count(c => c == '.') > 1 && _config.SuspiciousExtensions.Contains(extension))
                {
                    threatLevel = ThreatLevel.High;
                    reasons.Add("Dubbel fil-extension");
                }
                
                if (!reasons.Any()) return string.Empty;
                
                return new ScanResult
                {
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    CreatedDate = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    FileType = extension,
                    ThreatLevel = threatLevel,
                    Reason = string.Join(", ", reasons),
                    FileHash = "REALTIME_SCAN"
                };
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task PerformBackgroundScanAsync()
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested) return;

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
                        await HandleThreatDetected(result);
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

        private async Task HandleThreatDetected(ScanResult threat)
        {
            await System.Threading.Tasks.Task.Yield();

            TotalThreatsFound++;
            
            _logViewer.AddLogEntry(LogLevel.Warning, "Security", 
                $"🚨 HOT IDENTIFIERAT: {Path.GetFileName(threat.FilePath)} ({threat.ThreatLevel})");
            
            // Notifiera användaren
            ThreatDetected?.Invoke(this, new ThreatDetectedEventArgs(threat, AutoCleanMode));
            
            // Hantera automatiskt om auto-läge är aktivt
            if (AutoCleanMode)
            {
                await HandleThreatAutomatically(threat);
            }
        }

        private async Task HandleThreatAutomatically(ScanResult threat)
        {
            try
            {
                bool success = false;
                string action = "";
                
                // Hantera baserat på hotnivå
                if (threat.ThreatLevel >= ThreatLevel.High)
                {
                    // Höga hot: Radera direkt
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
                    _logViewer.AddLogEntry(LogLevel.Information, "AutoClean", 
                        $"🤖 AUTO-RENSNING: {Path.GetFileName(threat.FilePath)} {action}");
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

        public void Dispose()
        {
            StopProtectionAsync().Wait();
            _cancellationTokenSource?.Dispose();
        }
    }

    public class ThreatDetectedEventArgs : EventArgs
    {
        public ScanResult Threat { get; }
        public bool WasHandledAutomatically { get; }
        
        public ThreatDetectedEventArgs(ScanResult threat, bool wasHandledAutomatically)
        {
            Threat = threat;
            WasHandledAutomatically = wasHandledAutomatically;
        }
    }

    public class ProtectionStatusChangedEventArgs : EventArgs
    {
        public bool IsActive { get; }
        
        public ProtectionStatusChangedEventArgs(bool isActive)
        {
            IsActive = isActive;
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
    }
}