using System;
using System.Collections.Generic;
using System.IO;
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
            
            Directory.CreateDirectory(_quarantinePath);
        }

        public async Task<bool> QuarantineFileAsync(ScanResult scanResult)
        {
            try
            {
                var quarantineId = Guid.NewGuid().ToString();
                var quarantinedFilePath = Path.Combine(_quarantinePath, quarantineId);
                
                // Flytta fil till karantän
                File.Move(scanResult.FilePath, quarantinedFilePath);
                
                // Spara metadata
                var metadata = await LoadMetadataAsync();
                metadata[quarantineId] = new QuarantineItem
                {
                    Id = quarantineId,
                    OriginalPath = scanResult.FilePath,
                    QuarantineDate = DateTime.Now,
                    ScanResult = scanResult
                };
                
                await SaveMetadataAsync(metadata);
                
                _logger.Information($"Fil karantänerad: {scanResult.FilePath}");
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
            var metadata = await LoadMetadataAsync();
            return new List<QuarantineItem>(metadata.Values);
        }

        public async Task<bool> RestoreFileAsync(string quarantineId)
        {
            try
            {
                var metadata = await LoadMetadataAsync();
                if (!metadata.ContainsKey(quarantineId))
                    return false;

                var item = metadata[quarantineId];
                var quarantinedPath = Path.Combine(_quarantinePath, quarantineId);
                
                // Återställ fil till original plats
                File.Move(quarantinedPath, item.OriginalPath);
                
                // Ta bort från metadata
                metadata.Remove(quarantineId);
                await SaveMetadataAsync(metadata);
                
                _logger.Information($"Fil återställd: {item.OriginalPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid återställning av {quarantineId}: {ex.Message}");
                return false;
            }
        }

        public async Task CleanupExpiredQuarantineAsync(int retentionDays)
        {
            var metadata = await LoadMetadataAsync();
            var expiredItems = new List<string>();
            
            foreach (var kvp in metadata)
            {
                if (DateTime.Now - kvp.Value.QuarantineDate > TimeSpan.FromDays(retentionDays))
                {
                    expiredItems.Add(kvp.Key);
                }
            }
            
            foreach (var id in expiredItems)
            {
                await DeleteQuarantinedFileAsync(id);
            }
        }

        private async Task SecureDeleteAsync(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;
            
            // Skriv över med random data 3 gånger
            using (var fs = File.OpenWrite(filePath))
            {
                var random = new Random();
                var buffer = new byte[4096];
                
                for (int pass = 0; pass < 3; pass++)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    for (long written = 0; written < fileSize; written += buffer.Length)
                    {
                        var bytesToWrite = (int)Math.Min(buffer.Length, fileSize - written);
                        random.NextBytes(buffer);
                        await fs.WriteAsync(buffer, 0, bytesToWrite);
                    }
                    await fs.FlushAsync();
                }
            }
            
            File.Delete(filePath);
        }

        private async Task<Dictionary<string, QuarantineItem>> LoadMetadataAsync()
        {
            if (!File.Exists(_metadataFile))
                return new Dictionary<string, QuarantineItem>();
                
            try
            {
                var json = await File.ReadAllTextAsync(_metadataFile);
                return JsonSerializer.Deserialize<Dictionary<string, QuarantineItem>>(json) ??
                       new Dictionary<string, QuarantineItem>();
            }
            catch
            {
                return new Dictionary<string, QuarantineItem>();
            }
        }

        private async Task SaveMetadataAsync(Dictionary<string, QuarantineItem> metadata)
        {
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(_metadataFile, json);
        }

        private async Task DeleteQuarantinedFileAsync(string quarantineId)
        {
            var quarantinedPath = Path.Combine(_quarantinePath, quarantineId);
            if (File.Exists(quarantinedPath))
            {
                await SecureDeleteAsync(quarantinedPath);
            }
            
            var metadata = await LoadMetadataAsync();
            metadata.Remove(quarantineId);
            await SaveMetadataAsync(metadata);
        }
    }

    public class QuarantineItem
    {
        public string Id { get; set; }
        public string OriginalPath { get; set; }
        public DateTime QuarantineDate { get; set; }
        public ScanResult ScanResult { get; set; }
    }
}
