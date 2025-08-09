using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // <-- för SequenceEqual
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class QuarantineManager
    {
        private readonly ILogger _logger;
        private readonly string _quarantinePath;
        private readonly string _metadataFile;

        private readonly SemaphoreSlim _operationSemaphore = new(1, 1);

        public QuarantineManager(ILogger logger)
        {
            _logger = logger;
            _quarantinePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FilKollen", "Quarantine");
            Directory.CreateDirectory(_quarantinePath);
            _metadataFile = Path.Combine(_quarantinePath, "metadata.json");
            if (!File.Exists(_metadataFile)) File.WriteAllText(_metadataFile, "{}");
        }

        // === Publika API:er som övrig kod baserar sig på (bool-retur) ===

        public Task<bool> DeleteFileAsync(ScanResult scan) =>
            DeleteFileAsync(scan.FilePath);

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            try { await SecureDeleteAsync(filePath); return true; }
            catch (Exception ex) { _logger.Warning("DeleteFileAsync misslyckades: {Msg}", ex.Message); return false; }
        }

        public Task<bool> QuarantineFileAsync(ScanResult scan)
            => QuarantineFileAsync(scan.FilePath, scan.Reason, scan.ThreatLevel);

        public Task<bool> QuarantineFileAsync(string filePath, string reason, SecuritySeverity severity)
            => QuarantineFileAsync(filePath, reason, MapSeverity(severity));

        public async Task<bool> QuarantineFileAsync(string filePath, string reason, ThreatLevel level)
        {
            var result = await QuarantineFileWithResultAsync(filePath, reason, level);
            return result.Success;
        }

        // === Detaljerat resultat (används internt) ===
        public async Task<QuarantineResult> QuarantineFileWithResultAsync(string filePath, string reason, ThreatLevel level)
        {
            if (!File.Exists(filePath))
                return new QuarantineResult { Success = false, ErrorMessage = "Fil finns inte", FilePath = filePath };

            await _operationSemaphore.WaitAsync();
            try
            {
                var id = Guid.NewGuid().ToString();
                var dest = Path.Combine(_quarantinePath, $"{id}.quarantine");

                var copy = await SafeCopyFileAsync(filePath, dest);
                if (!copy.Success)
                    return new QuarantineResult { Success = false, ErrorMessage = copy.ErrorMessage ?? copy.Error, FilePath = filePath };

                if (!await VerifyFileCopyAsync(filePath, dest))
                {
                    TryDelete(dest);
                    return new QuarantineResult { Success = false, ErrorMessage = "Filkopiering misslyckades verifiering", FilePath = filePath };
                }

                var scan = new ScanResult
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath,
                    ThreatLevel = level,
                    Reason = reason,
                    FileSize = new FileInfo(filePath).Length,
                    CreatedDate = File.GetCreationTime(filePath),
                    LastModified = File.GetLastWriteTime(filePath)
                };

                var meta = await LoadMetadataAsync();
                meta[id] = new QuarantineItem
                {
                    Id = id,
                    OriginalPath = filePath,
                    QuarantinedPath = dest,
                    QuarantinedFilePath = dest,
                    Reason = reason,
                    ThreatLevel = level,
                    Timestamp = DateTime.Now,
                    QuarantineDate = DateTime.Now,
                    ScanResult = scan
                };
                await SaveMetadataAsync(meta);

                await SecureDeleteAsync(filePath);

                _logger.Information("Fil satt i karantän: {File} -> {Id}", filePath, id);
                return new QuarantineResult { Success = true, FilePath = filePath, QuarantineId = id, QuarantinedPath = dest };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Karantän misslyckades för {File}", filePath);
                return new QuarantineResult { Success = false, ErrorMessage = ex.Message, FilePath = filePath };
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        // === Helpers ===

        private static ThreatLevel MapSeverity(SecuritySeverity s) => s switch
        {
            SecuritySeverity.Critical => ThreatLevel.Critical,
            SecuritySeverity.High     => ThreatLevel.High,
            SecuritySeverity.Medium   => ThreatLevel.Medium,
            _                         => ThreatLevel.Low
        };

        private async Task<Dictionary<string, QuarantineItem>> LoadMetadataAsync()
        {
            try
            {
                if (!File.Exists(_metadataFile)) return new();
                using var fs = File.OpenRead(_metadataFile);
                return await JsonSerializer.DeserializeAsync<Dictionary<string, QuarantineItem>>(fs) ?? new();
            }
            catch { return new(); }
        }

        private async Task SaveMetadataAsync(Dictionary<string, QuarantineItem> meta)
        {
            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var tmp = _metadataFile + ".writing";
            await File.WriteAllTextAsync(tmp, json);
            if (File.Exists(_metadataFile)) File.Delete(_metadataFile);
            File.Move(tmp, _metadataFile);
        }

        private async Task SecureDeleteAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                var fi = new FileInfo(filePath);
                if (fi.Length <= 1024 * 1024)
                {
                    var rnd = new byte[fi.Length];
                    RandomNumberGenerator.Fill(rnd);
                    await File.WriteAllBytesAsync(filePath, rnd);
                }
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _logger.Warning("SecureDelete misslyckades för {File}: {Msg}", filePath, ex.Message);
                TryDelete(filePath);
            }
        }

        private void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        private async Task<CopyResult> SafeCopyFileAsync(string src, string dest)
        {
            const int MaxRetries = 3, BufferSize = 64 * 1024;
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    using var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
                    using var d = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                    await s.CopyToAsync(d);
                    await d.FlushAsync();
                    return new CopyResult { Success = true, DestinationPath = dest };
                }
                catch (IOException ex) when (attempt < MaxRetries)
                {
                    _logger.Warning("Kopiering misslyckades, försök {Attempt}/{Max}: {Msg}", attempt, MaxRetries, ex.Message);
                    await Task.Delay(1000 * attempt);
                }
                catch (Exception ex)
                {
                    return new CopyResult { Success = false, ErrorMessage = $"Kopiering misslyckades: {ex.Message}" };
                }
            }
            return new CopyResult { Success = false, ErrorMessage = "Kopiering misslyckades efter upprepade försök" };
        }

        private async Task<bool> VerifyFileCopyAsync(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a); var fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                if (fa.Length < 10 * 1024 * 1024)
                {
                    var ba = await File.ReadAllBytesAsync(a);
                    var bb = await File.ReadAllBytesAsync(b);
                    return ba.SequenceEqual(bb);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning("Fil-verifiering misslyckades: {Msg}", ex.Message);
                return false;
            }
        }
    }
}
