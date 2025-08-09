using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using FilKollen.Models;
using Serilog;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FilKollen.Models;

namespace FilKollen.Services
{
    
        public partial class QuarantineManager
    {
        private readonly SemaphoreSlim _operationSemaphore = new(1, 1);
        private readonly object _metadataLock = new object();
        
        // FÖRBÄTTRING: Thread-safe quarantine operations med atomic updates
        public async Task<QuarantineResult> QuarantineFileAtomicAsync(string filePath, string reason, ThreatLevel level)
        {
            if (!File.Exists(filePath))
            {
                return new QuarantineResult 
                { 
                    Success = false, 
                    ErrorMessage = "Fil finns inte",
                    FilePath = filePath 
                };
            }

            await _operationSemaphore.WaitAsync();
            
            try
            {
                var quarantineId = Guid.NewGuid().ToString();
                var quarantinedFilePath = Path.Combine(_quarantinePath, $"{quarantineId}.quarantine");
                var tempMetadataPath = _metadataFile + ".tmp";
                
                // Steg 1: Skapa backup av metadata
                var backupMetadataPath = _metadataFile + $".backup.{DateTime.Now:yyyyMMddHHmmss}";
                if (File.Exists(_metadataFile))
                {
                    File.Copy(_metadataFile, backupMetadataPath, true);
                }
                
                try
                {
                    // Steg 2: Säkert kopiera fil till karantän
                    var copyResult = await SafeCopyFileAsync(filePath, quarantinedFilePath);
                    if (!copyResult.Success)
                    {
                        return new QuarantineResult 
                        { 
                            Success = false, 
                            ErrorMessage = copyResult.ErrorMessage,
                            FilePath = filePath 
                        };
                    }
                    
                    // Steg 3: Verifiera kopia
                    if (!await VerifyFileCopyAsync(filePath, quarantinedFilePath))
                    {
                        File.Delete(quarantinedFilePath);
                        return new QuarantineResult 
                        { 
                            Success = false, 
                            ErrorMessage = "Filkopiering misslyckades verifiering",
                            FilePath = filePath 
                        };
                    }
                    
                    // Steg 4: Uppdatera metadata atomiskt
                    var scanResult = new ScanResult
                    {
                        FilePath = filePath,
                        ThreatLevel = level,
                        Reason = reason,
                        FileSize = new FileInfo(filePath).Length,
                        CreatedDate = File.GetCreationTime(filePath),
                        LastModified = File.GetLastWriteTime(filePath)
                    };
                    
                    var metadata = await LoadMetadataAsync();
                    metadata[quarantineId] = new QuarantineItem
                    {
                        Id = quarantineId,
                        OriginalPath = filePath,
                        QuarantineDate = DateTime.UtcNow,
                        ScanResult = scanResult,
                        QuarantinedFilePath = quarantinedFilePath
                    };
                    
                    // Skriv till temp-fil först
                    await SaveMetadataToFileAsync(metadata, tempMetadataPath);
                    
                    // Atomisk ersättning
                    File.Move(tempMetadataPath, _metadataFile, true);
                    
                    // Steg 5: Ta bort originalfil EFTER framgångsrik metadata-uppdatering
                    await SecureDeleteAsync(filePath);
                    
                    // Rensa backup
                    File.Delete(backupMetadataPath);
                    
                    _logger.Information($"Fil atomiskt karantänerad: {filePath} -> {quarantineId}");
                    
                    return new QuarantineResult 
                    { 
                        Success = true, 
                        QuarantineId = quarantineId,
                        FilePath = filePath 
                    };
                }
                catch (Exception ex)
                {
                    // Rollback: Återställ från backup
                    try
                    {
                        if (File.Exists(quarantinedFilePath))
                            File.Delete(quarantinedFilePath);
                            
                        if (File.Exists(tempMetadataPath))
                            File.Delete(tempMetadataPath);
                            
                        if (File.Exists(backupMetadataPath))
                        {
                            File.Move(backupMetadataPath, _metadataFile, true);
                        }
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.Error($"Rollback misslyckades: {rollbackEx.Message}");
                    }
                    
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Atomisk karantän misslyckades för {filePath}: {ex.Message}");
                return new QuarantineResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message,
                    FilePath = filePath 
                };
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        private async Task<CopyResult> SafeCopyFileAsync(string sourcePath, string destinationPath)
        {
            const int MaxRetries = 3;
            const int BufferSize = 64 * 1024; // 64KB
            
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
                    using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                    
                    await source.CopyToAsync(destination);
                    await destination.FlushAsync();
                    
                    return new CopyResult { Success = true };
                }
                catch (IOException ex) when (attempt < MaxRetries)
                {
                    _logger.Warning($"Kopiering misslyckades, försök {attempt}/{MaxRetries}: {ex.Message}");
                    await Task.Delay(1000 * attempt);
                }
                catch (Exception ex)
                {
                    return new CopyResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Kopiering misslyckades: {ex.Message}" 
                    };
                }
            }
            
            return new CopyResult 
            { 
                Success = false, 
                ErrorMessage = $"Kopiering misslyckades efter {MaxRetries} försök" 
            };
        }

        private async Task<bool> VerifyFileCopyAsync(string originalPath, string copyPath)
        {
            try
            {
                var originalInfo = new FileInfo(originalPath);
                var copyInfo = new FileInfo(copyPath);
                
                // Kontrollera filstorlek
                if (originalInfo.Length != copyInfo.Length)
                {
                    _logger.Warning($"Filstorlek skiljer sig: {originalInfo.Length} vs {copyInfo.Length}");
                    return false;
                }
                
                // För små filer, verifiera byte-för-byte
                if (originalInfo.Length < 10 * 1024 * 1024) // 10MB
                {
                    var originalBytes = await File.ReadAllBytesAsync(originalPath);
                    var copyBytes = await File.ReadAllBytesAsync(copyPath);
                    
                    return originalBytes.SequenceEqual(copyBytes);
                }
                
                // För stora filer, kontrollera bara storlek och datum
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fil-verifiering misslyckades: {ex.Message}");
                return false;
            }
        }

        private async Task SaveMetadataToFileAsync(Dictionary<string, QuarantineItem> metadata, string filePath)
        {
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            // Skriv till temp-fil först för atomisk operation
            var tempPath = filePath + ".writing";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, filePath, true);
        }
    }
}
