using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class QuarantineManager
    {
        private readonly string _quarantinePath;
        private readonly ILogger _logger;
        private readonly string _metadataFile;

        public QuarantineManager(ILogger logger)
        {
            _logger = logger;
            _quarantinePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FilKollen", "Quarantine");
            _metadataFile = Path.Combine(_quarantinePath, "metadata.json");

            EnsureQuarantineDirectoryExists();
        }

        private void EnsureQuarantineDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_quarantinePath))
                {
                    Directory.CreateDirectory(_quarantinePath);
                    _logger.Information($"Skapade karantänkatalog: {_quarantinePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Kunde inte skapa karantänkatalog: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> QuarantineFileAsync(ScanResult scanResult)
        {
            try
            {
                if (!File.Exists(scanResult.FilePath))
                {
                    _logger.Warning($"Fil finns inte för karantän: {scanResult.FilePath}");
                    return false;
                }

                var quarantineId = Guid.NewGuid().ToString();
                var quarantinedFilePath = Path.Combine(_quarantinePath, $"{quarantineId}.quarantine");

                // Kopiera fil till karantän först (säkrare än move)
                File.Copy(scanResult.FilePath, quarantinedFilePath, overwrite: true);

                // Verifiera att kopieringen lyckades
                if (!File.Exists(quarantinedFilePath))
                {
                    _logger.Error($"Kopiering till karantän misslyckades: {scanResult.FilePath}");
                    return false;
                }

                // Ta bort originalfilen EFTER framgångsrik kopiering
                await SecureDeleteAsync(scanResult.FilePath);

                // Spara metadata
                var metadata = await LoadMetadataAsync();
                metadata[quarantineId] = new QuarantineItem
                {
                    Id = quarantineId,
                    OriginalPath = scanResult.FilePath,
                    QuarantineDate = DateTime.UtcNow,
                    ScanResult = scanResult,
                    QuarantinedFilePath = quarantinedFilePath
                };

                await SaveMetadataAsync(metadata);

                _logger.Information($"Fil karantänerad: {scanResult.FilePath} -> {quarantineId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid karantän av {scanResult.FilePath}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteFileAsync(ScanResult scanResult)
        {
            try
            {
                if (!File.Exists(scanResult.FilePath))
                {
                    _logger.Warning($"Fil finns inte för radering: {scanResult.FilePath}");
                    return true; // Redan borttagen
                }

                // Säker borttagning - skriv över med random data först
                await SecureDeleteAsync(scanResult.FilePath);

                _logger.Information($"Fil säkert raderad: {scanResult.FilePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid radering av {scanResult.FilePath}: {ex.Message}");
                return false;
            }
        }

        public async Task<List<QuarantineItem>> GetQuarantinedFilesAsync()
        {
            try
            {
                var metadata = await LoadMetadataAsync();
                var quarantineItems = new List<QuarantineItem>();

                foreach (var kvp in metadata)
                {
                    var item = kvp.Value;

                    // Kontrollera att karantänfilen fortfarande finns
                    if (File.Exists(item.QuarantinedFilePath))
                    {
                        quarantineItems.Add(item);
                    }
                    else
                    {
                        _logger.Warning($"Karantänfil saknas: {item.QuarantinedFilePath}");
                    }
                }

                return quarantineItems;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hämtning av karantänfiler: {ex.Message}");
                return new List<QuarantineItem>();
            }
        }

        public async Task<bool> RestoreFileAsync(string quarantineId)
        {
            try
            {
                var metadata = await LoadMetadataAsync();
                if (!metadata.ContainsKey(quarantineId))
                {
                    _logger.Warning($"Karantän-ID finns inte: {quarantineId}");
                    return false;
                }

                var item = metadata[quarantineId];

                // Kontrollera att karantänfilen finns
                if (!File.Exists(item.QuarantinedFilePath))
                {
                    _logger.Error($"Karantänfil saknas: {item.QuarantinedFilePath}");
                    return false;
                }

                // Kontrollera att målkatalogen finns
                var targetDirectory = Path.GetDirectoryName(item.OriginalPath);
                if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);  // Nu är targetDirectory inte null
                }

                // Återställ fil till original plats
                File.Move(item.QuarantinedFilePath, item.OriginalPath, overwrite: true);

                // Ta bort från metadata
                metadata.Remove(quarantineId);
                await SaveMetadataAsync(metadata);

                _logger.Information($"Fil återställd från karantän: {item.OriginalPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid återställning av {quarantineId}: {ex.Message}");
                return false;
            }
        }

        public async Task<int> CleanupExpiredQuarantineAsync(int retentionDays = 30)
        {
            try
            {
                var metadata = await LoadMetadataAsync();
                var expiredItems = new List<string>();
                var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

                foreach (var kvp in metadata)
                {
                    if (kvp.Value.QuarantineDate < cutoffDate)
                    {
                        expiredItems.Add(kvp.Key);
                    }
                }

                foreach (var id in expiredItems)
                {
                    await DeleteQuarantinedFileAsync(id);
                }

                _logger.Information($"Rensade {expiredItems.Count} utgångna karantänfiler");
                return expiredItems.Count;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid rensning av utgången karantän: {ex.Message}");
                return 0;
            }
        }

        public async Task<bool> DeleteQuarantinedFileAsync(string quarantineId)
        {
            try
            {
                var metadata = await LoadMetadataAsync();
                if (!metadata.ContainsKey(quarantineId))
                {
                    return false;
                }

                var item = metadata[quarantineId];

                // Radera karantänfilen säkert
                if (File.Exists(item.QuarantinedFilePath))
                {
                    await SecureDeleteAsync(item.QuarantinedFilePath);
                }

                // Ta bort från metadata
                metadata.Remove(quarantineId);
                await SaveMetadataAsync(metadata);

                _logger.Information($"Karantänfil permanent raderad: {quarantineId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid radering av karantänfil {quarantineId}: {ex.Message}");
                return false;
            }
        }

        private async Task SecureDeleteAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;

                // Skriv över med random data 3 gånger
                using (var fs = File.OpenWrite(filePath))
                {
                    var buffer = new byte[4096];

                    for (int pass = 0; pass < 3; pass++)
                    {
                        fs.Seek(0, SeekOrigin.Begin);
                        var random = new Random();

                        for (long written = 0; written < fileSize; written += buffer.Length)
                        {
                            var bytesToWrite = (int)Math.Min(buffer.Length, fileSize - written);
                            random.NextBytes(buffer);
                            await fs.WriteAsync(buffer.AsMemory(0, bytesToWrite));
                        }
                        await fs.FlushAsync();
                    }
                }

                // Radera filen efter överskrivning
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Säker radering misslyckades för {filePath}: {ex.Message}");
                // Försök vanlig radering som fallback
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    throw new IOException($"Kunde inte radera fil: {filePath}");
                }
            }
        }

        private async Task<Dictionary<string, QuarantineItem>> LoadMetadataAsync()
        {
            if (!File.Exists(_metadataFile))
            {
                return new Dictionary<string, QuarantineItem>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_metadataFile);
                var result = JsonSerializer.Deserialize<Dictionary<string, QuarantineItem>>(json);
                return result ?? new Dictionary<string, QuarantineItem>();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte läsa karantän-metadata: {ex.Message}");

                // Backup corrupted metadata
                var backupFile = _metadataFile + ".backup." + DateTime.Now.Ticks;
                try
                {
                    File.Copy(_metadataFile, backupFile);
                    _logger.Information($"Korrupt metadata säkerhetskopierad till: {backupFile}");
                }
                catch { }

                return new Dictionary<string, QuarantineItem>();
            }
        }

        private async Task SaveMetadataAsync(Dictionary<string, QuarantineItem> metadata)
        {
            try
            {
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Atomisk skrivning - skriv till temp-fil först
                var tempFile = _metadataFile + ".temp";
                await File.WriteAllTextAsync(tempFile, json);

                // Ersätt originalfilen atomiskt
                File.Move(tempFile, _metadataFile, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.Error($"Kunde inte spara karantän-metadata: {ex.Message}");
                throw;
            }
        }

        // Statistik-metoder
        public async Task<QuarantineStats> GetQuarantineStatsAsync()
        {
            try
            {
                var items = await GetQuarantinedFilesAsync();
                var stats = new QuarantineStats
                {
                    TotalFiles = items.Count,
                    TotalSizeBytes = 0,
                    OldestDate = DateTime.MaxValue,
                    NewestDate = DateTime.MinValue
                };

                foreach (var item in items)
                {
                    if (File.Exists(item.QuarantinedFilePath))
                    {
                        var fileInfo = new FileInfo(item.QuarantinedFilePath);
                        stats.TotalSizeBytes += fileInfo.Length;
                    }

                    if (item.QuarantineDate < stats.OldestDate)
                        stats.OldestDate = item.QuarantineDate;
                    if (item.QuarantineDate > stats.NewestDate)
                        stats.NewestDate = item.QuarantineDate;
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid hämtning av karantän-statistik: {ex.Message}");
                return new QuarantineStats();
            }
        }
    }

    public class QuarantineItem
    {
        public string Id { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string QuarantinedFilePath { get; set; } = string.Empty;
        public DateTime QuarantineDate { get; set; }
        public ScanResult ScanResult { get; set; } = new();

        public string FormattedSize => FormatFileSize(ScanResult.FileSize);
        public string FormattedDate => QuarantineDate.ToString("yyyy-MM-dd HH:mm");
        public int DaysInQuarantine => (int)(DateTime.UtcNow - QuarantineDate).TotalDays;

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

    public class QuarantineStats
    {
        public int TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public DateTime OldestDate { get; set; }
        public DateTime NewestDate { get; set; }

        public string FormattedTotalSize
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB" };
                double len = TotalSizeBytes;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }
    }
    
    // Lägg till i QuarantineManager.cs
public async Task<QuarantineResult> QuarantineFileAsync(string filePath, string reason, ThreatLevel level)
{
    var scanResult = new ScanResult
    {
        FilePath = filePath,
        ThreatLevel = level,
        Reason = reason,
        FileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
        CreatedDate = File.Exists(filePath) ? File.GetCreationTime(filePath) : DateTime.Now,
        LastModified = File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.Now
    };
    
    var success = await QuarantineFileAsync(scanResult);
    return new QuarantineResult { Success = success };
}

public class QuarantineResult
{
    public bool Success { get; set; }
}
}