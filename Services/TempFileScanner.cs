// Services/EnhancedTempFileScanner.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class EnhancedTempFileScanner : TempFileScanner
    {
        private readonly SemaphoreSlim _scanSemaphore;
        private readonly ConcurrentDictionary<string, DateTime> _scanCache;
        private readonly object _lockObject = new object();
        
        // Förbättrade malware signaturer baserat på ditt intrång
        private readonly Dictionary<string, int> _malwareSignatures = new()
        {
            // Telegram bot-specifika signaturer (HÖJD prioritet efter ditt intrång)
            ["api.telegram.org/bot"] = 95,
            ["sendDocument"] = 85,
            ["savescreenshot"] = 90,
            ["nircmd.exe"] = 88,
            ["Screenshot_"] = 85,
            ["ScreenshotLog.txt"] = 87,
            ["Invoke-WebRequest"] = 75,
            ["curl -F"] = 80,
            ["chat_id="] = 82,
            
            // Kända hackerverktyg
            ["psexec.exe"] = 95,
            ["mimikatz"] = 98,
            ["netcat"] = 85,
            ["nmap"] = 80,
            ["metasploit"] = 95,
            ["burpsuite"] = 78,
            ["sqlmap"] = 85,
            ["aircrack"] = 88,
            ["hashcat"] = 82,
            ["john.exe"] = 85,
            
            // Remote access verktyg
            ["teamviewer"] = 70,
            ["anydesk"] = 70,
            ["vnc"] = 75,
            ["putty"] = 60,
            
            // Cryptocurrency miners
            ["xmrig"] = 92,
            ["miner.exe"] = 88,
            ["cpuminer"] = 85,
            ["cgminer"] = 85,
            ["nicehash"] = 90,
            ["monero"] = 80,
            ["stratum"] = 82,
            
            // Script patterns
            ["powershell -enc"] = 85,
            ["powershell -e"] = 85,
            ["cmd /c"] = 70,
            ["wscript"] = 75,
            ["cscript"] = 75,
            ["rundll32"] = 70,
            ["regsvr32"] = 72,
            ["mshta"] = 78,
            
            // Suspekta nätverks patterns
            ["bit.ly/"] = 60,
            ["tinyurl.com/"] = 60,
            ["pastebin.com/"] = 65,
            ["hastebin.com/"] = 65,
            ["mega.nz/"] = 55,
            ["discord.gg/"] = 50,
            
            // Obfuscation patterns
            ["base64"] = 50,
            ["FromBase64String"] = 70,
            ["ToBase64String"] = 65,
            ["[Convert]::"] = 65,
            ["System.Text.Encoding"] = 60,
            
            // Persistence mechanisms
            ["HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run"] = 80,
            ["HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run"] = 75,
            ["schtasks"] = 70,
            ["at.exe"] = 75
        };

        // Extensionlösa filer som ofta är malware
        private readonly string[] _suspiciousExtensionlessPatterns = 
        {
            @"^[a-f0-9]{8,32}$",  // Hex strings
            @"^\d{8,}$",          // Long number strings
            @"^[A-Za-z0-9+/=]{16,}$", // Base64-like
            @"^temp\d+$",         // temp123 patterns
            @"^[a-z]{1,3}\d{3,}$" // abc123 patterns
        };

        public EnhancedTempFileScanner(AppConfig config, ILogger logger) 
            : base(config, logger)
        {
            _scanSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
            _scanCache = new ConcurrentDictionary<string, DateTime>();
            
            // Starta cache cleanup timer
            var cleanupTimer = new Timer(CleanupScanCache, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task<List<ScanResult>> ScanTempDirectoriesRobustAsync()
        {
            var results = new List<ScanResult>();
            var failedPaths = new List<string>();

            _logger.Information("🔍 Startar robust säkerhetsskanning av temp-kataloger...");

            try
            {
                // Utökad lista av temp-sökvägar
                var tempPaths = GetExtendedTempPaths();
                var totalPaths = tempPaths.Count;
                var completedPaths = 0;

                // Parallell skanning med semaphore för att kontrollera resurser
                var scanTasks = tempPaths.Select(async path =>
                {
                    await _scanSemaphore.WaitAsync();
                    try
                    {
                        var pathResults = await ScanDirectoryRobustAsync(path);
                        
                        lock (_lockObject)
                        {
                            results.AddRange(pathResults);
                            completedPaths++;
                            
                            if (completedPaths % 5 == 0) // Progress every 5 paths
                            {
                                _logger.Information($"📊 Skanning progress: {completedPaths}/{totalPaths} sökvägar");
                            }
                        }
                        
                        return pathResults;
                    }
                    catch (Exception ex)
                    {
                        lock (_lockObject)
                        {
                            failedPaths.Add(path);
                            _logger.Warning($"❌ Misslyckades skanna {path}: {ex.Message}");
                        }
                        return new List<ScanResult>();
                    }
                    finally
                    {
                        _scanSemaphore.Release();
                    }
                });

                // Vänta på alla scan tasks med timeout
                var allTasks = Task.WhenAll(scanTasks);
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(10));
                
                var completedTask = await Task.WhenAny(allTasks, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _logger.Warning("⏰ Skanning timeout - avbryter kvarvarande operationer");
                }

                _logger.Information($"✅ Robust skanning slutförd. Hittade {results.Count} suspekta filer.");
                
                if (failedPaths.Any())
                {
                    _logger.Warning($"⚠️ Kunde inte skanna {failedPaths.Count} sökvägar: {string.Join(", ", failedPaths)}");
                }

                return results.OrderByDescending(r => r.ThreatLevel)
                             .ThenByDescending(r => GetThreatScore(r))
                             .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Kritiskt fel vid robust skanning: {ex.Message}");
                return results;
            }
        }

        private List<string> GetExtendedTempPaths()
        {
            var paths = new List<string>();
            
            // Standard temp-paths
            var standardPaths = new[]
            {
                Environment.GetEnvironmentVariable("TEMP"),
                Environment.GetEnvironmentVariable("TMP"),
                @"C:\Windows\Temp",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Temp"),
                
                // Användarspecifika downloads och desktop
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                @"C:\Users\Public\Downloads",
                @"C:\Users\Public\Desktop",
                @"C:\Users\Public\Documents",
                
                // System temp locations
                @"C:\Windows\System32\config\systemprofile\AppData\Local\Temp",
                @"C:\Windows\ServiceProfiles\LocalService\AppData\Local\Temp",
                @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Temp",
                
                // Browser temp/cache (ofta används för malware)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Mozilla\Firefox\Profiles"),
                
                // Office och Adobe temp
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Office\16.0\OfficeFileCache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Adobe\Acrobat\DC\Cache"),
                
                // Vanliga malware-gömslen
                @"C:\ProgramData",
                @"C:\Windows\SysWOW64",
                @"C:\Windows\System32\drivers",
                @"C:\Users\All Users"
            };

            paths.AddRange(standardPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)));
            
            // Lägg till konfiguerade sökvägar
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

            return paths.Distinct().ToList();
        }

        private async Task<List<ScanResult>> ScanDirectoryRobustAsync(string path)
        {
            var results = new List<ScanResult>();
            const int MaxFilesPerDirectory = 500;
            const int MaxSubdirectories = 10;
            
            try
            {
                if (!HasDirectoryAccessSafe(path))
                {
                    _logger.Debug($"🔒 Ingen åtkomst till: {path}");
                    return results;
                }

                _logger.Debug($"📂 Skannar robust: {path}");

                // Skanna huvudkatalog med begränsningar
                var files = await GetFilesWithTimeoutSafeAsync(path, TimeSpan.FromSeconds(30));
                
                // Begränsa antalet filer för prestanda
                if (files.Length > MaxFilesPerDirectory)
                {
                    _logger.Warning($"⚠️ Begränsar skanning till {MaxFilesPerDirectory} filer i {path}");
                    files = files.Take(MaxFilesPerDirectory).ToArray();
                }

                var fileTasks = files.Select(async file =>
                {
                    try
                    {
                        // Kontrollera cache först
                        var cacheKey = $"{file}_{File.GetLastWriteTime(file).Ticks}";
                        if (_scanCache.ContainsKey(cacheKey))
                        {
                            return null; // Redan skannad
                        }

                        if (IsFileLocked(file) || IsSystemFile(file))
                        {
                            return null;
                        }

                        var result = await AnalyzeFileAdvancedRobustAsync(file);
                        
                        if (result != null)
                        {
                            _scanCache.TryAdd(cacheKey, DateTime.Now);
                        }
                        
                        return result;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Fel vid analys av {file}: {ex.Message}");
                        return null;
                    }
                });

                var fileResults = await Task.WhenAll(fileTasks);
                results.AddRange(fileResults.Where(r => r != null)!);

                // Skanna undermappar (begränsat)
                try
                {
                    var subdirs = Directory.GetDirectories(path).Take(MaxSubdirectories);
                    foreach (var subdir in subdirs)
                    {
                        if (!HasDirectoryAccessSafe(subdir)) continue;
                        
                        var subdirFiles = await GetFilesWithTimeoutSafeAsync(subdir, TimeSpan.FromSeconds(10));
                        foreach (var file in subdirFiles.Take(50)) // Max 50 filer per undermapp
                        {
                            try
                            {
                                if (IsFileLocked(file) || IsSystemFile(file)) continue;
                                
                                var result = await AnalyzeFileAdvancedRobustAsync(file);
                                if (result != null)
                                {
                                    results.Add(result);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug($"Fel vid undermapp-analys {file}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Fel vid undermapp-skanning i {path}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"❌ Fel vid robust skanning av {path}: {ex.Message}");
            }
            
            return results;
        }

        private async Task<ScanResult?> AnalyzeFileAdvancedRobustAsync(string filePath)
        {
            const int MaxAnalysisTimeSeconds = 10;
            
            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(MaxAnalysisTimeSeconds));
                
                return await Task.Run(async () =>
                {
                    var fileInfo = new FileInfo(filePath);
                    var fileName = fileInfo.Name.ToLowerInvariant();
                    var extension = fileInfo.Extension.ToLowerInvariant();
                    
                    // Kolla mot whitelist först
                    if (IsWhitelisted(filePath)) return null;
                    
                    // Undvik systemfiler och för stora filer
                    if (IsSystemFile(filePath) || fileInfo.Length > 200 * 1024 * 1024) return null;
                    
                    var threatLevel = ThreatLevel.Low;
                    var reasons = new List<string>();
                    var confidence = 0;

                    // 1. KRITISK: Kända malware signaturer i innehåll
                    if (fileInfo.Length < 50 * 1024 * 1024) // Under 50MB för innehållsanalys
                    {
                        var contentScore = await AnalyzeContentForMalwareAsync(filePath, cancellationTokenSource.Token);
                        confidence += contentScore.Score;
                        reasons.AddRange(contentScore.Reasons);
                        
                        if (contentScore.Score >= 85)
                        {
                            threatLevel = ThreatLevel.Critical;
                        }
                        else if (contentScore.Score >= 65)
                        {
                            threatLevel = ThreatLevel.High;
                        }
                    }

                    // 2. KRITISK: Telegram bot patterns (höjd prioritet efter ditt intrång)
                    if (await IsTelegramBotRelatedAsync(filePath, fileName, extension))
                    {
                        threatLevel = ThreatLevel.Critical;
                        reasons.Add("🚨 TELEGRAM BOT MALWARE - Screenshot-stöld detekterat");
                        confidence += 90;
                    }

                    // 3. HÖG: Suspekta extensions i temp
                    if (_config.SuspiciousExtensions.Contains(extension))
                    {
                        if (threatLevel < ThreatLevel.High) threatLevel = ThreatLevel.High;
                        reasons.Add($"🔴 Exekverbar fil i temp-katalog ({extension})");
                        confidence += 30;
                    }

                    // 4. KRITISK: Extensionlösa executables med suspekta patterns
                    if (string.IsNullOrEmpty(extension))
                    {
                        if (await IsExecutableFileAsync(filePath) || HasSuspiciousExtensionlessPattern(fileName))
                        {
                            threatLevel = ThreatLevel.Critical;
                            reasons.Add("🚨 EXTENSIONLÖS EXECUTABLE - Klassisk malware-teknik");
                            confidence += 85;
                        }
                    }

                    // 5. KRITISK: Dubbla extensions
                    if (HasDoubleExtension(fileName))
                    {
                        threatLevel = ThreatLevel.Critical;
                        reasons.Add("🚨 DUBBEL EXTENSION - Social engineering attack");
                        confidence += 90;
                    }

                    // 6. HÖG: Kända malware file hashes
                    var hash = await GetFileHashSafeAsync(filePath);
                    if (!string.IsNullOrEmpty(hash) && _knownMalwareHashes.ContainsKey(hash))
                    {
                        threatLevel = ThreatLevel.Critical;
                        reasons.Add($"🚨 KÄND MALWARE HASH: {_knownMalwareHashes[hash]}");
                        confidence += 95;
                    }

                    // 7. MEDIUM: Suspekta filstorlekar och timestamps
                    if (fileInfo.Length == 0)
                    {
                        if (threatLevel < ThreatLevel.Low) threatLevel = ThreatLevel.Low;
                        reasons.Add("⚠️ Tom fil - möjlig placeholder attack");
                        confidence += 10;
                    }
                    else if (fileInfo.Length < 1024 && _config.SuspiciousExtensions.Contains(extension))
                    {
                        if (threatLevel < ThreatLevel.Medium) threatLevel = ThreatLevel.Medium;
                        reasons.Add("🟠 Extremt liten executable - trolig malware");
                        confidence += 25;
                    }

                    // 8. HÖG: Nyligen skapade filer med högt hot-score
                    if (fileInfo.CreationTime > DateTime.Now.AddHours(-6) && confidence >= 50)
                    {
                        if (threatLevel < ThreatLevel.High) threatLevel = ThreatLevel.High;
                        reasons.Add("🔴 NYSKAPAT HOT - Aktivt intrång pågår");
                        confidence += 30;
                    }

                    // 9. HÖG: Suspekta filnamns-patterns
                    if (HasAdvancedSuspiciousNaming(fileName))
                    {
                        if (threatLevel < ThreatLevel.High) threatLevel = ThreatLevel.High;
                        reasons.Add("🔴 Avancerat suspekt filnamn-pattern");
                        confidence += 35;
                    }

                    // 10. MEDIUM: Filer i systemkataloger där de inte hör hemma
                    if (IsInSystemDirectory(filePath) && !IsLegitimateSystemFile(filePath))
                    {
                        if (threatLevel < ThreatLevel.High) threatLevel = ThreatLevel.High;
                        reasons.Add("🔴 Icke-legitim fil i systemkatalog");
                        confidence += 40;
                    }

                    // Om inget hot eller låg confidence, returnera null
                    if (!reasons.Any() || confidence < 15) return null;

                    return new ScanResult
                    {
                        FilePath = filePath,
                        FileSize = fileInfo.Length,
                        CreatedDate = fileInfo.CreationTime,
                        LastModified = fileInfo.LastWriteTime,
                        FileType = extension,
                        ThreatLevel = threatLevel,
                        Reason = $"{string.Join(" | ", reasons)} [Konfidiens: {confidence}%]",
                        FileHash = hash ?? "UNAVAILABLE"
                    };
                    
                }, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning($"⏰ Analys timeout för fil: {filePath}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Fel vid robust filanalys {filePath}: {ex.Message}");
                return null;
            }
        }

        private async Task<(int Score, List<string> Reasons)> AnalyzeContentForMalwareAsync(string filePath, CancellationToken cancellationToken)
        {
            var score = 0;
            var reasons = new List<string>();
            
            try
            {
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                var contentLower = content.ToLowerInvariant();
                
                // Analysera mot malware signaturer
                foreach (var signature in _malwareSignatures)
                {
                    if (contentLower.Contains(signature.Key.ToLowerInvariant()))
                    {
                        score += signature.Value;
                        reasons.Add($"Malware-signatur: {signature.Key} (Score: +{signature.Value})");
                        
                        // Stoppa vid mycket högt score för prestanda
                        if (score >= 200) break;
                    }
                }
                
                return (Math.Min(score, 100), reasons); // Cap at 100
            }
            catch (Exception ex)
            {
                _logger.Debug($"Innehållsanalys misslyckades för {filePath}: {ex.Message}");
                return (0, new List<string>());
            }
        }

        private async Task<bool> IsTelegramBotRelatedAsync(string filePath, string fileName, string extension)
        {
            try
            {
                // Filnamns-check för Telegram bot patterns
                var telegramBotFilePatterns = new[]
                {
                    "screenshot", "capture", "telegram", "bot", "send", "upload",
                    "spy", "monitor", "nircmd", "desktop", "screen"
                };
                
                if (telegramBotFilePatterns.Any(pattern => fileName.Contains(pattern)))
                {
                    return true;
                }
                
                // Innehålls-check för script-filer
                if (new[] { ".bat", ".cmd", ".ps1", ".vbs" }.Contains(extension))
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var contentLower = content.ToLowerInvariant();
                    
                    // Specifika patterns från ditt intrång-exempel
                    var telegramBotSignatures = new[]
                    {
                        "api.telegram.org", "senddocument", "savescreenshot",
                        "nircmd", "screenshot_", "screenshotlog.txt",
                        "chat_id=", "bot", "telegram"
                    };
                    
                    var matchCount = telegramBotSignatures.Count(sig => contentLower.Contains(sig));
                    
                    // Om 3+ signaturer matchar = troligt Telegram bot
                    return matchCount >= 3;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool HasSuspiciousExtensionlessPattern(string fileName)
        {
            return _suspiciousExtensionlessPatterns.Any(pattern => 
                Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase));
        }

        private bool HasAdvancedSuspiciousNaming(string fileName)
        {
            // Random hex strings (8+ chars)
            if (Regex.IsMatch(fileName, @"^[a-f0-9]{8,}$", RegexOptions.IgnoreCase)) return true;
            
            // Base64-like strings
            if (Regex.IsMatch(fileName, @"^[A-Za-z0-9+/]{12,}=*$")) return true;
            
            // Många understreck/bindestreck
            if (fileName.Count(c => c == '_' || c == '-') > 3) return true;
            
            // Endast siffror
            if (Regex.IsMatch(fileName, @"^\d{6,}$")) return true;
            
            // Suspekta prefixes
            var suspiciousPrefixes = new[] { "temp", "tmp", "cache", "~", "$", ".", "copy" };
            if (suspiciousPrefixes.Any(prefix => fileName.StartsWith(prefix) && fileName.Length > prefix.Length + 3))
                return true;
            
            return false;
        }

        private async Task<string?> GetFileHashSafeAsync(string filePath)
        {
            const int MaxHashSizeBytes = 50 * 1024 * 1024; // 50MB
            const int HashTimeoutSeconds = 15;
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxHashSizeBytes) return null;
                
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(HashTimeoutSeconds));
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                
                var hash = await sha256.ComputeHashAsync(stream, cancellationTokenSource.Token);
                return Convert.ToHexString(hash);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Hash-beräkning misslyckades för {filePath}: {ex.Message}");
                return null;
            }
        }

        private bool HasDirectoryAccessSafe(string path)
        {
            try
            {
                return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
            }
            catch
            {
                return false;
            }
        }

        private async Task<string[]> GetFilesWithTimeoutSafeAsync(string path, TimeSpan timeout)
        {
            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(timeout);
                
                return await Task.Run(() =>
                {
                    var files = new List<string>();
                    
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationTokenSource.Token.ThrowIfCancellationRequested();
                        
                        if (!IsSystemFile(file) && !IsFileLocked(file))
                        {
                            files.Add(file);
                        }
                        
                        if (files.Count >= 1000) break; // Säkerhetsgräns
                    }
                    
                    return files.ToArray();
                }, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning($"Timeout vid fillistning: {path}");
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logger.Debug($"Fel vid säker fillistning {path}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private async Task<bool> IsExecutableFileAsync(string filePath)
        {
            try
            {
                using var fs = File.OpenRead(filePath);
                var buffer = new byte[512];
                
                if (await fs.ReadAsync(buffer, 0, buffer.Length) >= 64)
                {
                    // PE header check (MZ + PE signature)
                    if (buffer[0] == 0x4D && buffer[1] == 0x5A)
                    {
                        var peOffset = BitConverter.ToInt32(buffer, 60);
                        if (peOffset < buffer.Length - 4)
                        {
                            fs.Seek(peOffset, SeekOrigin.Begin);
                            var peBuffer = new byte[4];
                            await fs.ReadAsync(peBuffer, 0, 4);
                            return peBuffer[0] == 0x50 && peBuffer[1] == 0x45;
                        }
                        return true;
                    }
                    
                    // ELF header
                    if (buffer[0] == 0x7F && buffer[1] == 0x45 && buffer[2] == 0x4C && buffer[3] == 0x46)
                        return true;
                        
                    // Script headers
                    var text = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(10, buffer.Length));
                    if (text.StartsWith("#!") || text.StartsWith("@echo") || text.StartsWith("REM"))
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private int GetThreatScore(ScanResult result)
        {
            return result.ThreatLevel switch
            {
                ThreatLevel.Critical => 1000,
                ThreatLevel.High => 500,
                ThreatLevel.Medium => 100,
                ThreatLevel.Low => 10,
                _ => 1
            };
        }

        private void CleanupScanCache(object? state)
        {
            try
            {
                var cutoffTime = DateTime.Now.AddHours(-2);
                var keysToRemove = _scanCache
                    .Where(kvp => kvp.Value < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _scanCache.TryRemove(key, out _);
                }

                if (keysToRemove.Any())
                {
                    _logger.Debug($"🧹 Rensade {keysToRemove.Count} gamla cache-entries");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid cache-rensning: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scanSemaphore?.Dispose();
            }
            base.Dispose(disposing);
        }
    }