// Models/ScanResult.cs
using System;
using System.IO;

namespace FilKollen.Models
{
    public class ScanResult
    {
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
        public string FileType { get; set; }
        public ThreatLevel ThreatLevel { get; set; }
        public string Reason { get; set; }
        public bool IsQuarantined { get; set; }
        public string FileHash { get; set; }
        
        public string FileName => Path.GetFileName(FilePath);
        public string FormattedSize => FormatFileSize(FileSize);
        
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
    
    public enum ThreatLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
}

// Models/AppConfig.cs
using System.Collections.Generic;

namespace FilKollen.Models
{
    public class AppConfig
    {
        public bool AutoDelete { get; set; } = false;
        public int QuarantineDays { get; set; } = 30;
        public bool EnableScheduling { get; set; } = false;
        public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
        public TimeSpan ScheduledTime { get; set; } = new TimeSpan(2, 0, 0);
        public List<string> ScanPaths { get; set; } = new();
        public List<string> SuspiciousExtensions { get; set; } = new();
        public List<string> WhitelistPaths { get; set; } = new();
        public bool ShowNotifications { get; set; } = true;
        public bool PlaySoundAlerts { get; set; } = false;
    }
    
    public enum ScheduleFrequency
    {
        Daily,
        Weekly,
        Monthly
    }
}

// Services/FileScanner.cs
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

        public FileScanner(AppConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<List<ScanResult>> ScanAsync()
        {
            var results = new List<ScanResult>();
            
            foreach (var path in _config.ScanPaths)
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(path);
                if (Directory.Exists(expandedPath))
                {
                    var pathResults = await ScanDirectoryAsync(expandedPath);
                    results.AddRange(pathResults);
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
                    var result = await AnalyzeFileAsync(file);
                    if (result != null)
                    {
                        results.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid skanning av {path}: {ex.Message}");
            }
            
            return results;
        }

        private async Task<ScanResult> AnalyzeFileAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name.ToLowerInvariant();
                var extension = fileInfo.Extension.ToLowerInvariant();
                
                // Kolla mot whitelist
                if (IsWhitelisted(filePath))
                    return null;
                
                var threatLevel = ThreatLevel.Low;
                var reasons = new List<string>();
                
                // Kolla suspekta extensions
                if (_config.SuspiciousExtensions.Contains(extension))
                {
                    threatLevel = ThreatLevel.Medium;
                    reasons.Add($"Suspekt filtyp: {extension}");
                }
                
                // Kolla extensionlösa filer
                if (string.IsNullOrEmpty(extension) && IsPotentialExecutable(filePath))
                {
                    threatLevel = ThreatLevel.High;
                    reasons.Add("Extensionlös exekverbar fil");
                }
                
                // Kolla suspekta namn
                if (_suspiciousNames.Any(name => fileName.Contains(name)))
                {
                    threatLevel = ThreatLevel.Critical;
                    reasons.Add("Känt hackerverktyg");
                }
                
                // Kolla dubbla extensions
                if (HasDoubleExtension(fileName))
                {
                    threatLevel = ThreatLevel.High;
                    reasons.Add("Dubbel fil-extension");
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
                _logger.Error($"Fel vid analys av fil {filePath}: {ex.Message}");
                return null;
            }
        }

        private bool IsWhitelisted(string filePath)
        {
            return _config.WhitelistPaths.Any(whitePath => 
                filePath.StartsWith(Environment.ExpandEnvironmentVariables(whitePath), 
                StringComparison.OrdinalIgnoreCase));
        }

        private bool IsPotentialExecutable(string filePath)
        {
            try
            {
                using var fs = File.OpenRead(filePath);
                var buffer = new byte[2];
                if (fs.Read(buffer, 0, 2) == 2)
                {
                    // Kolla PE header (MZ)
                    return buffer[0] == 0x4D && buffer[1] == 0x5A;
                }
            }
            catch { }
            return false;
        }

        private bool HasDoubleExtension(string fileName)
        {
            var parts = fileName.Split('.');
            return parts.Length > 2 && 
                   _config.SuspiciousExtensions.Contains($".{parts[^1]}");
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
                return "N/A";
            }
        }
    }
}
