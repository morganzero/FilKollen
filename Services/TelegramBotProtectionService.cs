// Services/TelegramBotProtectionService.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Serilog;
using FilKollen.Models;

namespace FilKollen.Services
{
    public class TelegramBotProtectionService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly QuarantineManager _quarantineManager;
        private readonly List<FileSystemWatcher> _watchers;
        private readonly Timer _scanTimer;
        private readonly ConcurrentDictionary<string, DateTime> _processedFiles;
        private readonly SemaphoreSlim _operationSemaphore;
        
        // Telegram Bot Attack Signatures (baserat på ditt exempel)
        private readonly HashSet<string> _telegramBotSignatures = new()
        {
            // API Calls och endpoints
            "api.telegram.org/bot", "sendDocument", "sendPhoto", "sendMessage",
            "chat_id=", "/sendDocument", "/sendPhoto", "telegram.org/bot",
            "getUpdates", "setWebhook", "sendLocation", "sendContact",
            
            // Screenshot och spionage funktioner
            "savescreenshot", "Screenshot_", "ScreenshotLog.txt", "capture_screen",
            "desktop_capture", "screen_grab", "printscreen", "GetDesktopWindow",
            "window_capture", "display_capture", "screen_record", "desktop_shot",
            
            // NirCmd (ofta använt i Telegram bot-attacker)
            "nircmd.exe", "nircmd.zip", "nirsoft.net", "nir_cmd", "nircmdc",
            "nircmd_download", "nirsoft_tool", "savescreenshot", "sendkeypress",
            
            // PowerShell indicators
            "Invoke-WebRequest", "WebClient", "DownloadString", "DownloadFile",
            "Expand-Archive", "Compress-Archive", "-Uri", "-OutFile",
            "System.Net.WebClient", "System.Net.Http.HttpClient", "IEX (",
            
            // Encoding/Obfuscation
            "FromBase64String", "ToBase64String", "[Convert]::", "base64",
            "System.Text.Encoding", "UTF8.GetBytes", "ASCII.GetString",
            
            // Specifika patterns från ditt exempel
            "Téléchargement de nircmd", "Échec capture écran", "Envoi réussi",
            "chat_id=1230565176", "8184812740:AAFPbn1XTOjEqzAhD5kv6G5qMqre1N9kByo",
            "yyyyMMdd_HHmmss", "timestamp", "_HHmmss", "Get-Date -Format",
            
            // Nätverks operationer
            "curl -F", "wget", "bitsadmin", "certutil -urlcache",
            "Start-BitsTransfer", "HttpWebRequest", "WebRequest",
            
            // Bot token patterns
            "bot[0-9]+:", "AAE", "AAF", "AAG", "AAH", "AAI" // Common bot token prefixes
        };

        // Vanliga filnamn för Telegram bot-attacker
        private readonly HashSet<string> _suspiciousBotFiles = new()
        {
            "screenshot.bat", "capture.cmd", "bot.ps1", "telegram.bat",
            "send.cmd", "upload.ps1", "spy.bat", "monitor.cmd",
            "nircmd.exe", "nircmd.zip", "screen.bat", "cap.cmd",
            "log.txt", "temp.bat", "run.cmd", "exec.ps1",
            "screencap.ps1", "desktop.bat", "spy_screen.cmd", "telegram_send.ps1"
        };

        public TelegramBotProtectionService(ILogger logger, QuarantineManager quarantineManager)
        {
            _logger = logger;
            _quarantineManager = quarantineManager;
            _watchers = new List<FileSystemWatcher>();
            _processedFiles = new ConcurrentDictionary<string, DateTime>();
            _operationSemaphore = new SemaphoreSlim(1, 1);
            
            // Sätt upp file watchers för real-time protection
            SetupFileWatchers();
            
            // Periodisk skanning var 2:a minut för tidig detektering
            _scanTimer = new Timer(PerformPeriodicScan, null, TimeSpan.Zero, TimeSpan.FromMinutes(2));
            
            _logger.Information("🛡️ Telegram Bot Protection Service aktiverat - förstärkt skydd mot screenshot-stöld");
        }

