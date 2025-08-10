using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
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
        private readonly HashSet<string> _recentlyProcessed;
        private CancellationTokenSource _cancellationTokenSource = null!;

        // FÖRENKLAD: Endast kritiska hot som specificerat
        private readonly Dictionary<string, DateTime> _lastProcessCheck = new();
        private readonly TimeSpan _processCheckCooldown = TimeSpan.FromSeconds(30);
        private readonly object _processMonitorLock = new object();

        // Concurrent event processing
        private readonly ConcurrentQueue<SecurityEvent> _eventQueue = new();
        private readonly System.Timers.Timer _eventProcessingTimer;

        // ENDAST NirCmd/Screenshot/Telegram/Pastebin-regler
        private readonly HashSet<string> _criticalMalwareProcesses = new()
        {
            // NirCmd och screenshot-verktyg (primärt hot)
            "nircmd", "nirsoft", "screenshot", "screencapture", "printscreen",
            "capture", "snap", "grab", "screen", "desktop",
            
            // Remote Access verktyg (högt hot)
            "anydesk", "teamviewer", "vnc", "rdp", "logmein", "supremo",
            "ammyy", "luminance", "chrome_remote", "gotomypc", "bomgar",
            "splashtop", "ultravnc", "tightvnc", "realvnc",
            
            // Enkel crypto miner-heuristik
            "miner", "mining", "xmrig", "cpuminer", "cgminer", "nicehash"
        };

        // Telegram bot-specifika indicators (förstärkt)
        private readonly HashSet<string> _telegramBotIndicators = new()
        {
            "api.telegram.org/bot", "sendDocument", "savescreenshot", "nircmd.exe",
            "Screenshot_", "ScreenshotLog.txt", "telegram", "bot.exe",
            "grabber", "stealer", "clipper", "loader", "/sendPhoto", "/sendDocument",
            "bot[0-9]+:", "chat_id=", "Invoke-WebRequest.*telegram", "curl.*api.telegram.org"
        };

        // Pastebin och liknande tjänster
        private readonly HashSet<string> _suspiciousPasteSites = new()
        {
            "pastebin.com", "hastebin.com", "ghostbin.com", "controlc.com",
            "paste.ee", "dpaste.com", "justpaste.it", "rentry.co"
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

            _recentlyProcessed = new HashSet<string>();

            // Event processing timer - enklare än original
            _eventProcessingTimer = new System.Timers.Timer(2000); // Process events var 2:a sekund
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

        /// <summary>
        /// FÖRENKLAD: Process monitoring med endast kritiska hot
        /// </summary>
        private async Task MonitorCriticalProcessesAsync()
        {
            const int MaxProcessesPerScan = 200; // Reducerat från 500

            try
            {
                var processes = Process.GetProcesses()
                    .Take(MaxProcessesPerScan)
                    .Where(p => !p.HasExited)
                    .ToList();

                var processAnalysisTasks = new List<Task>();
                var semaphore = new SemaphoreSlim(2, 2); // Reducerat från ProcessorCount

                foreach (var process in processes)
                {
                    processAnalysisTasks.Add(AnalyzeCriticalProcessAsync(process, semaphore));
                }

                // Kortare timeout för snabbare scanning
                var allAnalysisTask = Task.WhenAll(processAnalysisTasks);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15)); // Reducerat från 30

                var completedTask = await Task.WhenAny(allAnalysisTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.Information("Process analysis timeout - optimerar prestanda");
                }

                // Cleanup
                foreach (var process in processes)
                {
                    try { process.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Critical process monitoring error: {ex.Message}");
            }
        }

        private async Task AnalyzeCriticalProcessAsync(Process process, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();

            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(3)); // Snabbare timeout

                await Task.Run(() => AnalyzeCriticalProcessInternal(process), cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout är OK för performance
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

        private void AnalyzeCriticalProcessInternal(Process process)
        {
            try
            {
                if (process.HasExited) return;

                var processName = process.ProcessName.ToLowerInvariant();

                // Rate limiting per process name
                if (!ShouldAnalyzeProcess(processName)) return;

                // PRIORITET 1: NirCmd och screenshot-verktyg (kritiskt)
                if (IsNirCmdOrScreenshotTool(processName, process))
                {
                    QueueSecurityEvent(new SecurityEvent
                    {
                        EventType = "NIRCMD_SCREENSHOT_DETECTED",
                        Severity = SecuritySeverity.Critical,
                        Description = $"🚨 KRITISKT: NirCmd/Screenshot-verktyg upptäckt: {processName}",
                        ProcessName = processName,
                        ProcessId = process.Id,
                        Timestamp = DateTime.Now
                    });
                    return;
                }

                // PRIORITET 2: Remote Access verktyg (högt)
                if (_criticalMalwareProcesses.Any(malware => processName.Contains(malware)))
                {
                    QueueSecurityEvent(new SecurityEvent
                    {
                        EventType = "REMOTE_ACCESS_TOOL_DETECTED",
                        Severity = SecuritySeverity.High,
                        Description = $"⚠️ Remote Access verktyg upptäckt: {processName}",
                        ProcessName = processName,
                        ProcessId = process.Id,
                        Timestamp = DateTime.Now
                    });
                    return;
                }

                // PRIORITET 3: Enkel crypto miner detection
                if (IsLikelyCryptoMining(process))
                {
                    QueueSecurityEvent(new SecurityEvent
                    {
                        EventType = "CRYPTO_MINING_DETECTED",
                        Severity = SecuritySeverity.Medium,
                        Description = $"⛏️ Möjlig crypto mining aktivitet: {processName}",
                        ProcessName = processName,
                        ProcessId = process.Id,
                        Timestamp = DateTime.Now
                    });
                    return;
                }

            }
            catch (Exception ex)
            {
                _logger.Debug($"Internal process analysis error: {ex.Message}");
            }
        }

        /// <summary>
        /// Specifik detection för NirCmd och screenshot-verktyg
        /// </summary>
        private bool IsNirCmdOrScreenshotTool(string processName, Process process)
        {
            try
            {
                // Direkta NirCmd-indikationer
                if (processName.Contains("nircmd") || processName.Contains("nirsoft"))
                    return true;

                // Screenshot-verktyg
                var screenshotIndicators = new[] { "screenshot", "screencap", "capture", "snap", "grab" };
                if (screenshotIndicators.Any(indicator => processName.Contains(indicator)))
                    return true;

                // Kontrollera command line arguments för screenshot-kommandon
                try
                {
                    var commandLine = GetProcessCommandLine(process);
                    if (commandLine != null)
                    {
                        var suspiciousCommands = new[] { "savescreenshot", "screenshot", "printscreen", "capture" };
                        if (suspiciousCommands.Any(cmd => commandLine.Contains(cmd, StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                }
                catch { }

                // Kontrollera om processen körs från temp-mapp (suspekt)
                try
                {
                    var mainModule = process.MainModule;
                    if (mainModule?.FileName != null)
                    {
                        var filePath = mainModule.FileName.ToLowerInvariant();
                        var tempPaths = new[] { @"\temp\", @"\appdata\local\temp\", @"\windows\temp\" };

                        if (tempPaths.Any(path => filePath.Contains(path)))
                            return true;
                    }
                }
                catch { }

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
                var processName = process.ProcessName.ToLowerInvariant();
                var miningKeywords = new[] { "miner", "mining", "crypto", "xmr", "btc", "eth", "monero", "nicehash" };

                if (miningKeywords.Any(keyword => processName.Contains(keyword)))
                    return true;

                // Hög CPU-användning kan indikera mining (förenklad check)
                try
                {
                    // Enkel heuristik baserad på processnamn och lokation
                    var mainModule = process.MainModule;
                    if (mainModule?.FileName != null)
                    {
                        var filePath = mainModule.FileName.ToLowerInvariant();
                        if (filePath.Contains(@"\appdata\") && miningKeywords.Any(k => filePath.Contains(k)))
                            return true;
                    }
                }
                catch { }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string? GetProcessCommandLine(Process process)
        {
            try
            {
                // Förenklad command line extraction
                return process.StartInfo?.Arguments;
            }
            catch
            {
                return null;
            }
        }

        private void QueueSecurityEvent(SecurityEvent securityEvent)
        {
            _eventQueue.Enqueue(securityEvent);

            // Begränsa kö-storlek för performance
            while (_eventQueue.Count > 100)
            {
                _eventQueue.TryDequeue(out _);
            }
        }

        private void ProcessSecurityEvents()
        {
            const int MaxEventsPerBatch = 5; // Reducerat för prestanda
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
            // Logga händelsen
            var logLevel = securityEvent.Severity switch
            {
                SecuritySeverity.Critical => LogLevel.Error,
                SecuritySeverity.High => LogLevel.Warning,
                SecuritySeverity.Medium => LogLevel.Information,
                _ => LogLevel.Debug
            };

            _logViewer.AddLogEntry(logLevel, "IDS", $"🔍 {securityEvent.EventType}: {securityEvent.Description}");

            TotalThreatsDetected++;
            LastThreatTime = DateTime.Now;

            if (securityEvent.Severity >= SecuritySeverity.High)
            {
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

                // Endast process-övervakning för förenklad version
                await StartCriticalProcessMonitoringAsync();

                // Periodisk systemkontroll (förenklad)
                StartPeriodicSystemCheck();

                IsMonitoringActive = true;

                _logger.Information("🔒 Förenklad Intrusion Detection AKTIVERAT - endast kritiska hot");
                _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                    "🔒 FÖRENKLAD IDS AKTIVERAT - NirCmd/RA-verktyg/Crypto mining detection");

                // Första genomgång direkt
                _ = Task.Run(async () => await PerformCriticalSecurityScanAsync());
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

                IsMonitoringActive = false;

                _logger.Information("Intrusion Detection System inaktiverat");
                _logViewer.AddLogEntry(LogLevel.Warning, "IDS",
                    "⚠️ INTRUSION DETECTION INAKTIVERAT");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid stopp av intrusion detection: {ex.Message}");
            }
        }

        private async Task StartCriticalProcessMonitoringAsync()
        {
            _ = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await MonitorCriticalProcessesAsync();
                        await Task.Delay(10000, _cancellationTokenSource.Token); // Var 10:e sekund
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Critical process monitoring error: {ex.Message}");
                        await Task.Delay(15000, _cancellationTokenSource.Token);
                    }
                }
            });

            _logViewer.AddLogEntry(LogLevel.Information, "IDS", "🔍 Kritisk process-övervakning aktiverad");
            await Task.Yield();
        }

        private void StartPeriodicSystemCheck()
        {
            _monitoringTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds); // Var 5:e minut
            _monitoringTimer.Elapsed += async (sender, e) => await PerformCriticalSecurityScanAsync();
            _monitoringTimer.AutoReset = true;
            _monitoringTimer.Start();
        }

        private async Task PerformCriticalSecurityScanAsync()
        {
            try
            {
                _logViewer.AddLogEntry(LogLevel.Information, "IDS", "🔍 Genomför kritisk säkerhetskontroll");

                // Fokuserad temp-fil scan för endast kritiska hot
                var tempResults = await _fileScanner.ScanTempDirectoriesAsync();
                var criticalThreats = tempResults?.Where(r =>
                    r.ThreatLevel >= ThreatLevel.Medium &&
                    IsCriticalThreat(r)).ToList() ?? new List<ScanResult>();

                foreach (var threat in criticalThreats)
                {
                    await HandleCriticalThreatAsync(threat);
                }

                if (criticalThreats.Any())
                {
                    _logViewer.AddLogEntry(LogLevel.Warning, "IDS",
                        $"⚠️ Kritisk kontroll: {criticalThreats.Count} kritiska hot upptäckta");
                }
                else
                {
                    _logViewer.AddLogEntry(LogLevel.Information, "IDS",
                        "✅ Kritisk kontroll: Inga kritiska hot upptäckta");
                }
            }
            catch (Exception ex)
            {
                _logViewer.AddLogEntry(LogLevel.Error, "IDS",
                    $"❌ Fel vid kritisk säkerhetskontroll: {ex.Message}");
            }
        }

        private bool IsCriticalThreat(ScanResult scanResult)
        {
            var fileName = scanResult.FileName.ToLowerInvariant();
            var filePath = scanResult.FilePath.ToLowerInvariant();

            // Kontrollera mot våra kritiska indikatorer
            var criticalIndicators = new[] { "nircmd", "screenshot", "telegram", "bot", "grabber", "stealer" };

            return criticalIndicators.Any(indicator =>
                fileName.Contains(indicator) || filePath.Contains(indicator));
        }

        private async Task HandleCriticalThreatAsync(ScanResult threat)
        {
            var securityEvent = new SecurityEvent
            {
                EventType = "CRITICAL_FILE_THREAT",
                Severity = threat.ThreatLevel switch
                {
                    ThreatLevel.Critical => SecuritySeverity.Critical,
                    ThreatLevel.High => SecuritySeverity.High,
                    _ => SecuritySeverity.Medium
                },
                Description = $"Kritisk fil-hot: {threat.Reason}",
                FilePath = threat.FilePath,
                FileHash = threat.FileHash,
                Timestamp = DateTime.Now
            };

            ProcessSecurityEventInternal(securityEvent);
        }

        public void Dispose()
        {
            try
            {
                StopMonitoringAsync().Wait(3000);
                _cancellationTokenSource?.Dispose();
                _eventProcessingTimer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error disposing IntrusionDetectionService: {ex.Message}");
            }
        }
    }

    // Event Arguments (oförändrade)
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

    // Models (oförändrade)
    public class SecurityEvent
    {
        public string EventType { get; set; } = string.Empty;
        public SecuritySeverity Severity { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? FilePath { get; set; }
        public string? FileHash { get; set; }
        public string? ProcessName { get; set; }
        public int? ProcessId { get; set; }
        public DateTime Timestamp { get; set; }
    }
}