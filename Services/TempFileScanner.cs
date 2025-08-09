using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace FilKollen.Services
{
    // Re-implemented robust base scanner. Extended versions may inherit this.
    public class TempFileScanner : IDisposable
    {
        protected readonly AppConfig _config;
        protected readonly ILogger _logger;
        protected readonly HashSet<string> _whitelistedPaths;
        protected readonly Dictionary<string, string> _knownMalwareHashes;
        protected readonly string[] _scanPaths;

        private readonly string[] _extensions = new[] { ".exe",".bat",".cmd",".ps1",".vbs",".scr",".com",".pif" };
        private readonly SemaphoreSlim _scanSemaphore = new(Environment.ProcessorCount, Environment.ProcessorCount);
        private readonly ConcurrentDictionary<string, DateTime> _scanCache = new();

        public TempFileScanner(AppConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
            _whitelistedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _knownMalwareHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _scanPaths = new[]
            {
                Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
            }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public virtual async Task<List<ScanResult>> ScanTempDirectoriesAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        {
            var results = new List<ScanResult>();
            var tasks = new List<Task<List<ScanResult>>>();
            foreach (var p in _scanPaths)
            {
                tasks.Add(ScanPathWithSemaphoreAsync(p, _scanSemaphore, progress, ct));
            }

            var timeout = TimeSpan.FromMinutes(10);
            var all = Task.WhenAll(tasks);
            var completed = await Task.WhenAny(all, Task.Delay(timeout, ct));
            if (completed != all)
            {
                _logger.Warning("Scanning timeout - avbryter kvarvarande tasks");
            }
            else
            {
                foreach (var list in all.Result) results.AddRange(list);
            }
            return results;
        }

        protected virtual async Task<List<ScanResult>> ScanPathWithSemaphoreAsync(string path, SemaphoreSlim sem, IProgress<ScanProgress>? progress, CancellationToken ct)
        {
            var list = new List<ScanResult>();
            if (!Directory.Exists(path)) return list;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                await sem.WaitAsync(ct);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var r = await ScanSingleFileAsync(file, ct);
                        if (r != null)
                        {
                            lock (list) list.Add(r);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Fel vid filskanning: {File}", file);
                    }
                    finally { sem.Release(); }
                }, ct);
            }
            // drain
            while (sem.CurrentCount < Environment.ProcessorCount) await Task.Delay(50, ct);
            return list;
        }

        public virtual async Task<ScanResult?> ScanSingleFileAsync(string filePath, CancellationToken ct = default)
        {
            try
            {
                if (IsWhitelisted(filePath)) return null;
                if (!File.Exists(filePath)) return null;

                var extension = Path.GetExtension(filePath);
                var isNoExt = string.IsNullOrEmpty(extension);
                var suspiciousExt = _extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

                if (!(suspiciousExt || isNoExt)) return null;

                var info = new FileInfo(filePath);
                if (info.Length == 0) return null;

                var reason = isNoExt ? "Extensionless in Temp" : $"Suspicious extension '{extension}' in Temp";
                return new ScanResult
                {
                    FileName = info.Name,
                    FilePath = info.FullName,
                    ThreatLevel = ThreatLevel.Medium,
                    Reason = reason,
                    FormattedSize = $"{info.Length} B",
                    FileHash = null,
                    IsQuarantined = false
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ScanSingleFileAsync exception");
                return null;
            }
        }

        protected bool IsWhitelisted(string path) => _whitelistedPaths.Contains(path);

        public void Dispose() { _scanSemaphore?.Dispose(); }
    }
}