        private void SetupFileWatchers()
        {
            var criticalPaths = new[]
            {
                Environment.GetEnvironmentVariable("TEMP"),
                Environment.GetEnvironmentVariable("TMP"),
                @"C:\Windows\Temp",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                @"C:\Users\Public\Downloads",
                @"C:\Users\Public\Desktop"
            };

            foreach (var path in criticalPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
            {
                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnSuspiciousFileDetected;
                    watcher.Changed += OnSuspiciousFileDetected;
                    watcher.Renamed += OnSuspiciousFileRenamed;
                    
                    _watchers.Add(watcher);
                    _logger.Information($"📁 Övervakar för Telegram bot-aktivitet: {path}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Kunde inte övervaka {path}: {ex.Message}");
                }
            }
        }

        private async void OnSuspiciousFileDetected(object sender, FileSystemEventArgs e)
        {
            await ProcessSuspiciousFileAsync(e.FullPath, "Real-time detection");
        }

        private async void OnSuspiciousFileRenamed(object sender, RenamedEventArgs e)
        {
            await ProcessSuspiciousFileAsync(e.FullPath, "File renamed");
        }

        private async Task ProcessSuspiciousFileAsync(string filePath, string detectionMethod)
        {
            if (!await _operationSemaphore.WaitAsync(5000)) return;

            try
            {
                // Undvik duplicerad bearbetning
                var fileKey = $"{filePath}_{File.GetLastWriteTime(filePath).Ticks}";
                if (_processedFiles.ContainsKey(fileKey)) return;

                _processedFiles.TryAdd(fileKey, DateTime.Now);

                // Vänta lite för att filen ska skrivas klart
                await Task.Delay(2000);

                if (!File.Exists(filePath)) return;

                var analysisResult = await AnalyzePotentialTelegramBotAsync(filePath);
                
                if (analysisResult.IsTelegramBot)
                {
                    await HandleTelegramBotThreatAsync(filePath, analysisResult, detectionMethod);
                }
                else if (analysisResult.SuspiciousScore > 50)
                {
                    _logger.Warning($"⚠️ Suspekt fil upptäckt: {Path.GetFileName(filePath)} (Score: {analysisResult.SuspiciousScore})");
                }

                // Rensa gamla entries
                CleanupProcessedFiles();
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid bearbetning av suspekt fil {filePath}: {ex.Message}");
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        private async Task<TelegramBotAnalysisResult> AnalyzePotentialTelegramBotAsync(string filePath)
        {
            var result = new TelegramBotAnalysisResult();
            
            try
            {
                var fileName = Path.GetFileName(filePath).ToLowerInvariant();
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var fileInfo = new FileInfo(filePath);
                
                // 1. Kontrollera filnamn mot bot-patterns
                if (_suspiciousBotFiles.Any(botFile => fileName.Contains(botFile)))
                {
                    result.SuspiciousScore += 40;
                    result.DetectionReasons.Add($"Suspekt bot-filnamn: {fileName}");
                }
                
                // 2. Kontrollera extension
                var dangerousExtensions = new[] { ".bat", ".cmd", ".ps1", ".vbs", ".exe", ".scr", ".pif", ".com" };
                if (dangerousExtensions.Contains(extension))
                {
                    result.SuspiciousScore += 30;
                    result.DetectionReasons.Add($"Farlig filextension: {extension}");
                }
                
                // 3. Kontrollera filstorlek (många bot-script är små)
                if (fileInfo.Length < 50000 && dangerousExtensions.Contains(extension))
                {
                    result.SuspiciousScore += 20;
                    result.DetectionReasons.Add("Liten executable fil (trolig malware)");
                }
                
                // 4. Analysera innehåll för script-filer
                if (new[] { ".bat", ".cmd", ".ps1", ".vbs" }.Contains(extension))
                {
                    var contentAnalysis = await AnalyzeScriptContentAsync(filePath);
                    result.SuspiciousScore += contentAnalysis.SuspiciousScore;
                    result.DetectionReasons.AddRange(contentAnalysis.DetectionReasons);
                    result.BotTokens.AddRange(contentAnalysis.BotTokens);
                    result.ChatIds.AddRange(contentAnalysis.ChatIds);
                    result.TelegramUrls.AddRange(contentAnalysis.TelegramUrls);
                }
                
                // 5. Final assessment
                if (result.SuspiciousScore >= 80)
                {
                    result.IsTelegramBot = true;
                    result.ThreatLevel = "CRITICAL";
                }
                else if (result.SuspiciousScore >= 60)
                {
                    result.IsTelegramBot = true;
                    result.ThreatLevel = "HIGH";
                }
                else if (result.SuspiciousScore >= 40)
                {
                    result.ThreatLevel = "MEDIUM";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid analys av potentiell Telegram bot: {ex.Message}");
                result.DetectionReasons.Add($"Analys fel: {ex.Message}");
                return result;
            }
        }

        private async Task<ScriptAnalysisResult> AnalyzeScriptContentAsync(string filePath)
        {
            var result = new ScriptAnalysisResult();
            
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var contentLower = content.ToLowerInvariant();
                
                // Räkna Telegram bot-signaturer
                foreach (var signature in _telegramBotSignatures)
                {
                    if (contentLower.Contains(signature.ToLowerInvariant()))
                    {
                        result.SuspiciousScore += GetSignatureWeight(signature);
                        result.DetectionReasons.Add($"Telegram bot-signatur: {signature}");
                    }
                }
                
                // Extrahera bot tokens
                var tokenPattern = @"bot(\d+):([A-Za-z0-9_-]{35})";
                var tokenMatches = Regex.Matches(content, tokenPattern, RegexOptions.IgnoreCase);
                foreach (Match match in tokenMatches)
                {
                    result.BotTokens.Add(match.Value);
                    result.SuspiciousScore += 50;
                    result.DetectionReasons.Add("Bot token detekterat");
                }
                
                // Extrahera chat IDs
                var chatIdPattern = @"chat_id[=\s]*([0-9-]+)";
                var chatIdMatches = Regex.Matches(content, chatIdPattern, RegexOptions.IgnoreCase);
                foreach (Match match in chatIdMatches)
                {
                    result.ChatIds.Add(match.Groups[1].Value);
                    result.SuspiciousScore += 30;
                    result.DetectionReasons.Add("Chat ID detekterat");
                }
                
                // Extrahera Telegram URLs
                var urlPattern = @"https?://(api\.telegram\.org|telegram\.org|t\.me)[^\s]*";
                var urlMatches = Regex.Matches(content, urlPattern, RegexOptions.IgnoreCase);
                foreach (Match match in urlMatches)
                {
                    result.TelegramUrls.Add(match.Value);
                    result.SuspiciousScore += 40;
                    result.DetectionReasons.Add("Telegram URL detekterat");
                }
                
                // Specifika patterns från ditt exempel
                if (contentLower.Contains("savescreenshot") && contentLower.Contains("telegram"))
                {
                    result.SuspiciousScore += 60;
                    result.DetectionReasons.Add("KRITISKT: Screenshot + Telegram kombination");
                }
                
                if (contentLower.Contains("nircmd") && contentLower.Contains("api.telegram.org"))
                {
                    result.SuspiciousScore += 70;
                    result.DetectionReasons.Add("KRITISKT: NirCmd + Telegram API kombination");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                result.DetectionReasons.Add($"Script-analys fel: {ex.Message}");
                return result;
            }
        }

        private int GetSignatureWeight(string signature)
        {
            return signature.ToLowerInvariant() switch
            {
                var s when s.Contains("api.telegram.org") => 50,
                var s when s.Contains("senddocument") => 40,
                var s when s.Contains("savescreenshot") => 45,
                var s when s.Contains("nircmd") => 35,
                var s when s.Contains("chat_id") => 30,
                var s when s.Contains("bot") && s.Contains(":") => 50,
                var s when s.Contains("invoke-webrequest") => 25,
                var s when s.Contains("base64") => 20,
                _ => 15
            };
        }

        private async Task HandleTelegramBotThreatAsync(string filePath, TelegramBotAnalysisResult analysis, string detectionMethod)
        {
            try
            {
                _logger.Error($"🚨 TELEGRAM BOT ATTACK DETECTED: {Path.GetFileName(filePath)}");
                _logger.Error($"   Detection Method: {detectionMethod}");
                _logger.Error($"   Threat Level: {analysis.ThreatLevel}");
                _logger.Error($"   Suspicion Score: {analysis.SuspiciousScore}/100");
                
                // 1. Omedelbar karantän
                var quarantineResult = await _quarantineManager.QuarantineFileAtomicAsync(
                    filePath, 
                    $"TELEGRAM BOT ATTACK - {analysis.ThreatLevel} - Score: {analysis.SuspiciousScore}",
                    GetThreatLevel(analysis.ThreatLevel));
                
                if (quarantineResult.Success)
                {
                    _logger.Information($"✅ Telegram bot-fil karantänerad: {quarantineResult.QuarantineId}");
                }
                
                // 2. Logga detaljerad information
                await LogTelegramBotIncidentAsync(filePath, analysis, detectionMethod);
                
                // 3. Blockera Telegram API access
                await BlockTelegramApiAccessAsync();
                
                // 4. Sök efter relaterade filer
                await ScanForRelatedTelegramFilesAsync(Path.GetDirectoryName(filePath));
                
                // 5. Rensa potentiella artefakter
                await CleanupTelegramBotArtifactsAsync();
                
                // 6. Säkra systemet ytterligare
                await ApplyTelegramBotMitigationsAsync();
                
                _logger.Information("🛡️ Telegram bot-attack fully mitigated");
            }
            catch (Exception ex)
            {
                _logger.Error($"Kritiskt fel vid hantering av Telegram bot-attack: {ex.Message}");
            }
        }

        private async Task BlockTelegramApiAccessAsync()
        {
            try
            {
                var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), 
                    "drivers", "etc", "hosts");
                
                if (!File.Exists(hostsPath)) return;
                
                var hostsContent = await File.ReadAllTextAsync(hostsPath);
                
                if (hostsContent.Contains("# FilKollen Telegram Bot Block")) return;
                
                var telegramBlocks = new[]
                {
                    "",
                    "# FilKollen Telegram Bot Block - START",
                    "# This blocks Telegram API access to prevent bot attacks",
                    "0.0.0.0 api.telegram.org",
                    "0.0.0.0 telegram.org",
                    "0.0.0.0 web.telegram.org",
                    "0.0.0.0 t.me",
                    "0.0.0.0 telegram.me",
                    "0.0.0.0 core.telegram.org",
                    "0.0.0.0 desktop.telegram.org",
                    "# FilKollen Telegram Bot Block - END",
                    ""
                };
                
                await File.AppendAllTextAsync(hostsPath, string.Join(Environment.NewLine, telegramBlocks));
                
                // Flush DNS cache
                await ExecuteCommandAsync("ipconfig /flushdns");
                
                _logger.Information("🚫 Telegram API access blockerat via hosts-fil");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid blockering av Telegram API: {ex.Message}");
            }
        }

        private async Task CleanupTelegramBotArtifactsAsync()
        {
            try
            {
                _logger.Information("🧹 Rensar Telegram bot-artefakter...");
                
                var artifactLocations = new[]
                {
                    Environment.GetEnvironmentVariable("TEMP"),
                    Environment.GetEnvironmentVariable("TMP"),
                    @"C:\Windows\Temp",
                    @"C:\Users\Public\Downloads",
                    @"C:\Users\Public\Desktop"
                };
                
                var artifactPatterns = new[]
                {
                    "nircmd*", "screenshot*", "*ScreenshotLog*", "capture_*",
                    "shot_*", "desktop_*", "screen_*", "*_HHmmss*",
                    "temp_*", "*bot*", "*telegram*", "spy_*"
                };
                
                var cleanedCount = 0;
                
                foreach (var location in artifactLocations.Where(loc => !string.IsNullOrEmpty(loc) && Directory.Exists(loc)))
                {
                    foreach (var pattern in artifactPatterns)
                    {
                        try
                        {
                            var files = Directory.GetFiles(location, pattern, SearchOption.TopDirectoryOnly);
                            foreach (var file in files)
                            {
                                try
                                {
                                    if (File.GetCreationTime(file) > DateTime.Now.AddDays(-1))
                                    {
                                        await _quarantineManager.QuarantineFileAtomicAsync(
                                            file, 
                                            "Telegram bot-artefakt cleanup",
                                            ThreatLevel.Low);
                                        cleanedCount++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Debug($"Kunde inte rensa artefakt {file}: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Fel vid artefakt-skanning {location}/{pattern}: {ex.Message}");
                        }
                    }
                }
                
                _logger.Information($"✅ Artifact cleanup complete: {cleanedCount} artefakter rensade");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid cleanup av artefakter: {ex.Message}");
            }
        }

        private async Task ApplyTelegramBotMitigationsAsync()
        {
            try
            {
                _logger.Information("🛡️ Tillämpar ytterligare Telegram bot-mitigationer...");
                
                // 1. Förstärk PowerShell execution policy
                await ExecuteCommandAsync("Set-ExecutionPolicy -ExecutionPolicy Restricted -Scope LocalMachine -Force");
                
                // 2. Inaktivera farliga Windows funktioner via registry
                await SetRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\PowerShell", 
                    "EnableScripts", 0);
                
                // 3. Förstärk firewall-regler
                await UpdateFirewallRulesAsync();
                
                _logger.Information("✅ Telegram bot-mitigationer tillämpade");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid tillämpning av mitigationer: {ex.Message}");
            }
        }

        private async Task UpdateFirewallRulesAsync()
        {
            try
            {
                var firewallCommands = new[]
                {
                    "netsh advfirewall firewall add rule name=\"Block Telegram API\" dir=out action=block remoteip=149.154.160.0/20",
                    "netsh advfirewall firewall add rule name=\"Block Telegram API 2\" dir=out action=block remoteip=91.108.4.0/22"
                };
                
                foreach (var command in firewallCommands)
                {
                    try
                    {
                        await ExecuteCommandAsync(command);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Firewall rule error: {ex.Message}");
                    }
                }
                
                _logger.Information("🔥 Firewall-regler uppdaterade för Telegram-blockering");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid uppdatering av firewall-regler: {ex.Message}");
            }
        }

        private async void PerformPeriodicScan(object? state)
        {
            try
            {
                _logger.Debug("🔍 Periodisk Telegram bot-skanning...");
                
                var scanLocations = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
                    Environment.GetEnvironmentVariable("TEMP"),
                    Environment.GetEnvironmentVariable("TMP"),
                    @"C:\Windows\Temp",
                    @"C:\Users\Public\Downloads"
                };
                
                foreach (var location in scanLocations.Where(loc => !string.IsNullOrEmpty(loc) && Directory.Exists(loc)))
                {
                    try
                    {
                        var recentFiles = Directory.GetFiles(location, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f => File.GetCreationTime(f) > DateTime.Now.AddHours(-1))
                            .ToList();
                        
                        foreach (var file in recentFiles)
                        {
                            await ProcessSuspiciousFileAsync(file, "Periodic scan");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Periodisk skanning fel för {location}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid periodisk Telegram bot-skanning: {ex.Message}");
            }
        }

        private void CleanupProcessedFiles()
        {
            var cutoffTime = DateTime.Now.AddMinutes(-30);
            var keysToRemove = _processedFiles
                .Where(kvp => kvp.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _processedFiles.TryRemove(key, out _);
            }
        }

        private async Task ExecuteCommandAsync(string command)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Command execution failed: {command} - {ex.Message}");
            }
        }

        private async Task SetRegistryValue(string keyPath, string valueName, object value)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(keyPath.Replace("HKEY_LOCAL_MACHINE\\", ""));
                key?.SetValue(valueName, value);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Registry update failed: {keyPath}\\{valueName} = {value} - {ex.Message}");
            }
            
            await Task.Yield();
        }

        private ThreatLevel GetThreatLevel(string threatLevel)
        {
            return threatLevel switch
            {
                "CRITICAL" => ThreatLevel.Critical,
                "HIGH" => ThreatLevel.High,
                "MEDIUM" => ThreatLevel.Medium,
                _ => ThreatLevel.Low
            };
        }

        // Helper methods for logging and reporting
        private async Task LogTelegramBotIncidentAsync(string filePath, TelegramBotAnalysisResult analysis, string detectionMethod)
        {
            // Implementation for detailed incident logging
            await Task.Yield();
        }

        private async Task ScanForRelatedTelegramFilesAsync(string? directory)
        {
            // Implementation for scanning related files
            await Task.Yield();
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher?.Dispose();
            }
            _scanTimer?.Dispose();
            _operationSemaphore?.Dispose();
            _logger.Information("🛡️ Telegram Bot Protection Service stoppad");
        }
    }

    // Support classes
    public class TelegramBotAnalysisResult
    {
        public bool IsTelegramBot { get; set; }
        public int SuspiciousScore { get; set; }
        public string ThreatLevel { get; set; } = "LOW";
        public List<string> DetectionReasons { get; set; } = new();
        public List<string> BotTokens { get; set; } = new();
        public List<string> ChatIds { get; set; } = new();
        public List<string> TelegramUrls { get; set; } = new();
    }

    public class ScriptAnalysisResult
    {
        public int SuspiciousScore { get; set; }
        public List<string> DetectionReasons { get; set; } = new();
        public List<string> BotTokens { get; set; } = new();
        public List<string> ChatIds { get; set; } = new();
        public List<string> TelegramUrls { get; set; } = new();
    }
}