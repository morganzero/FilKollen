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
using System.Threading;
using System.Threading.Tasks;

namespace FilKollen.Services
{

    public partial class IntrusionDetectionService
    {
        private readonly ConcurrentQueue<SecurityEvent> _eventQueue = new();
        private readonly Timer _eventProcessingTimer;
        private readonly object _processMonitorLock = new object();
        
        // FÖRBÄTTRING: Robust process monitoring med rate limiting
        private async Task MonitorRunningProcessesAsync()
        {
            const int MaxProcessesPerScan = 500;
            const int ProcessScanIntervalMs = 5000;
            
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

        private readonly Dictionary<string, DateTime> _lastProcessCheck = new();
        private readonly TimeSpan _processCheckCooldown = TimeSpan.FromSeconds(30);

        private bool ShouldAnalyzeProcess(string processName)
        {
            lock (_processMonitorLock)
            {
                if (_lastProcessCheck.TryGetValue(processName, out var lastCheck))
                {
                    if (DateTime.Now - lastCheck < _processCheckCooldown)
                    {
                        return false;
                    }
                }
                
                _lastProcessCheck[processName] = DateTime.Now;
                return true;
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
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error triggering security alert: {ex.Message}");
            }
        }
    }
}
    public class IntrusionDetectionService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly LogViewerService _logViewer;
        private readonly TempFileScanner _fileScanner; // KORRIGERAT namn
        private readonly QuarantineManager _quarantineManager;

        private System.Timers.Timer _monitoringTimer = null!;
        private readonly List<FileSystemWatcher> _fileWatchers;
        private readonly HashSet<string> _recentlyProcessed;
        private CancellationTokenSource _cancellationTokenSource = null!;

        // Intrång-detektering
        private readonly Dictionary<string, int> _suspiciousActivityCounter;
        private readonly Dictionary<string, DateTime> _lastActivityTime;
        private readonly Queue<SecurityEvent> _recentSecurityEvents;

        // Känd malware-aktivitet
        private readonly HashSet<string> _knownMalwareProcesses = new()
        {
            "cryptonight", "xmrig", "nicehash", "miner", "cgminer", "bfgminer",
            "ethminer", "claymore", "phoenix", "t-rex", "gminer", "nbminer",
            "teamredminer", "lolminer", "miniZ", "excavator", "ccminer",
            "cpuminer", "pooler", "winminer", "honeyminer", "cudo",
            
            // Remote access malware
            "anydesk", "teamviewer", "vnc", "rdp", "logmein", "supremo",
            "ammyy", "luminance", "chrome_remote", "gotomypc", "bomgar",
            
            // Known trojans/backdoors
            "backdoor", "trojan", "keylogger", "spyware", "rootkit",
            "ransomware", "cryptolocker", "wannacry", "emotet", "trickbot"
        };

        // Suspekta nätverksanslutningar
        private readonly HashSet<string> _suspiciousNetworkPatterns = new()
        {
            ".onion", ".tor", "stratum+tcp://", "mining:", "pool:",
            "bitcoin", "monero", "ethereum", "zcash", "litecoin"
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
            _fileScanner = fileScanner; // KORRIGERAT namn från _tempFileScanner
            _quarantineManager = quarantineManager;

            _fileWatchers = new List<FileSystemWatcher>();
            _recentlyProcessed = new HashSet<string>();
            _suspiciousActivityCounter = new Dictionary<string, int>();
            _lastActivityTime = new Dictionary<string, DateTime>();
            _recentSecurityEvents = new Queue<SecurityEvent>();
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

        private async Task StartFileSystemMonitoringAsync()
        {
            await Task.Yield();

            var criticalPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "Startup")
            };

