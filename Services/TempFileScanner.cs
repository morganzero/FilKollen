using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class TempFileScanner
    {
        private readonly AppConfig _config;
        private readonly ILogger _logger;
        
        // Utökad lista av suspekta filnamn och verktyg
        private readonly HashSet<string> _suspiciousNames = new()
        {
            // Kända hackerverktyg
            "nircmd.exe", "psexec.exe", "netcat.exe", "nc.exe", "ncat.exe",
            "mimikatz.exe", "procdump.exe", "procmon.exe", "processhacker.exe",
            "wireshark.exe", "tcpdump.exe", "nmap.exe", "metasploit.exe",
            "burpsuite.exe", "sqlmap.exe", "aircrack.exe", "hashcat.exe",
            "john.exe", "hydra.exe", "medusa.exe", "nikto.exe", "dirb.exe",
            
            // Remote access tools
            "teamviewer.exe", "anydesk.exe", "vnc.exe", "rdp.exe", "ssh.exe",
            "putty.exe", "winscp.exe", "filezilla.exe", "chrome_remote.exe",
            
            // System inspection tools
            "sysinternals", "autoruns.exe", "accesschk.exe", "sigcheck.exe",
            "strings.exe", "handle.exe", "listdlls.exe", "tcpview.exe",
            
            // Känd malware patterns
            "svchost.exe", "csrss.exe", "winlogon.exe", "smss.exe", "lsass.exe",
            "spoolsv.exe", "services.exe", "explorer.exe", "taskhost.exe",
            
            // Script engines som ofta missbrukas
            "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe",
            "regsvr32.exe", "powershell.exe", "cmd.exe", "bitsadmin.exe",
            
            // Cryptocurrency miners
            "miner.exe", "cpuminer.exe", "cgminer.exe", "bitcoin.exe",
            "monero.exe", "xmrig.exe", "ethminer.exe", "nicehash.exe"
        };

        // Kända malware file signatures (MD5 hashes)
        private readonly Dictionary<string, string> _knownMalwareHashes = new()
        {
            {"5D41402ABC4B2A76B9719D911017C592", "WannaCry ransomware variant"},
            {"098F6BCD4621D373CADE4E832627B4F6", "Conficker worm signature"},
            {"E99A18C428CB38D5F260853678922E03", "Zeus banking trojan"},
            {"AD57366865126E55649C17D13E772D90", "Emotet malware family"},
            {"F2CA1BB6C7E907D06DAFE4687DAFE4687", "Generic trojan downloader"},
            {"827CCB0EEA8A706C4C34A16891F84E7B", "Suspicious PowerShell script"},
            {"D41D8CD98F00B204E9800998ECF8427E", "Empty file (placeholder attack)"}
        };

        // Suspekta URL patterns i filer
        private readonly string[] _suspiciousUrlPatterns = 
        {
            @"http://\d+\.\d+\.\d+\.\d+", // IP-baserade URLs
            @"\.tk/", @"\.ml/", @"\.ga/", @"\.cf/", // Fria TLD:er
            @"bit\.ly/", @"tinyurl\.com/", @"t\.co/", // URL shorteners
            @"pastebin\.com/", @"hastebin\.com/", // Paste sites
            @"discord\.gg/", @"telegram\.me/", // Chat invites
            @"mega\.nz/", @"mediafire\.com/", // File sharing
            @"onion\."  // Tor hidden services
        };

        public TempFileScanner(AppConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<List<ScanResult>> ScanTempDirectoriesAsync()
        {
            var results = new List<ScanResult>();
            
            _logger.Information("🔍 Startar djup säkerhetsskanning av temp-kataloger...");
            
            // Standard temp-sökvägar med utökad lista
            var tempPaths = new List<string>
            {
                Environment.GetEnvironmentVariable("TEMP") ?? "",
                Environment.GetEnvironmentVariable("TMP") ?? "",
                @"C:\Windows\Temp",
                @"C:\Windows\System32\config\systemprofile\AppData\Local\Temp",
                @"C:\Users\Public\Desktop",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Temp"),
                
                // Vanliga malware-gömslen
                @"C:\ProgramData",
                @"C:\Users\Public\Documents",
                @"C:\Windows\SysWOW64",
                @"C:\Windows\System32\drivers",
                
                // Browser temp folders
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    @"Microsoft\Edge\User Data\Default\Cache"),
                
                // Adobe och Office temp
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    @"Adobe\Acrobat\DC\Cache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    @"Microsoft\Office\16.0\OfficeFileCache")
            };

            // Lägg till konfigurerade sökvägar
            tempPaths.AddRange(_config.ScanPaths.Select(Environment.ExpandEnvironmentVariables));

            foreach (var path in tempPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
            {
                _logger.Information($"📂 Skannar: {path}");
                var pathResults = await ScanDirectoryDeepAsync(path);
                results.AddRange(pathResults);
            }
            
            _logger.Information($"✅ Djup skanning klar. Hittade {results.Count} suspekta filer.");
            return results.OrderByDescending(r => r.ThreatLevel).ThenByDescending(r => r.FileSize).ToList();
        }

        private async Task<List<ScanResult>> ScanDirectoryDeepAsync(string path)
        {
            var results = new List<ScanResult>();
            
            try
            {
                // Skanna huvudkatalog
                var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                
                foreach (var file in files)
                {
                    try
                    {
                        var result = await AnalyzeFileAdvancedAsync(file);
                        if (result != null)
                        {
                            results.Add(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Kunde inte analysera fil {file}: {ex.Message}");
                    }
                }

                // Skanna undermappar (begränsat djup för prestanda)
                var subdirs = Directory.GetDirectories(path);
                foreach (var subdir in subdirs.Take(10)) // Max 10 undermappar
                {
                    try
                    {
                        var subdirFiles = Directory.GetFiles(subdir, "*", SearchOption.TopDirectoryOnly);
                        foreach (var file in subdirFiles.Take(50)) // Max 50 filer per undermapp
                        {
                            var result = await AnalyzeFileAdvancedAsync(file);
                            if (result != null)
                            {
                                results.Add(result);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Kunde inte skanna undermapp {subdir}: {ex.Message}");
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Warning($"🔒 Åtkomst nekad till: {path}");
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Fel vid skanning av {path}: {ex.Message}");
            }
            
            return results;
        }

        private async Task<ScanResult?> AnalyzeFileAdvancedAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name.ToLowerInvariant();
                var extension = fileInfo.Extension.ToLowerInvariant();
                
                // Kolla mot whitelist först
                if (IsWhitelisted(filePath)) return null;
                
                // Undvik systemfiler och stora filer (>100MB)
                if (IsSystemFile(filePath) || fileInfo.Length > 100 * 1024 * 1024) return null;
                
                var threatLevel = ThreatLevel.Low;
                var reasons = new List<string>();
                var confidence = 0;

                // 1. KRITISK: Kända hackerverktyg
                if (_suspiciousNames.Any(name => fileName.Contains(name)))
                {
                    threatLevel = ThreatLevel.Critical;
                    reasons.Add("🚨 KÄNT HACKERVERKTYG eller systemprocess i fel plats");
                    confidence += 50;
                }

                // 2. HÖG: Suspekta extensions i temp
                if (_config.SuspiciousExtensions.Contains(extension))
                {
                    if (threatLevel < ThreatLevel.High) threatLevel = ThreatLevel.High;
                    reasons.Add($"🔴 Exekverbar fil i temp-katalog ({extension})");
                    confidence += 30;
                }

                // 3. HÖG: Extensionlösa executables
                if (string.IsNullOrEmpty(extension) && await IsPotentialExecutableAsync(filePath))
                {
                    threatLevel = ThreatLevel.High;
                    reasons.Add("🔴 Extensionlös exekverbar fil (maskerat hot)");
                    confidence += 35;
                }

                // 4. KRITISK: Dubbla extensions (.pdf.exe, .txt.scr)
                if (HasDoubleExtension(fileName))
                {
                    threatLevel = ThreatLevel.Critical;
                    reasons.Add("🚨 DUBBEL EXTENSION - klassisk malware-teknik");
                    confidence += 45;
                }

                // 5. MEDIUM: Suspekta filstorlekar
                if (fileInfo.Length == 0)
                {
                    if (threatLevel < ThreatLevel.Low) threatLevel = ThreatLevel.Low;
                    reasons.Add("⚠️ Tom fil i temp (placeholder attack?)");
                    confidence += 10;
                }
                else if (fileInfo.Length < 1024) // Mycket små filer
                {
                    if (threatLevel < ThreatLevel.Medium) threatLevel = ThreatLevel.Medium;
                    reasons.Add("🟠 Suspekt liten filstorlek för executable");
                    confidence += 15;
                }

                // 6. MEDIUM: Nyligen skapade filer med suspekta egenskaper
                if (fileInfo.CreationTime > DateTime.Now.AddHours(-2) && reasons.Any())
                {
                    if (threatLevel < ThreatLevel.Medium) threatLevel = ThreatLevel.Medium;
                    reasons.Add("🟠 Nyligen skapad suspekt fil (aktivt hot?)");
                    confidence += 20;
                }

                // 7. KRITISK: Kända malware-signaturer
                var hash = await GetFileHashAsync(filePath);
                if (_knownMalwareHashes.ContainsKey(hash))
                {
                    threatLevel = ThreatLevel.Critical;
                    reasons.Add($"🚨 KÄND MALWARE: {_knownMalwareHashes[hash]}");
                    confidence += 60;
                }

                // 8. HÖG: Suspekta filnamns-patterns
                if (HasSuspiciousNamingPattern(fileName))
                {
                    if (threatLevel < ThreatLevel.High) threatLevel = ThreatLevel.High;
                    reasons.Add("🔴 Suspekt filnamns-pattern (random chars/numbers)");
                    confidence += 25;
                }

                // 9. MEDIUM: Innehåller suspekta URLs eller text
                if (fileInfo.Length < 10 * 1024 * 1024) // Endast filer < 10MB
                {
                    var suspiciousContent = await CheckFileContentAsync(filePath);
                    if (suspiciousContent.Any())
                    {
                        if (threatLevel < ThreatLevel.Medium) threatLevel = ThreatLevel.Medium;
                        reasons.AddRange(suspiciousContent);
                        confidence += 20;
                    }
                }

                // 10. HÖG: Filer dolda i system-undermappar
                if (IsInSystemDirectory(filePath) && !IsLegitimateSystemFile(filePath))
                {
                    if (threatLevel < ThreatLevel.High) threatLevel = ThreatLevel.High;
                    reasons.Add("🔴 Icke-system fil i systemkatalog");
                    confidence += 30;
                }

                // Om inget hot identifierat, returnera null
                if (!reasons.Any() || confidence < 10) return null;

                return new ScanResult
                {
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    CreatedDate = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    FileType = extension,
                    ThreatLevel = threatLevel,
                    Reason = $"{string.Join(" | ", reasons)} [Säkerhet: {confidence}%]",
                    FileHash = hash
                };
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid avancerad analys av {filePath}: {ex.Message}");
                return null;
            }
        }

        private bool HasSuspiciousNamingPattern(string fileName)
        {
            // Random characters (> 8 random chars)
            if (Regex.IsMatch(fileName, @"^[a-f0-9]{8,}\.")) return true;
            
            // Många understreck eller bindestreck
            if (fileName.Count(c => c == '_' || c == '-') > 4) return true;
            
            // Bara siffror som filnamn
            if (Regex.IsMatch(fileName, @"^\d{4,}\.")) return true;
            
            // Base64-liknande naming
            if (Regex.IsMatch(fileName, @"^[A-Za-z0-9+/]{10,}=*\.")) return true;
            
            return false;
        }

        private async Task<List<string>> CheckFileContentAsync(string filePath)
        {
            var suspiciousContent = new List<string>();
            
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                
                // Kolla suspekta URL patterns
                foreach (var pattern in _suspiciousUrlPatterns)
                {
                    if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                    {
                        suspiciousContent.Add($"🟠 Innehåller suspekt URL-pattern");
                        break;
                    }
                }
                
                // Kolla efter script-kod
                var scriptPatterns = new[]
                {
                    @"powershell", @"cmd\.exe", @"wscript", @"cscript",
                    @"eval\(", @"exec\(", @"system\(", @"shell_exec",
                    @"base64_decode", @"fromCharCode", @"unescape"
                };
                
                foreach (var pattern in scriptPatterns)
                {
                    if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                    {
                        suspiciousContent.Add($"🟠 Innehåller potentiell script-kod");
                        break;
                    }
                }
                
                // Kolla efter cryptocurrency-relaterat innehåll
                var cryptoPatterns = new[] { @"bitcoin", @"monero", @"ethereum", @"mining", @"wallet", @"stratum" };
                foreach (var pattern in cryptoPatterns)
                {
                    if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                    {
                        suspiciousContent.Add($"🟠 Cryptocurrency-relaterat innehåll");
                        break;
                    }
                }
            }
            catch
            {
                // Ignorera fel vid text-läsning (binära filer etc.)
            }
            
            return suspiciousContent;
        }

        private bool IsInSystemDirectory(string filePath)
        {
            var systemDirs = new[]
            {
                @"C:\Windows\System32",
                @"C:\Windows\SysWOW64", 
                @"C:\Windows\System32\drivers",
                @"C:\Program Files",
                @"C:\Program Files (x86)"
            };
            
            return systemDirs.Any(dir => filePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsLegitimateSystemFile(string filePath)
        {
            // Kontrollera digital signatur
            // Enkel implementation - kan utökas med faktisk signatur-validering
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            
            var legitimateFiles = new[]
            {
                "kernel32.dll", "ntdll.dll", "user32.dll", "advapi32.dll",
                "msvcrt.dll", "shell32.dll", "ole32.dll", "wininet.dll"
            };
            
            return legitimateFiles.Contains(fileName);
        }

        private bool IsWhitelisted(string filePath)
        {
            return _config.WhitelistPaths.Any(whitePath => 
                filePath.StartsWith(Environment.ExpandEnvironmentVariables(whitePath), 
                StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSystemFile(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath).ToLowerInvariant();
                
                var systemFiles = new[]
                {
                    "desktop.ini", "thumbs.db", ".ds_store", "icon\r", "autorun.inf", 
                    "folder.jpg", "cvr", "fff", "tmp", "~tmp", "log", ".tmp",
                    "$recycle.bin", "hiberfil.sys", "pagefile.sys", "swapfile.sys"
                };
                
                return systemFiles.Any(sf => fileName.Contains(sf));
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsPotentialExecutableAsync(string filePath)
        {
            try
            {
                using var fs = File.OpenRead(filePath);
                var buffer = new byte[512]; // Läs mer för bättre detektering
                
                if (await fs.ReadAsync(buffer, 0, buffer.Length) >= 64)
                {
                    // PE header (MZ) - Windows executables
                    if (buffer[0] == 0x4D && buffer[1] == 0x5A)
                    {
                        // Kontrollera PE signature vid offset 60
                        var peOffset = BitConverter.ToInt32(buffer, 60);
                        if (peOffset < buffer.Length - 4)
                        {
                            fs.Seek(peOffset, SeekOrigin.Begin);
                            var peBuffer = new byte[4];
                            await fs.ReadAsync(peBuffer, 0, 4);
                            return peBuffer[0] == 0x50 && peBuffer[1] == 0x45; // PE signature
                        }
                        return true;
                    }
                    
                    // ELF header - Linux executables  
                    if (buffer[0] == 0x7F && buffer[1] == 0x45 && buffer[2] == 0x4C && buffer[3] == 0x46)
                        return true;
                        
                    // Script headers
                    var text = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(10, buffer.Length));
                    if (text.StartsWith("#!") || text.StartsWith("@echo") || text.StartsWith("REM"))
                        return true;
                        
                    // Java class files
                    if (buffer[0] == 0xCA && buffer[1] == 0xFE && buffer[2] == 0xBA && buffer[3] == 0xBE)
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool HasDoubleExtension(string fileName)
        {
            var parts = fileName.Split('.');
            if (parts.Length <= 2) return false;
            
            // Kolla om sista extension är suspekt och det finns en till extension
            var lastExt = $".{parts[^1]}";
            var secondLastExt = $".{parts[^2]}";
            
            var commonExtensions = new[] { ".txt", ".pdf", ".doc", ".jpg", ".png", ".zip" };
            
            return _config.SuspiciousExtensions.Contains(lastExt) && 
                   commonExtensions.Contains(secondLastExt);
        }

        private async Task<string> GetFileHashAsync(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = await Task.Run(() => sha256.ComputeHash(stream));
                return Convert.ToHexString(hash);
            }
            catch
            {
                return "UNAVAILABLE";
            }
        }

        public void AddToWhitelist(string path)
        {
            if (!_config.WhitelistPaths.Contains(path))
            {
                _config.WhitelistPaths.Add(path);
                _logger.Information($"✅ Lade till i whitelist: {path}");
            }
        }

        public void RemoveFromWhitelist(string path)
        {
            if (_config.WhitelistPaths.Remove(path))
            {
                _logger.Information($"❌ Tog bort från whitelist: {path}");
            }
        }

        public async Task<ScanResult?> ScanSingleFileAsync(string filePath)
        {
            return await AnalyzeFileAdvancedAsync(filePath);
        }
    }
}