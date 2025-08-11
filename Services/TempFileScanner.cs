using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using FilKollen.Models;

namespace FilKollen.Services
{
    /// <summary>
    /// FÖRBÄTTRAD TempFileScanner som säkerställer korrekt scanning av C:\Windows\Temp
    /// och alla andra temp-mappar med bättre hot-detection
    /// </summary>
    public class TempFileScanner : IDisposable
    {
        protected readonly AppConfig _config;
        protected readonly ILogger _logger;
        protected readonly HashSet<string> _whitelistedPaths;
        protected readonly Dictionary<string, string> _knownMalwareHashes;
        protected readonly string[] _scanPaths;

        // UTÖKADE misstänkta extensions och patterns
        private readonly string[] _suspiciousExtensions = new[] {
            ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com", ".pif", ".msi", ".jar", ".js", ".wsf", ".wsh"
        };

        // Kända malware-filnamn patterns
        private readonly string[] _malwarePatterns = new[] {
            "nircmd", "screenshot", "grabber", "stealer", "miner", "crypter", "loader", "bot", "rat", "keylog"
        };

        private readonly SemaphoreSlim _scanSemaphore = new(Environment.ProcessorCount, Environment.ProcessorCount);
        private readonly ConcurrentDictionary<string, DateTime> _scanCache = new();

