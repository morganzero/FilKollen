// Services/QuarantineManager.cs
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

// Services/ScheduleManager.cs
using System;
using System.Threading.Tasks;
using Microsoft.Win32.TaskScheduler;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class ScheduleManager
    {
        private readonly ILogger _logger;
        private const string TaskName = "FilKollen_AutoScan";

        public ScheduleManager(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<bool> CreateScheduledTaskAsync(AppConfig config)
        {
            try
            {
                using var ts = new TaskService();
                
                // Ta bort befintlig task om den finns
                ts.RootFolder.DeleteTask(TaskName, false);
                
                var td = ts.NewTask();
                td.RegistrationInfo.Description = "FilKollen automatisk skanning";
                td.RegistrationInfo.Author = "FilKollen";
                
                // Sätt trigger baserat på frekvens
                Trigger trigger = config.Frequency switch
                {
                    ScheduleFrequency.Daily => new DailyTrigger 
                    { 
                        StartBoundary = DateTime.Today.Add(config.ScheduledTime),
                        DaysInterval = 1 
                    },
                    ScheduleFrequency.Weekly => new WeeklyTrigger 
                    { 
                        StartBoundary = DateTime.Today.Add(config.ScheduledTime),
                        WeeksInterval = 1,
                        DaysOfWeek = DaysOfTheWeek.Monday 
                    },
                    ScheduleFrequency.Monthly => new MonthlyTrigger 
                    { 
                        StartBoundary = DateTime.Today.Add(config.ScheduledTime),
                        MonthsOfYear = MonthsOfTheYear.AllMonths,
                        DaysOfMonth = new[] { 1 }
                    },
                    _ => throw new ArgumentException("Okänd schema-frekvens")
                };
                
                td.Triggers.Add(trigger);
                
                // Sätt action - kör FilKollen med --scheduled parameter
                var execPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                td.Actions.Add(new ExecAction(execPath, "--scheduled"));
                
                // Kör med högsta rättigheter
                td.Principal.RunLevel = TaskRunLevel.Highest;
                td.Settings.AllowDemandStart = true;
                td.Settings.AllowHardTerminate = false;
                
                ts.RootFolder.RegisterTaskDefinition(TaskName, td);
                
                _logger.Information($"Schemalagd task skapad: {config.Frequency} kl {config.ScheduledTime}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid skapande av schemalagd task: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteScheduledTaskAsync()
        {
            try
            {
                using var ts = new TaskService();
                ts.RootFolder.DeleteTask(TaskName, false);
                
                _logger.Information("Schemalagd task raderad");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid radering av schemalagd task: {ex.Message}");
                return false;
            }
        }

        public bool IsTaskScheduled()
        {
            try
            {
                using var ts = new TaskService();
                var task = ts.GetTask(TaskName);
                return task != null && task.Enabled;
            }
            catch
            {
                return false;
            }
        }

        public DateTime? GetNextRunTime()
        {
            try
            {
                using var ts = new TaskService();
                var task = ts.GetTask(TaskName);
                return task?.NextRunTime;
            }
            catch
            {
                return null;
            }
        }
    }
}
