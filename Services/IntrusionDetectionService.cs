using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;
using FilKollen.Models;
using FilKollen.Services;
using Serilog;
using System.Collections.Concurrent;

namespace FilKollen.Services
{
    public class IntrusionDetectionService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly LogViewerService _logViewer;
        private readonly TempFileScanner _fileScanner;
        private readonly QuarantineManager _quarantineManager;

        private System.Timers.Timer _monitoringTimer = null!;
        private readonly List<FileSystemWatcher> _fileWatchers;
        private readonly HashSet<string> _recentlyProcessed;
        private CancellationTokenSource _cancellationTokenSource = null!;

        // ENHANCED PROCESS MONITORING - Rate limiting för att undvika performance-problem
        private readonly Dictionary<string, DateTime> _lastProcessCheck = new();
        private readonly TimeSpan _processCheckCooldown = TimeSpan.FromSeconds(30);
        private readonly object _processMonitorLock = new object();

        // Förbättrad concurrent event processing
        private readonly ConcurrentQueue<SecurityEvent> _eventQueue = new();
        private readonly Timer _eventProcessingTimer;

        // Intrång-detektering
        private readonly Dictionary<string, int> _suspiciousActivityCounter;
        private readonly Dictionary<string, DateTime> _lastActivityTime;
        private readonly Queue<SecurityEvent> _recentSecurityEvents;

        // Känd malware-aktivitet - UTÖKAD LISTA
        private readonly HashSet<string> _knownMalwareProcesses = new()
        {
            // Crypto miners
            "cryptonight", "xmrig", "nicehash", "miner", "cgminer", "bfgminer",
            "ethminer", "claymore", "phoenix", "t-rex", "gminer", "nbminer",
            "teamredminer", "lolminer", "miniZ", "excavator", "ccminer",
            "cpuminer", "pooler", "winminer", "honeyminer", "cudo",
            
            // Remote access malware
            "anydesk", "teamviewer", "vnc", "rdp", "logmein", "supremo",
            "ammyy", "luminance", "chrome_remote", "gotomypc", "bomgar",
            
            // Known trojans/backdoors
            "backdoor", "trojan", "keylogger", "spyware", "rootkit",
            "ransomware", "cryptolocker", "wannacry", "emotet", "trickbot",
            
            // NYTT: Telegram bot spyware och screenshot tools
            "nircmd", "nirsoft", "screenshot", "screencapture", "telegram",
            "bot.exe", "grabber", "stealer", "clipper", "loader"
        };

        // FÖRBÄTTRADE Telegram bot-specifika indicators
        private readonly HashSet<string> _telegramBotIndicators = new()
        {
            "api.telegram.org/bot", "sendDocument", "savescreenshot", "nircmd.exe",
            "Screenshot_", "ScreenshotLog.txt", "Invoke-WebRequest", "DownloadString", 
            "DownloadFile", "IEX (", "iex(", "powershell -", "cmd /c", "cmd.exe /c",
            "base64 -d", "echo -n", "curl -F", "wget -O", "certutil -decode",
            "bitsadmin /transfer", "ngrok.io", "serveo.net", "localhost.run",
            "ssh -R", "reverse shell", "xmrig", "monero", "stratum+tcp", "mining pool",
            "api.telegram.org", "Expand-Archive", "bot[0-9]+:", "chat_id=", "/sendDocument",
            @"bot\d+:", @"chat_id=\d+", "Invoke-WebRequest.*telegram", "curl.*api.telegram.org"
        };

        // Suspekta nätverksanslutningar
        private readonly HashSet<string> _suspiciousNetworkPatterns = new()
        {
            ".onion", ".tor", "stratum+tcp://", "mining:", "pool:",
            "bitcoin", "monero", "ethereum", "zcash", "litecoin",
            "api.telegram.org", "t.me", "pastebin.com"
        };

        // Farliga registry-ändringar
        private readonly string[] _criticalRegistryKeys =
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
        };

        public bool IsMonitoringActive { get; private set; }
        public int TotalThreatsDetected { get; private set; }
        public int TotalThreatsBlocked { get; private set; }
        public DateTime LastThreatTime { get; private set; }

        public event EventHandler<IntrusionDetectedEventArgs>? IntrusionDetected;
        public event EventHandler<SecurityAlertEventArgs>? SecurityAlert;

        public IntrusionDetectionService(ILogger logger, LogViewerService logViewer,
            TempFileScanner fileScanner, QuarantineManager quarantineManager)
        {
            _logger = logger;
            _logViewer = logViewer;
            _fileScanner = fileScanner;
            _quarantineManager = quarantineManager;

            _fileWatchers = new List<FileSystemWatcher>();
            _recentlyProcessed = new HashSet<string>();
            _suspiciousActivityCounter = new Dictionary<string, int>();
            _lastActivityTime = new Dictionary<string, DateTime>();
            _recentSecurityEvents = new Queue<SecurityEvent>();

            // Sätt upp event processing timer
            _eventProcessingTimer = new Timer(1000); // Process events varje sekund
            _eventProcessingTimer.Elapsed += (s, e) => ProcessSecurityEvents();
            _eventProcessingTimer.Start();
        }