            foreach (var path in criticalPaths.Where(Directory.Exists))
            {
                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnCriticalFileCreated;
                    watcher.Changed += OnCriticalFileChanged;
                    watcher.Renamed += OnCriticalFileRenamed;

                    _fileWatchers.Add(watcher);

                    _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                        $"🔍 Övervakar kritisk sökväg: {path}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Kunde inte övervaka {path}: {ex.Message}");
                }
            }
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
                        await Task.Delay(5000, _cancellationTokenSource.Token); // Kolla var 5:e sekund
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
            await Task.Yield(); // TILLAGD för att uppfylla async contract
        }

        private async Task StartNetworkMonitoringAsync()
        {
            _ = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await MonitorNetworkConnectionsAsync();
                        await Task.Delay(10000, _cancellationTokenSource.Token); // Kolla var 10:e sekund
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Network monitoring error: {ex.Message}");
                        await Task.Delay(15000, _cancellationTokenSource.Token);
                    }
                }
            });

            _logViewer.AddLogEntry(LogLevel.Information, "IDS", "🌐 Nätverks-övervakning aktiverad");
            await Task.Yield(); // TILLAGD för att uppfylla async contract
        }

        private async Task StartRegistryMonitoringAsync()
        {
            await Task.Yield(); // TILLAGD för att uppfylla async contract

            // Registry monitoring är komplex och kräver WMI eller Registry notification API
            // För MVP implementerar vi en periodisk kontroll istället

            _logViewer.AddLogEntry(LogLevel.Information, "IDS", "📋 Registry-övervakning aktiverad");
        }

        private void StartPeriodicSystemCheck()
        {
            _monitoringTimer = new System.Timers.Timer(TimeSpan.FromMinutes(2).TotalMilliseconds);
            _monitoringTimer.Elapsed += async (sender, e) => await PerformSystemSecurityScanAsync();
            _monitoringTimer.AutoReset = true;
            _monitoringTimer.Start();

            _logViewer.AddLogEntry(LogLevel.Information, "IDS", "⏰ Periodisk säkerhetskontroll aktiverad (2-minuters intervall)");
        }

        private async void OnCriticalFileCreated(object sender, FileSystemEventArgs e)
        {
            await ProcessCriticalFileEvent(e.FullPath, "skapad", "CRITICAL_FILE_CREATED");
        }

        private async void OnCriticalFileChanged(object sender, FileSystemEventArgs e)
        {
            await ProcessCriticalFileEvent(e.FullPath, "ändrad", "CRITICAL_FILE_MODIFIED");
        }

        private async void OnCriticalFileRenamed(object sender, RenamedEventArgs e)
        {
            await ProcessCriticalFileEvent(e.FullPath, "omdöpt", "CRITICAL_FILE_RENAMED");
        }

        private async Task ProcessCriticalFileEvent(string filePath, string action, string eventType)
        {
            try
            {
                var key = $"{filePath}_{action}";
                if (_recentlyProcessed.Contains(key)) return;

                _recentlyProcessed.Add(key);
                _ = Task.Delay(30000).ContinueWith(t => _recentlyProcessed.Remove(key));

                await Task.Delay(1000); // Vänta på att filen ska skrivas klart

                if (!File.Exists(filePath)) return;

                var fileName = Path.GetFileName(filePath);
                var fileInfo = new FileInfo(filePath);

                _logViewer.AddLogEntry(LogLevel.Warning, "IDS",
                    $"🚨 KRITISK FILAKTIVITET: {fileName} {action} i {Path.GetDirectoryName(filePath)}");

                // Analysera filen omedelbart
                var scanResult = await _fileScanner.ScanSingleFileAsync(filePath);
                if (scanResult != null)
                {
                    await HandleSecurityThreatAsync(scanResult, eventType, $"Kritisk fil {action} i systemkatalog");
                }
                else
                {
                    // Även om inga direkta hot hittas, logga ändå kritisk aktivitet
                    var securityEvent = new SecurityEvent
                    {
                        EventType = eventType,
                        Severity = SecuritySeverity.High,
                        Description = $"Fil {action} i kritisk systemkatalog: {fileName}",
                        FilePath = filePath,
                        ProcessName = GetProcessThatAccessedFile(filePath),
                        Timestamp = DateTime.Now
                    };

                    RecordSecurityEvent(securityEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid kritisk filbearbetning {filePath}: {ex.Message}");
            }
        }

        private async Task MonitorRunningProcessesAsync()
        {
            try
            {
                var processes = Process.GetProcesses();

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited) continue;

                        var processName = process.ProcessName.ToLowerInvariant();

                        // Kolla mot kända malware-processer
                        if (_knownMalwareProcesses.Any(malware => processName.Contains(malware)))
                        {
                            await HandleSuspiciousProcessAsync(process, "KNOWN_MALWARE_PROCESS");
                        }

                        // Kolla efter suspekta process-egenskaper
                        await CheckProcessCharacteristicsAsync(process);

                    }
                    catch (Exception ex)
                    {
                        // Ignorera fel för individuella processer (kan vara ACL-problem)
                        _logger.Debug($"Process check error for {process.Id}: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Process monitoring error: {ex.Message}");
            }
        }

        private async Task CheckProcessCharacteristicsAsync(Process process)
        {
            try
            {
                // Kolla CPU-användning för crypto mining
                if (await IsLikelyCryptoMinerAsync(process))
                {
                    await HandleSuspiciousProcessAsync(process, "CRYPTO_MINING_DETECTED");
                }

                // Kolla efter processer utan beskrivning (ofta malware)
                if (string.IsNullOrEmpty(process.MainModule?.FileVersionInfo.FileDescription))
                {
                    var executablePath = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(executablePath) && IsInSuspiciousLocation(executablePath))
                    {
                        await HandleSuspiciousProcessAsync(process, "UNSIGNED_PROCESS_SUSPICIOUS_LOCATION");
                    }
                }

                // Kolla efter processer som körs från temp-kataloger
                var mainModulePath = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(mainModulePath))
                {
                    var tempPaths = new[]
                    {
                        Environment.GetEnvironmentVariable("TEMP")?.ToLowerInvariant(),
                        Environment.GetEnvironmentVariable("TMP")?.ToLowerInvariant(),
                        @"c:\windows\temp",
                        @"c:\users\public"
                    };

                    if (tempPaths.Any(temp => !string.IsNullOrEmpty(temp) &&
                        mainModulePath.ToLowerInvariant().StartsWith(temp)))
                    {
                        await HandleSuspiciousProcessAsync(process, "PROCESS_RUNNING_FROM_TEMP");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Process characteristics check error: {ex.Message}");
            }
        }

        private async Task<bool> IsLikelyCryptoMinerAsync(Process process)
        {
            try
            {
                // Kolla CPU-användning under en kort period
                var initialCpuTime = process.TotalProcessorTime;
                await Task.Delay(1000);

                if (process.HasExited) return false;

                var finalCpuTime = process.TotalProcessorTime;
                var cpuUsed = (finalCpuTime - initialCpuTime).TotalMilliseconds;

                // Om processen använder mer än 80% CPU under mätperioden
                if (cpuUsed > 800) // 800ms av 1000ms = 80%
                {
                    // Dubbelkolla genom att se om det inte är en känd systemprocess
                    var knownSystemProcesses = new[]
                    {
                        "svchost", "dwm", "csrss", "winlogon", "explorer",
                        "taskhostw", "services", "lsass", "smss"
                    };

                    var processName = process.ProcessName.ToLowerInvariant();
                    return !knownSystemProcesses.Any(sys => processName.Contains(sys));
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private readonly HashSet<string> _telegramBotIndicators = new()
        {
            "api.telegram.org",
            "sendDocument",
            "savescreenshot",
            "nircmd.exe",
            "Screenshot_",
            "ScreenshotLog.txt",
            "Invoke-WebRequest",
            "Expand-Archive",
            "bot[0-9]+:",
            "chat_id=",
            "/sendDocument"
        };

        private readonly HashSet<string> _suspiciousScreenshotPatterns = new()
        {
            "savescreenshot",
            "screenshot_",
            "capture_screen",
            "desktop_capture",
            "screen_grab",
            "printscreen"
        };

        private async Task<bool> DetectTelegramBotActivityAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                var content = await File.ReadAllTextAsync(filePath);
                var suspiciousCount = 0;
                var detectedPatterns = new List<string>();

                // Kontrollera för Telegram bot-aktivitet
                foreach (var indicator in _telegramBotIndicators)
                {
                    if (content.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                    {
                        suspiciousCount++;
                        detectedPatterns.Add(indicator);
                    }
                }

                // Kontrollera för screenshot-aktivitet 
                foreach (var pattern in _suspiciousScreenshotPatterns)
                {
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        suspiciousCount++;
                        detectedPatterns.Add($"screenshot:{pattern}");
                    }
                }

                // Om vi hittar 3+ indicators = troligt Telegram bot attack
                if (suspiciousCount >= 3)
                {
                    var securityEvent = new SecurityEvent
                    {
                        EventType = "TELEGRAM_BOT_SPYWARE_DETECTED",
                        Severity = SecuritySeverity.Critical,
                        Description = $"🚨 TELEGRAM BOT SPYWARE: {Path.GetFileName(filePath)} - Skickar screenshots till Telegram",
                        FilePath = filePath,
                        ProcessName = "Telegram Bot Script",
                        Timestamp = DateTime.Now
                    };

                    RecordSecurityEvent(securityEvent);

                    // Omedelbar blockering och karantän
                    await BlockTelegramBotThreatAsync(filePath, detectedPatterns);

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error detecting Telegram bot activity in {filePath}: {ex.Message}");
                return false;
            }
        }

        private async Task BlockTelegramBotThreatAsync(string filePath, List<string> detectedPatterns)
        {
            try
            {
                _logViewer.AddLogEntry(LogLevel.Error, "CRITICAL",
                    $"🚨 KRITISKT: TELEGRAM BOT SPYWARE DETEKTERAT - {Path.GetFileName(filePath)}");

                // 1. Sätt filen i karantän omedelbart
                var scanResult = new ScanResult
                {
                    FilePath = filePath,
                    ThreatLevel = ThreatLevel.Critical,
                    Reason = $"Telegram Bot Spyware - Detekterade patterns: {string.Join(", ", detectedPatterns)}",
                    FileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
                    CreatedDate = File.Exists(filePath) ? File.GetCreationTime(filePath) : DateTime.Now
                };

                var quarantineResult = await _quarantineManager.QuarantineFileAsync(scanResult);

                if (quarantineResult)
                {
                    _logViewer.AddLogEntry(LogLevel.Information, "CRITICAL",
                        $"✅ Telegram bot spyware satt i karantän: {Path.GetFileName(filePath)}");
                }

                // 2. Blockera nätverksanslutningar till Telegram (via hosts)
                await BlockTelegramDomainsAsync();

                // 3. Sök efter relaterade filer (nircmd.exe, temp screenshots etc)
                await CleanupTelegramBotArtifactsAsync();

                // 4. Trigger kritisk säkerhetsvarning
                var alertArgs = new SecurityAlertEventArgs
                {
                    AlertType = "TELEGRAM_BOT_SPYWARE_BLOCKED",
                    Message = "KRITISKT: Telegram Bot Spyware blockerat - skickar screenshots till attackerare",
                    Severity = SecuritySeverity.Critical,
                    ProcessName = "Telegram Bot Script",
                    ProcessPath = filePath,
                    ActionTaken = "Fil karantänerad, nätverksaccess blockerad, relaterade filer rensade"
                };

                SecurityAlert?.Invoke(this, alertArgs);

                TotalThreatsBlocked++;

            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid blockering av Telegram bot threat: {ex.Message}");
            }
        }

        private async Task BlockTelegramDomainsAsync()
        {
            try
            {
                var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers", "etc", "hosts");

                if (!File.Exists(hostsPath)) return;

                var hostsContent = await File.ReadAllTextAsync(hostsPath);

                // Lägg till Telegram-blockering om den inte redan finns
                if (!hostsContent.Contains("# FilKollen Telegram Bot Block"))
                {
                    var telegramBlocks = new[]
                    {
                        "\n# FilKollen Telegram Bot Block - START",
                        "0.0.0.0 api.telegram.org",
                        "0.0.0.0 telegram.org",
                        "0.0.0.0 web.telegram.org",
                        "0.0.0.0 t.me",
                        "# FilKollen Telegram Bot Block - END\n"
                    };

                    await File.AppendAllTextAsync(hostsPath, string.Join('\n', telegramBlocks));

                    _logViewer.AddLogEntry(LogLevel.Information, "CRITICAL",
                        "🛡️ Telegram-domäner blockerade via hosts-fil");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte blockera Telegram-domäner: {ex.Message}");
            }
        }

        private async Task CleanupTelegramBotArtifactsAsync()
        {
            try
            {
                var tempPaths = new[]
                {
                    Environment.GetEnvironmentVariable("TEMP"),
                    Environment.GetEnvironmentVariable("TMP"),
                    @"C:\Windows\Temp"
                };

                var suspiciousFiles = new List<string>();

                foreach (var tempPath in tempPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
                {
                    var files = Directory.GetFiles(tempPath, "*.*", SearchOption.TopDirectoryOnly);

                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file).ToLowerInvariant();

                        // Sök efter nircmd.exe, screenshot-filer, log-filer
                        if (fileName.Contains("nircmd") ||
                            fileName.Contains("screenshot") ||
                            fileName.Contains("screenshotlog") ||
                            fileName.EndsWith(".zip") && fileName.Contains("nircmd"))
                        {
                            suspiciousFiles.Add(file);
                        }
                    }
                }

                // Sätt alla suspekta filer i karantän
                foreach (var file in suspiciousFiles)
                {
                    try
                    {
                        var scanResult = new ScanResult
                        {
                            FilePath = file,
                            ThreatLevel = ThreatLevel.High,
                            Reason = "Telegram Bot Spyware - Relaterad artefakt",
                            FileSize = new FileInfo(file).Length,
                            CreatedDate = File.GetCreationTime(file)
                        };

                        await _quarantineManager.QuarantineFileAsync(scanResult);

                        _logViewer.AddLogEntry(LogLevel.Information, "CLEANUP",
                            $"🧹 Rensade Telegram bot artefakt: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Kunde inte rensa artefakt {file}: {ex.Message}");
                    }
                }

                if (suspiciousFiles.Any())
                {
                    _logViewer.AddLogEntry(LogLevel.Information, "CLEANUP",
                        $"✅ Telegram bot cleanup slutförd: {suspiciousFiles.Count} artefakter rensade");
                }

            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid cleanup av Telegram bot artefakter: {ex.Message}");
            }
        }

        private bool IsInSuspiciousLocation(string filePath)
        {
            var suspiciousLocations = new[]
            {
                @"\AppData\Roaming\",
                @"\AppData\Local\Temp\",
                @"\Users\Public\",
                @"\Windows\Temp\",
                @"\ProgramData\"
            };

            return suspiciousLocations.Any(loc =>
                filePath.Contains(loc, StringComparison.OrdinalIgnoreCase));
        }

        private async Task MonitorNetworkConnectionsAsync()
        {
            try
            {
                // Använd netstat för att få nätverksanslutningar
                var processInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines.Skip(2)) // Hoppa över headers
                {
                    await AnalyzeNetworkConnectionAsync(line);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Network monitoring error: {ex.Message}");
            }
        }

        private async Task AnalyzeNetworkConnectionAsync(string connectionLine)
        {
            try
            {
                var parts = connectionLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) return;

                var protocol = parts[0];
                var localAddress = parts[1];
                var foreignAddress = parts[2];
                var state = parts[3];

                if (!int.TryParse(parts[4], out var pid)) return;

                // Kolla efter suspekta portar och adresser
                if (IsSuspiciousConnection(foreignAddress, localAddress))
                {
                    var process = GetProcessById(pid);
                    if (process != null)
                    {
                        var securityEvent = new SecurityEvent
                        {
                            EventType = "SUSPICIOUS_NETWORK_CONNECTION",
                            Severity = SecuritySeverity.High,
                            Description = $"Suspekt nätverksanslutning till {foreignAddress}",
                            ProcessName = process.ProcessName,
                            ProcessId = pid,
                            NetworkDetails = $"{protocol} {localAddress} -> {foreignAddress} ({state})",
                            Timestamp = DateTime.Now
                        };

                        RecordSecurityEvent(securityEvent);

                        await HandleSuspiciousProcessAsync(process, "SUSPICIOUS_NETWORK_ACTIVITY");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Network connection analysis error: {ex.Message}");
            }
        }

        private bool IsSuspiciousConnection(string foreignAddress, string localAddress)
        {
            // Kolla efter kända mining pools och suspekta portar
            var suspiciousPorts = new[] { "4444", "3333", "8333", "9999", "14444", "5555" };
            var miningPoolIndicators = new[] { "pool", "mining", "stratum", "crypto" };

            // Kolla port
            if (suspiciousPorts.Any(port => foreignAddress.EndsWith($":{port}")))
                return true;

            // Kolla för mining pool-adresser
            if (miningPoolIndicators.Any(indicator =>
                foreignAddress.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Kolla för TOR-exit nodes eller onion-domäner
            if (foreignAddress.Contains(".onion") || IsKnownTorExitNode(foreignAddress))
                return true;

            return false;
        }

        private bool IsKnownTorExitNode(string address)
        {
            // Förenklad implementation - i verkligheten skulle detta kolla mot en databas
            // av kända TOR exit nodes
            return false;
        }

        private Process? GetProcessById(int pid)
        {
            try
            {
                return Process.GetProcessById(pid);
            }
            catch
            {
                return null;
            }
        }

        private async Task HandleSuspiciousProcessAsync(Process process, string threatType)
        {
            try
            {
                var processName = process.ProcessName;
                var processPath = process.MainModule?.FileName ?? "Okänd sökväg";

                // Räkna upp misstänkt aktivitet för denna process
                if (!_suspiciousActivityCounter.ContainsKey(processName))
                    _suspiciousActivityCounter[processName] = 0;

                _suspiciousActivityCounter[processName]++;
                _lastActivityTime[processName] = DateTime.Now;

                var activityCount = _suspiciousActivityCounter[processName];

                var securityEvent = new SecurityEvent
                {
                    EventType = threatType,
                    Severity = activityCount > 3 ? SecuritySeverity.Critical : SecuritySeverity.High,
                    Description = $"Misstänkt process upptäckt: {processName} (Aktivitet #{activityCount})",
                    ProcessName = processName,
                    ProcessId = process.Id,
                    FilePath = processPath,
                    Timestamp = DateTime.Now
                };

                RecordSecurityEvent(securityEvent);

                // Om det är känd malware eller upprepade intrång, blockera omedelbart
                if (threatType == "KNOWN_MALWARE_PROCESS" || activityCount >= 3)
                {
                    await BlockAndQuarantineProcessAsync(process, securityEvent);
                }

                // Trigger intrusion alert
                var intrusionArgs = new IntrusionDetectedEventArgs
                {
                    ThreatType = threatType,
                    ProcessName = processName,
                    ProcessPath = processPath,
                    Severity = securityEvent.Severity,
                    Description = securityEvent.Description,
                    ShouldBlock = activityCount >= 3 || threatType == "KNOWN_MALWARE_PROCESS"
                };

                IntrusionDetected?.Invoke(this, intrusionArgs);

                TotalThreatsDetected++;
                LastThreatTime = DateTime.Now;

                _logViewer.AddLogEntry(LogLevel.Error, "IDS",
                    $"🚨 INTRÅNG UPPTÄCKT: {processName} - {threatType} (#{activityCount})");

            }
            catch (Exception ex)
            {
                _logger.Warning($"Error handling suspicious process: {ex.Message}");
            }
        }

        private async Task BlockAndQuarantineProcessAsync(Process process, SecurityEvent securityEvent)
        {
            try
            {
                var processPath = process.MainModule?.FileName;

                _logViewer.AddLogEntry(LogLevel.Error, "IDS",
                    $"🛡️ BLOCKERAR HOTPROCESS: {process.ProcessName} (PID: {process.Id})");

                // 1. Avsluta processen omedelbart
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    TotalThreatsBlocked++;

                    _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                        $"✅ Process {process.ProcessName} avslutad framgångsrikt");
                }
                catch (Exception ex)
                {
                    _logViewer.AddLogEntry(LogLevel.Error, "IDS",
                        $"❌ Kunde inte avsluta process {process.ProcessName}: {ex.Message}");
                }

                // 2. Sätt filen i karantän om vi har sökvägen
                if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                {
                    try
                    {
                        var quarantineResult = await _quarantineManager.QuarantineFileAsync(
                            processPath,
                            $"Automatisk karantän - {securityEvent.EventType}",
                            securityEvent.Severity);

                        if (quarantineResult.Success)
                        {
                            _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                                $"🔒 Fil satt i karantän: {Path.GetFileName(processPath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logViewer.AddLogEntry(LogLevel.Warning, "IDS",
                            $"⚠️ Kunde inte sätta fil i karantän: {ex.Message}");
                    }
                }

                // 3. Rapportera framgångsrik blockering
                var alertArgs = new SecurityAlertEventArgs
                {
                    AlertType = "THREAT_BLOCKED",
                    Message = $"HOTPROCESS BLOCKERAD: {process.ProcessName}",
                    Severity = SecuritySeverity.Critical,
                    ProcessName = process.ProcessName,
                    ProcessPath = processPath,
                    ActionTaken = "Process avslutad och fil satt i karantän"
                };

                SecurityAlert?.Invoke(this, alertArgs);

            }
            catch (Exception ex)
            {
                _logViewer.AddLogEntry(LogLevel.Error, "IDS",
                    $"❌ Kritiskt fel vid blockering av hotprocess: {ex.Message}");
            }
        }

        private async Task PerformSystemSecurityScanAsync()
        {
            try
            {
                _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                    "🔍 Genomför periodisk säkerhetskontroll");

                // 1. Snabb temp-fil scan
                var tempResults = await _fileScanner.ScanTempDirectoriesAsync();
                var threats = tempResults.Where(r => r.ThreatLevel >= ThreatLevel.Medium).ToList();

                foreach (var threat in threats.ToList())
                {
                    var isTelegramBot = await DetectTelegramBotActivityAsync(threat.FilePath);
                    if (isTelegramBot)
                    {
                        // Telegram bot-hot hanteras redan i DetectTelegramBotActivityAsync
                        threats.Remove(threat);
                    }
                }

                foreach (var threat in threats)
                {
                    await HandleSecurityThreatAsync(threat, "PERIODIC_SCAN", "Periodisk säkerhetskontroll");
                }

                // 2. Kontrollera registry för nya startup-poster
                await CheckRegistryStartupChangesAsync();

                // 3. Kontrollera för nya nätverksanslutningar
                await CheckForNewNetworkConnectionsAsync();

                // 4. Rensa gamla aktivitetsräknare
                CleanupOldActivityCounters();

                var newThreats = threats.Count;
                if (newThreats > 0)
                {
                    _logViewer.AddLogEntry(LogLevel.Warning, "IDS",
                        $"⚠️ Periodisk kontroll: {newThreats} nya hot upptäckta");
                }
                else
                {
                    _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                        "✅ Periodisk kontroll: Inga nya hot upptäckta");
                }
            }
            catch (Exception ex)
            {
                _logViewer.AddLogEntry(LogLevel.Error, "IDS",
                    $"❌ Fel vid periodisk säkerhetskontroll: {ex.Message}");
            }
        }

        private async Task CheckRegistryStartupChangesAsync()
        {
            await Task.Yield();

            try
            {
                // Förenklad implementation - kontrollera vanliga startup-nycklar
                var startupKeys = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
                };

                foreach (var keyPath in startupKeys)
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
                        if (key != null)
                        {
                            var values = key.GetValueNames();
                            foreach (var valueName in values)
                            {
                                var value = key.GetValue(valueName)?.ToString();
                                if (!string.IsNullOrEmpty(value) && IsSuspiciousStartupEntry(value))
                                {
                                    var securityEvent = new SecurityEvent
                                    {
                                        EventType = "SUSPICIOUS_STARTUP_ENTRY",
                                        Severity = SecuritySeverity.High,
                                        Description = $"Suspekt startup-post upptäckt: {valueName} = {value}",
                                        RegistryKey = $"HKCU\\{keyPath}\\{valueName}",
                                        Timestamp = DateTime.Now
                                    };

                                    RecordSecurityEvent(securityEvent);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Registry check error for {keyPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Registry startup check error: {ex.Message}");
            }
        }

        private bool IsSuspiciousStartupEntry(string value)
        {
            var suspiciousPatterns = new[]
            {
                @"\temp\", @"\appdata\local\temp\", @"\users\public\",
                "powershell", "cmd.exe /c", "wscript", "cscript",
                ".tmp.exe", ".scr", ".pif", ".com"
            };

            return suspiciousPatterns.Any(pattern =>
                value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private async Task CheckForNewNetworkConnectionsAsync()
        {
            await Task.Delay(100); // Stub implementation
            // I en fullständig implementation skulle vi spara tidigare anslutningar
            // och jämföra med nuvarande för att upptäcka nya suspekta anslutningar
        }

        private void CleanupOldActivityCounters()
        {
            var cutoffTime = DateTime.Now.AddMinutes(-30);
            var keysToRemove = _lastActivityTime
                .Where(kvp => kvp.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _suspiciousActivityCounter.Remove(key);
                _lastActivityTime.Remove(key);
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

            // Auto-karantän för kritiska hot
            if (threat.ThreatLevel >= ThreatLevel.High)
            {
                try
                {
                    var quarantineResult = await _quarantineManager.QuarantineFileAsync(
                        threat.FilePath,
                        $"Automatisk karantän - {eventType}: {threat.Reason}",
                        threat.ThreatLevel);

                    if (quarantineResult.Success)
                    {
                        TotalThreatsBlocked++;
                        _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                            $"🔒 AUTO-KARANTÄN: {Path.GetFileName(threat.FilePath)}");
                    }
                }
                catch (Exception ex)
                {
                    _logViewer.AddLogEntry(LogLevel.Warning, "IDS",
                        $"⚠️ Kunde inte sätta fil i auto-karantän: {ex.Message}");
                }
            }

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

            _logViewer.AddLogEntry(logLevel, "IDS",
                $"🔍 {securityEvent.EventType}: {securityEvent.Description}");
        }

        private string GetProcessThatAccessedFile(string filePath)
        {
            try
            {
                // Förenklad implementation - i verkligheten skulle vi använda
                // Process Monitor API eller liknande för att spåra filåtkomst
                return "Okänd process";
            }
            catch
            {
                return "Okänd process";
            }
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
            StopMonitoringAsync().Wait();
            _cancellationTokenSource?.Dispose();

            foreach (var watcher in _fileWatchers)
            {
                watcher?.Dispose();
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