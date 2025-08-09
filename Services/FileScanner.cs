using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class FileScanner
    {
        private readonly AppConfig _config;
        private readonly ILogger _logger;
        
        private readonly HashSet<string> _suspiciousNames = new()
        {
            "nircmd.exe", "psexec.exe", "netcat.exe", "nc.exe",
            "mimikatz.exe", "procdump.exe", "sysinternals"
        };

        // Kända hot-signaturer (förenklat för demo)
        private readonly Dictionary<string, string> _knownMalwareHashes = new()
        {
            {"D41D8CD98F00B204E9800998ECF8427E", "Tom fil (potentiell placeholder)"},
            {"5D41402ABC4B2A76B9719D911017C592", "Test malware signature"}
        };

        public FileScanner(AppConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<List<ScanResult>> ScanAsync()
        {
            var results = new List<ScanResult>();
            
            _logger.Information("Startar säkerhetsskanning...");
            
            foreach (var path in _config.ScanPaths)
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(path);
                if (Directory.Exists(expandedPath))
                {
                    _logger.Information($"Skannar: {expandedPath}");
                    var pathResults = await ScanDirectoryAsync(expandedPath);
                    results.AddRange(pathResults);
                }
                else
                {
                    _logger.Warning($"Sökväg finns inte: {expandedPath}");
                }
            }
            
            _logger.Information($"Skanning klar. Hittade {results.Count} suspekta filer.");
            return results;
        }

        private async Task<List<ScanResult>> ScanDirectoryAsync(string path)
        {
            var results = new List<ScanResult>();
            
            try
            {
                var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                
                foreach (var file in files)
                {
                    try
                    {
                        var result = await AnalyzeFileAsync(file);
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
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Warning($"Åtkomst nekad till: {path}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid skanning av {path}: {ex.Message}");
            }
            
            return results;
        }

        private async Task<ScanResult?> AnalyzeFileAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name.ToLowerInvariant();
                var extension = fileInfo.Extension.ToLowerInvariant();
                
                // Kolla mot whitelist
                if (IsWhitelisted(filePath))
                    return null;
                
                // Undvik systemfiler
                if (IsSystemFile(filePath))
                    return null;
                
                var threatLevel = ThreatLevel.Low;
                var reasons = new List<string>();
                
                // 1. Kolla suspekta extensions
                if (_config.SuspiciousExtensions.Contains(extension))
                {
                    threatLevel = ThreatLevel.Medium;
                    reasons.Add($"Suspekt filtyp: {extension}");
                }
                
                // 2. Kolla extensionlösa filer
                if (string.IsNullOrEmpty(extension) && await IsPotentialExecutableAsync(filePath))
                {
                    threatLevel = ThreatLevel.High;
                    reasons.Add("Extensionlös exekverbar fil");
                }
                
                // 3. Kolla suspekta namn
                if (_suspiciousNames.Any(name => fileName.Contains(name)))
                {
                    threatLevel = ThreatLevel.Critical;
                    reasons.Add("Känt hackerverktyg");
                }
                
                // 4. Kolla dubbla extensions (.txt.exe)
                if (HasDoubleExtension(fileName))
                {
                    threatLevel = ThreatLevel.High;
                    reasons.Add("Dubbel fil-extension (maskerat hot)");
                }
                
                // 5. Kolla filstorlek (mycket små eller stora filer i temp)
            if (fileInfo.Length == 0)
            {
                if (threatLevel < ThreatLevel.Low)
                    threatLevel = ThreatLevel.Low;
                reasons.Add("Tom fil i temp-katalog");
            }
            else if (fileInfo.Length > 100 * 1024 * 1024) // > 100MB
            {
                if (threatLevel < ThreatLevel.Medium)
                    threatLevel = ThreatLevel.Medium;
                reasons.Add("Mycket stor fil i temp-katalog");
            }
                
                // 6. Kolla ålder (filer skapade nyligen kan vara suspekta)
                if (fileInfo.CreationTime > DateTime.Now.AddHours(-1))
                {
                    if (threatLevel == ThreatLevel.Low && reasons.Any())
                    {
                        threatLevel = ThreatLevel.Medium;
                        reasons.Add("Nyligen skapad suspekt fil");
                    }
                }
                
                // 7. Kolla hash mot kända hot (endast för mindre filer)
                if (fileInfo.Length < 50 * 1024 * 1024) // < 50MB
                {
                    var hash = await GetFileHashAsync(filePath);
                    if (_knownMalwareHashes.ContainsKey(hash))
                    {
                        threatLevel = ThreatLevel.Critical;
                        reasons.Add($"Känd malware-signatur: {_knownMalwareHashes[hash]}");
                    }
                }
                
                // Om ingen hotflag, returnera null
                if (!reasons.Any())
                    return null;
                
                return new ScanResult
                {
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    CreatedDate = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    FileType = extension,
                    ThreatLevel = threatLevel,
                    Reason = string.Join(", ", reasons),
                    FileHash = await GetFileHashAsync(filePath)
                };
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid analys av fil {filePath}: {ex.Message}");
                return null;
            }
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
                
                // Vanliga systemfiler som ska undvikas
                var systemFiles = new[]
                {
                    "desktop.ini", "thumbs.db", ".ds_store",
                    "icon\r", "autorun.inf", "folder.jpg",
                    "cvr", "fff", "tmp", "~tmp", "log"
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
                var buffer = new byte[4];
                
                if (await fs.ReadAsync(buffer, 0, 4) >= 2)
                {
                    // Kolla PE header (MZ) - Windows executables
                    if (buffer[0] == 0x4D && buffer[1] == 0x5A)
                        return true;
                        
                    // Kolla ELF header - Linux executables
                    if (buffer[0] == 0x7F && buffer[1] == 0x45 && buffer[2] == 0x4C && buffer[3] == 0x46)
                        return true;
                        
                    // Kolla script headers
                    var text = System.Text.Encoding.ASCII.GetString(buffer);
                    if (text.StartsWith("#!"))
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
            return _config.SuspiciousExtensions.Contains(lastExt) && parts.Length > 2;
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

        // Metod för real-time scanning (används av RealTimeProtectionService)
        public async Task<ScanResult?> ScanSingleFileAsync(string filePath)
        {
            return await AnalyzeFileAsync(filePath);
        }

        // Metod för att lägga till whitelist entry
        public void AddToWhitelist(string path)
        {
            if (!_config.WhitelistPaths.Contains(path))
            {
                _config.WhitelistPaths.Add(path);
                _logger.Information($"Lade till i whitelist: {path}");
            }
        }

        // Metod för att ta bort från whitelist
        public void RemoveFromWhitelist(string path)
        {
            if (_config.WhitelistPaths.Remove(path))
            {
                _logger.Information($"Tog bort från whitelist: {path}");
            }
        }
    }
}