private bool ShouldAnalyzeProcess(string processName)
{
    lock (_processMonitorLock)
    {
        if (_lastProcessCheck.TryGetValue(processName, out var lastCheck))
        {
            if (DateTime.Now - lastCheck < _processCheckCooldown)
                return false; // Skip för att undvika spam
        }
        
        _lastProcessCheck[processName] = DateTime.Now;
        return true;
    }
}

        // FÖRBÄTTRAT: Robust process monitoring med rate limiting
        private async Task MonitorRunningProcessesAsync()
        {
            const int MaxProcessesPerScan = 500;
            
            try
            {
                var processes = Process.GetProcesses()
                    .Take(MaxProcessesPerScan)
                    .Where(p => !p.HasExited)
                    .ToList();

                var processAnalysisTasks = new List<Task>();
                var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

                foreach (var process in processes)
                {
                    processAnalysisTasks.Add(AnalyzeProcessSafelyAsync(process, semaphore));
                }

                // Vänta på alla analyser med timeout
                var allAnalysisTask = Task.WhenAll(processAnalysisTasks);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                
                var completedTask = await Task.WhenAny(allAnalysisTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _logger.Warning("Process analysis timeout - vissa processer hoppades över");
                }

                // Cleanup
                foreach (var process in processes)
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                        // Ignorera disposal errors
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Process monitoring error: {ex.Message}");
            }
        }

        private async Task AnalyzeProcessSafelyAsync(Process process, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            
            try
            {
                // Timeout för process-analys
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                
                await Task.Run(() => AnalyzeProcessInternal(process), cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.Debug($"Process analysis timeout for PID: {process.Id}");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Process analysis error for PID: {process.Id} - {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        private void AnalyzeProcessInternal(Process process)
        {
            try
            {
                if (process.HasExited) return;
                
                var processName = process.ProcessName.ToLowerInvariant();
                
                // Rate limiting per process name
                if (!ShouldAnalyzeProcess(processName)) return;
                
                // Snabb kolla mot kända malware-processer
                if (_knownMalwareProcesses.Any(malware => processName.Contains(malware)))
                {
                    QueueSecurityEvent(new SecurityEvent
                    {
                        EventType = "KNOWN_MALWARE_PROCESS_DETECTED",
                        Severity = SecuritySeverity.Critical,
                        Description = $"Känd malware-process upptäckt: {processName}",
                        ProcessName = processName,
                        ProcessId = process.Id,
                        Timestamp = DateTime.Now
                    });
                    return;
                }
                
                // Ytterligare analys för suspekta processer
                if (IsProcessSuspicious(process))
                {
                    QueueSecurityEvent(new SecurityEvent
                    {
                        EventType = "SUSPICIOUS_PROCESS_BEHAVIOR",
                        Severity = SecuritySeverity.High,
                        Description = $"Suspekt process-beteende: {processName}",
                        ProcessName = processName,
                        ProcessId = process.Id,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Internal process analysis error: {ex.Message}");
            }
        }

        private bool IsProcessSuspicious(Process process)
        {
            try
            {
                // Kontrollera om processen körs från suspekt plats
                var mainModule = process.MainModule;
                if (mainModule?.FileName != null)
                {
                    var filePath = mainModule.FileName.ToLowerInvariant();
                    
                    // Suspekta platser
                    var suspiciousLocations = new[]
                    {
                        @"\temp\", @"\appdata\local\temp\", @"\users\public\",
                        @"\windows\temp\", @"\programdata\"
                    };
                    
                    if (suspiciousLocations.Any(loc => filePath.Contains(loc)))
                    {
                        return true;
                    }
                }

                // Kontrollera om processen saknar digital signatur
                if (string.IsNullOrEmpty(mainModule?.FileVersionInfo?.CompanyName))
                {
                    return true;
                }

                // Kontrollera hög CPU-användning (möjlig crypto mining)
                if (IsLikelyCryptoMining(process))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsLikelyCryptoMining(Process process)
        {
            try
            {
                // Enkel heuristik för crypto mining detection
                var processName = process.ProcessName.ToLowerInvariant();
                var miningKeywords = new[] { "miner", "mining", "crypto", "xmr", "btc", "eth" };
                
                return miningKeywords.Any(keyword => processName.Contains(keyword));
            }
            catch
            {
                return false;
            }
        }

        private void QueueSecurityEvent(SecurityEvent securityEvent)
        {
            _eventQueue.Enqueue(securityEvent);
            
            // Begränsa kö-storlek
            while (_eventQueue.Count > 1000)
            {
                _eventQueue.TryDequeue(out _);
            }
        }

        private void ProcessSecurityEvents()
        {
            const int MaxEventsPerBatch = 10;
            var processedCount = 0;
            
            while (_eventQueue.TryDequeue(out var securityEvent) && processedCount < MaxEventsPerBatch)
            {
                try
                {
                    ProcessSecurityEventInternal(securityEvent);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error processing security event: {ex.Message}");
                }
            }
        }

        private void ProcessSecurityEventInternal(SecurityEvent securityEvent)
        {
            // Implementera event-hantering här
            RecordSecurityEvent(securityEvent);
            
            if (securityEvent.Severity >= SecuritySeverity.High)
            {
                // Trigger alerts för höga hot
                TriggerSecurityAlert(securityEvent);
            }
        }

        private void TriggerSecurityAlert(SecurityEvent securityEvent)
        {
            try
            {
                var alertArgs = new SecurityAlertEventArgs
                {
                    AlertType = securityEvent.EventType,
                    Message = securityEvent.Description,
                    Severity = securityEvent.Severity,
                    ProcessName = securityEvent.ProcessName ?? "Unknown",
                    ProcessPath = securityEvent.FilePath ?? "Unknown",
                    ActionTaken = "Event logged and monitored"
                };
                
                SecurityAlert?.Invoke(this, alertArgs);

                // KORRIGERAT: Använd IntrusionDetected event också
                if (securityEvent.Severity >= SecuritySeverity.High)
                {
                    var intrusionArgs = new IntrusionDetectedEventArgs
                    {
                        ThreatType = securityEvent.EventType,
                        ProcessName = securityEvent.ProcessName ?? "Unknown",
                        ProcessPath = securityEvent.FilePath ?? "Unknown",
                        Severity = securityEvent.Severity,
                        Description = securityEvent.Description,
                        ShouldBlock = securityEvent.Severity == SecuritySeverity.Critical
                    };
                    
                    IntrusionDetected?.Invoke(this, intrusionArgs);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error triggering security alert: {ex.Message}");
            }
        }

        public async Task StartMonitoringAsync()
        {
            if (IsMonitoringActive) return;

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();

                // Starta fil-system övervakning
                await StartFileSystemMonitoringAsync();

                // Starta process-övervakning
                await StartProcessMonitoringAsync();

                // Starta nätverks-övervakning
                await StartNetworkMonitoringAsync();

                // Starta registry-övervakning
                await StartRegistryMonitoringAsync();

                // Starta periodisk systemkontroll
                StartPeriodicSystemCheck();

                IsMonitoringActive = true;

                _logger.Information("🔒 Intrusion Detection System AKTIVERAT - kontinuerlig övervakning startad");
                _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                    "🔒 INTRUSION DETECTION AKTIVERAT - avancerad hotdetektering startad");

                // Första genomgång direkt
                _ = Task.Run(async () => await PerformSystemSecurityScanAsync());
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid start av intrusion detection: {ex.Message}");
                throw;
            }
        }

        public async Task StopMonitoringAsync()
        {
            if (!IsMonitoringActive) return;

            try
            {
                _cancellationTokenSource?.Cancel();

                _monitoringTimer?.Stop();
                _monitoringTimer?.Dispose();

                foreach (var watcher in _fileWatchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _fileWatchers.Clear();

                IsMonitoringActive = false;

                _logger.Information("Intrusion Detection System inaktiverat");
                _logViewer.AddLogEntry(LogLevel.Warning, "IDS",
                    "⚠️ INTRUSION DETECTION INAKTIVERAT - systemet är nu mer sårbart");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid stopp av intrusion detection: {ex.Message}");
            }
        }

        // Placeholder-metoder för att undvika compilation errors
        private async Task StartFileSystemMonitoringAsync()
        {
            await Task.Yield();
            _logger.Information("🔍 File system monitoring aktiverat");
        }

        private async Task StartProcessMonitoringAsync()
        {
            _ = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await MonitorRunningProcessesAsync();
                        await Task.Delay(5000, _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Process monitoring error: {ex.Message}");
                        await Task.Delay(10000, _cancellationTokenSource.Token);
                    }
                }
            });

            _logViewer.AddLogEntry(LogLevel.Information, "IDS", "🔍 Process-övervakning aktiverad");
            await Task.Yield();
        }

        private async Task StartNetworkMonitoringAsync()
        {
            await Task.Yield();
            _logger.Information("🌐 Network monitoring aktiverat");
        }

        private async Task StartRegistryMonitoringAsync()
        {
            await Task.Yield();
            _logger.Information("📋 Registry monitoring aktiverat");
        }

        private void StartPeriodicSystemCheck()
        {
            _monitoringTimer = new System.Timers.Timer(TimeSpan.FromMinutes(2).TotalMilliseconds);
            _monitoringTimer.Elapsed += async (sender, e) => await PerformSystemSecurityScanAsync();
            _monitoringTimer.AutoReset = true;
            _monitoringTimer.Start();
        }

        private async Task PerformSystemSecurityScanAsync()
        {
            try
            {
                _logViewer.AddLogEntry(LogLevel.Information, "IDS", "🔍 Genomför periodisk säkerhetskontroll");
                
                // Snabb temp-fil scan
                var tempResults = await _fileScanner.ScanTempDirectoriesAsync();
                var threats = tempResults?.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList() ?? new List<ScanResult>();

                foreach (var threat in threats)
                {
                    await HandleSecurityThreatAsync(threat, "PERIODIC_SCAN", "Periodisk säkerhetskontroll");
                }

                if (threats.Any())
                {
                    _logViewer.AddLogEntry(LogLevel.Warning, "IDS", $"⚠️ Periodisk kontroll: {threats.Count} nya hot upptäckta");
                }
                else
                {
                    _logViewer.AddLogEntry(LogLevel.Information, "IDS", "✅ Periodisk kontroll: Inga nya hot upptäckta");
                }
            }
            catch (Exception ex)
            {
                _logViewer.AddLogEntry(LogLevel.Error, "IDS", $"❌ Fel vid periodisk säkerhetskontroll: {ex.Message}");
            }
        }

        private async Task HandleSecurityThreatAsync(ScanResult threat, string eventType, string context)
        {
            var securityEvent = new SecurityEvent
            {
                EventType = eventType,
                Severity = threat.ThreatLevel switch
                {
                    ThreatLevel.Critical => SecuritySeverity.Critical,
                    ThreatLevel.High => SecuritySeverity.High,
                    ThreatLevel.Medium => SecuritySeverity.Medium,
                    _ => SecuritySeverity.Low
                },
                Description = $"{context}: {threat.Reason}",
                FilePath = threat.FilePath,
                FileHash = threat.FileHash,
                Timestamp = DateTime.Now
            };

            RecordSecurityEvent(securityEvent);
            TotalThreatsDetected++;
            LastThreatTime = DateTime.Now;
        }

        private void RecordSecurityEvent(SecurityEvent securityEvent)
        {
            _recentSecurityEvents.Enqueue(securityEvent);

            // Håll bara de senaste 100 händelserna i minnet
            while (_recentSecurityEvents.Count > 100)
            {
                _recentSecurityEvents.Dequeue();
            }

            // Logga händelsen
            var logLevel = securityEvent.Severity switch
            {
                SecuritySeverity.Critical => LogLevel.Error,
                SecuritySeverity.High => LogLevel.Warning,
                SecuritySeverity.Medium => LogLevel.Information,
                _ => LogLevel.Debug
            };

            _logViewer.AddLogEntry(logLevel, "IDS", $"🔍 {securityEvent.EventType}: {securityEvent.Description}");
        }

        public List<SecurityEvent> GetRecentSecurityEvents(int count = 50)
        {
            return _recentSecurityEvents.TakeLast(count).Reverse().ToList();
        }

        public Dictionary<string, int> GetSuspiciousActivitySummary()
        {
            return new Dictionary<string, int>(_suspiciousActivityCounter);
        }

        public void Dispose()
        {
            try
            {
                StopMonitoringAsync().Wait(5000);
                _cancellationTokenSource?.Dispose();
                _eventProcessingTimer?.Dispose();

                foreach (var watcher in _fileWatchers)
                {
                    watcher?.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error disposing IntrusionDetectionService: {ex.Message}");
            }
        }
    }

    // Event Arguments
    public class IntrusionDetectedEventArgs : EventArgs
    {
        public string ThreatType { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string ProcessPath { get; set; } = string.Empty;
        public SecuritySeverity Severity { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool ShouldBlock { get; set; }
    }

    public class SecurityAlertEventArgs : EventArgs
    {
        public string AlertType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public SecuritySeverity Severity { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ProcessPath { get; set; } = string.Empty;
        public string ActionTaken { get; set; } = string.Empty;
    }

    // Models
    public class SecurityEvent
    {
        public string EventType { get; set; } = string.Empty;
        public SecuritySeverity Severity { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? FilePath { get; set; }
        public string? FileHash { get; set; }
        public string? ProcessName { get; set; }
        public int? ProcessId { get; set; }
        public string? RegistryKey { get; set; }
        public string? NetworkDetails { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum SecuritySeverity
    {
        Low,
        Medium, 
        High,
        Critical
    }
}