        public TempFileScanner(AppConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
            _whitelistedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _knownMalwareHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // FÖRBÄTTRADE scan-sökvägar med explicit prioritering av C:\Windows\Temp
            _scanPaths = new[]
            {
                @"C:\Windows\Temp", // PRIORITET 1: Systemets temp (ofta används av malware)
                Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)),
                @"C:\Temp", // Extra: Vanlig malware-mapp
                @"C:\tmp"   // Extra: Unix-liknande temp
            }.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            _logger.Information($"TempFileScanner initierad med {_scanPaths.Length} sökvägar");
        }

        public virtual async Task<List<ScanResult>> ScanTempDirectoriesAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        {
            var results = new List<ScanResult>();
            var totalPaths = _scanPaths.Length;
            var completedPaths = 0;

            _logger.Information("Startar förbättrad temp-directory scanning...");

            foreach (var path in _scanPaths)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(path);
                    _logger.Information($"Skannar sökväg: {expandedPath}");

                    if (!Directory.Exists(expandedPath))
                    {
                        _logger.Warning($"Sökväg finns inte: {expandedPath}");
                        completedPaths++;
                        continue;
                    }

                    // Rapportera progress
                    progress?.Report(new ScanProgress
                    {
                        TotalPaths = totalPaths,
                        CompletedPaths = completedPaths,
                        CurrentPath = expandedPath,
                        Percent = (double)completedPaths / totalPaths * 100
                    });

                    var pathResults = await ScanSinglePathAsync(expandedPath, ct);
                    results.AddRange(pathResults);

                    _logger.Information($"Skannade {expandedPath}: {pathResults.Count} hot funna");

                }
                catch (UnauthorizedAccessException)
                {
                    _logger.Warning($"Åtkomst nekad till: {path}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid skanning av {path}: {ex.Message}");
                }
                finally
                {
                    completedPaths++;
                }
            }

            // Slutlig progress rapport
            progress?.Report(new ScanProgress
            {
                TotalPaths = totalPaths,
                CompletedPaths = completedPaths,
                IsCompleted = true,
                Percent = 100,
                FilesScanned = results.Count,
                SuspectsFound = results.Count(r => r.ThreatLevel >= ThreatLevel.Medium)
            });

            _logger.Information($"Temp-directory scanning slutförd: {results.Count} filer analyserade, {results.Count(r => r.ThreatLevel >= ThreatLevel.Medium)} hot funna");
            return results;
        }

        /// <summary>
        /// Skannar en enskild sökväg med förbättrad hot-detection
        /// </summary>
        private async Task<List<ScanResult>> ScanSinglePathAsync(string path, CancellationToken ct)
        {
            var results = new List<ScanResult>();
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
            var tasks = new List<Task>();

            try
            {
                // Få alla filer i katalogen (inte underkataloger för säkerhet)
                var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    if (ct.IsCancellationRequested) break;

                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(ct);
                        try
                        {
                            var result = await ScanSingleFileAsync(file, ct);
                            if (result != null)
                            {
                                lock (results)
                                {
                                    results.Add(result);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Fel vid filskanning: {file} - {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, ct));
                }

                // Vänta på alla scanning tasks med timeout
                var allTasks = Task.WhenAll(tasks);
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2), ct);

                var completedTask = await Task.WhenAny(allTasks, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    _logger.Warning($"Timeout vid skanning av {path}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid skanning av sökväg {path}: {ex.Message}");
            }

            return results;
        }

        public virtual async Task<ScanResult?> ScanSingleFileAsync(string filePath, CancellationToken ct = default)
        {
            try
            {
                if (IsWhitelisted(filePath)) return null;
                if (!File.Exists(filePath)) return null;

                await Task.Yield(); // För async compliance

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0) return null; // Skippa tomma filer

                // FÖRBÄTTRAD hot-detection logik
                var threatAnalysis = AnalyzeThreatLevel(fileInfo);

                // Endast rapportera medium+ hot
                if (threatAnalysis.Level < ThreatLevel.Medium) return null;

                return new ScanResult
                {
                    FileName = fileInfo.Name,
                    FilePath = fileInfo.FullName,
                    ThreatLevel = threatAnalysis.Level,
                    Reason = threatAnalysis.Reason,
                    FormattedSize = FormatFileSize(fileInfo.Length),
                    FileSize = fileInfo.Length,
                    CreatedDate = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    FileHash = null, // Beräknas vid behov för prestanda
                    IsQuarantined = false
                };
            }
            catch (Exception ex)
            {
                _logger.Debug($"ScanSingleFileAsync exception för {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// FÖRBÄTTRAD hot-analys som bedömer risknivå baserat på flera faktorer
        /// </summary>
        private (ThreatLevel Level, string Reason) AnalyzeThreatLevel(FileInfo fileInfo)
        {
            var fileName = fileInfo.Name.ToLowerInvariant();
            var extension = fileInfo.Extension.ToLowerInvariant();
            var isExecutable = _suspiciousExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
            var isExtensionless = string.IsNullOrEmpty(extension);

            // KRITISK NIVÅ: Kända malware-patterns
            foreach (var pattern in _malwarePatterns)
            {
                if (fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return (ThreatLevel.Critical, $"Kritisk: Innehåller malware-pattern '{pattern}'");
                }
            }

            // KRITISK NIVÅ: Misstänkta filnamn i temp
            var suspiciousNames = new[] { "update.exe", "install.exe", "setup.exe", "launcher.exe", "run.exe", "start.exe" };
            if (suspiciousNames.Contains(fileName))
            {
                return (ThreatLevel.Critical, $"Kritisk: Misstänkt filnamn '{fileName}' i temp-mapp");
            }

            // HÖG NIVÅ: Executables mindre än 10MB i temp (ofta malware)
            if (isExecutable && fileInfo.Length < 10 * 1024 * 1024)
            {
                return (ThreatLevel.High, $"Hög: Liten executable-fil ({extension}) i temp-mapp");
            }

            // HÖG NIVÅ: Extensionslösa filer över 1KB (ofta dolda executables)
            if (isExtensionless && fileInfo.Length > 1024)
            {
                return (ThreatLevel.High, "Hög: Extensionslös fil över 1KB i temp-mapp");
            }

            // MEDIUM NIVÅ: Script-filer
            var scriptExtensions = new[] { ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf" };
            if (scriptExtensions.Contains(extension))
            {
                return (ThreatLevel.Medium, $"Medium: Script-fil ({extension}) i temp-mapp");
            }

            // MEDIUM NIVÅ: Alla andra executables
            if (isExecutable)
            {
                return (ThreatLevel.Medium, $"Medium: Executable-fil ({extension}) i temp-mapp");
            }

            // MEDIUM NIVÅ: Filer skapade mycket nyligen (under 1 timme)
            if (DateTime.Now - fileInfo.CreationTime < TimeSpan.FromHours(1))
            {
                return (ThreatLevel.Medium, "Medium: Nyligen skapad fil i temp-mapp");
            }

            // LÅG NIVÅ: Övriga misstänkta filer
            return (ThreatLevel.Low, "Låg: Fil i temp-mapp");
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024:N0} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):N1} MB";
            return $"{bytes / (1024 * 1024 * 1024):N1} GB";
        }

        protected bool IsWhitelisted(string path)
        {
            try
            {
                // Grundläggande whitelist-kontroll
                if (_whitelistedPaths.Contains(path)) return true;

                // Whitelist vissa Windows-systemfiler
                var fileName = Path.GetFileName(path).ToLowerInvariant();
                var systemWhitelist = new[] {
                    "perflib_perfdata", "wmiadap.exe", "wmic.exe", "dllhost.exe", "svchost.exe"
                };

                return systemWhitelist.Any(wl => fileName.Contains(wl));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Test-metod för att validera scanning av specifik sökväg
        /// </summary>
        public async Task<int> TestScanPath(string path)
        {
            try
            {
                _logger.Information($"Test-scanning sökväg: {path}");
                var results = await ScanSinglePathAsync(path, CancellationToken.None);
                _logger.Information($"Test-resultat för {path}: {results.Count} filer funna");

                foreach (var result in results.Take(5))
                {
                    _logger.Information($"  - {result.FileName}: {result.ThreatLevel} ({result.Reason})");
                }

                return results.Count;
            }
            catch (Exception ex)
            {
                _logger.Error($"Test-scanning misslyckades för {path}: {ex.Message}");
                return 0;
            }
        }

        public void Dispose()
        {
            try
            {
                _scanSemaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning($"TempFileScanner dispose error: {ex.Message}");
            }
        }
    }
}