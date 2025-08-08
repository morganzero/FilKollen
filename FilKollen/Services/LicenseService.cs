using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class LicenseService
    {
        private const string LICENSE_FILE = "license.enc";
        private const string TRIAL_FILE = "trial.dat";
        private const string MACHINE_ID_FILE = "machine.id";
        private readonly ILogger _logger;
        private License? _currentLicense;

        public LicenseService(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<LicenseStatus> ValidateLicenseAsync()
        {
            try
            {
                // Kontrollera trial först
                if (!File.Exists(LICENSE_FILE))
                {
                    return await ValidateTrialAsync();
                }

                // Ladda och validera licens
                _currentLicense = await LoadLicenseAsync();
                if (_currentLicense == null)
                {
                    return LicenseStatus.Invalid;
                }

                // Kontrollera machine ID
                var currentMachineId = GetMachineId();
                if (_currentLicense.MachineId != currentMachineId)
                {
                    _logger.Warning("License machine ID mismatch");
                    return LicenseStatus.MachineIdMismatch;
                }

                // Kontrollera giltighetstid
                if (_currentLicense.IsExpired)
                {
                    _logger.Information($"License expired: {_currentLicense.ExpiryDate}");
                    return LicenseStatus.Expired;
                }

                // Uppdatera senaste validering
                _currentLicense.LastValidated = DateTime.UtcNow;
                await SaveLicenseAsync(_currentLicense);

                _logger.Information($"License valid until: {_currentLicense.ExpiryDate}");
                return LicenseStatus.Valid;
            }
            catch (Exception ex)
            {
                _logger.Error($"License validation error: {ex.Message}");
                return LicenseStatus.Invalid;
            }
        }

        public async Task<bool> RegisterLicenseAsync(string licenseKey, string customerName, string customerEmail)
        {
            try
            {
                _logger.Information($"Attempting to register license for: {customerEmail}");

                // Validera licensnyckel format
                if (!IsValidLicenseKeyFormat(licenseKey))
                {
                    _logger.Warning("Invalid license key format");
                    return false;
                }

                // Dekryptera och validera licensdata
                var license = DecryptLicenseKey(licenseKey);
                if (license == null)
                {
                    _logger.Warning("Failed to decrypt license key");
                    return false;
                }

                // Sätt användarespecifik data
                license.CustomerName = customerName;
                license.CustomerEmail = customerEmail;
                license.MachineId = GetMachineId();
                license.IsActivated = true;
                license.LastValidated = DateTime.UtcNow;

                // Spara licens
                await SaveLicenseAsync(license);
                _currentLicense = license;

                // Ta bort trial-fil om den finns
                if (File.Exists(TRIAL_FILE))
                {
                    File.Delete(TRIAL_FILE);
                }

                _logger.Information($"License successfully registered: {license.Type}, expires: {license.ExpiryDate}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"License registration failed: {ex.Message}");
                return false;
            }
        }

        private async Task<LicenseStatus> ValidateTrialAsync()
        {
            try
            {
                if (!File.Exists(TRIAL_FILE))
                {
                    // Första gången - starta trial
                    await StartTrialAsync();
                    return LicenseStatus.TrialActive;
                }

                var trialData = await File.ReadAllTextAsync(TRIAL_FILE);
                var trialInfo = JsonSerializer.Deserialize<TrialInfo>(trialData);

                if (trialInfo == null || DateTime.UtcNow > trialInfo.ExpiryDate)
                {
                    _logger.Information("Trial period expired");
                    return LicenseStatus.TrialExpired;
                }

                _logger.Information($"Trial active, expires: {trialInfo.ExpiryDate}");
                return LicenseStatus.TrialActive;
            }
            catch (Exception ex)
            {
                _logger.Error($"Trial validation error: {ex.Message}");
                return LicenseStatus.Invalid;
            }
        }

        public async Task StartTrialAsync()
        {
            var trialInfo = new TrialInfo
            {
                StartDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(14),
                MachineId = GetMachineId()
            };

            var json = JsonSerializer.Serialize(trialInfo);
            await File.WriteAllTextAsync(TRIAL_FILE, json);

            _logger.Information($"Trial started, expires: {trialInfo.ExpiryDate}");
        }

        public TimeSpan? GetRemainingTrialTime()
        {
            try
            {
                if (!File.Exists(TRIAL_FILE)) return null;

                var trialData = File.ReadAllText(TRIAL_FILE);
                var trialInfo = JsonSerializer.Deserialize<TrialInfo>(trialData);

                if (trialInfo?.ExpiryDate > DateTime.UtcNow)
                {
                    return trialInfo.ExpiryDate - DateTime.UtcNow;
                }

                return TimeSpan.Zero;
            }
            catch
            {
                return null;
            }
        }

        private async Task<License?> LoadLicenseAsync()
        {
            try
            {
                var encryptedData = await File.ReadAllBytesAsync(LICENSE_FILE);
                var jsonData = DecryptData(encryptedData);
                return JsonSerializer.Deserialize<License>(jsonData);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load license: {ex.Message}");
                return null;
            }
        }

        private async Task SaveLicenseAsync(License license)
        {
            try
            {
                var json = JsonSerializer.Serialize(license);
                var encryptedData = EncryptData(json);
                await File.WriteAllBytesAsync(LICENSE_FILE, encryptedData);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save license: {ex.Message}");
                throw;
            }
        }

        private string GetMachineId()
        {
            try
            {
                if (File.Exists(MACHINE_ID_FILE))
                {
                    return File.ReadAllText(MACHINE_ID_FILE).Trim();
                }

                // Generera unikt machine ID baserat på hårdvara
                var machineId = GenerateMachineId();
                File.WriteAllText(MACHINE_ID_FILE, machineId);
                return machineId;
            }
            catch
            {
                return Environment.MachineName + DateTime.UtcNow.Ticks.ToString();
            }
        }

        private string GenerateMachineId()
        {
            var components = new[]
            {
                Environment.MachineName,
                Environment.UserName,
                Environment.ProcessorCount.ToString(),
                Environment.OSVersion.ToString()
            };

            var combined = string.Join("|", components);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(hash)[..16]; // Första 16 tecken
        }

        private bool IsValidLicenseKeyFormat(string licenseKey)
        {
            if (string.IsNullOrEmpty(licenseKey)) return false;
            
            // Format: FILK-XXXX-XXXX-XXXX-XXXX
            var parts = licenseKey.Split('-');
            return parts.Length == 5 && parts[0] == "FILK" && 
                   parts.All(p => p.Length == 4 || p == "FILK");
        }

        private License? DecryptLicenseKey(string licenseKey)
        {
            try
            {
                // Ta bort prefix och bindestreck
                var keyData = licenseKey.Replace("FILK-", "").Replace("-", "");
                
                // Dekryptera med RSA public key (implementera baserat på din nyckel)
                // Detta är en förenklad version - använd riktiga RSA-nycklar i produktion
                
                return new License
                {
                    LicenseKey = licenseKey,
                    Type = LicenseType.Yearly, // Parse från dekrypterad data
                    IssuedDate = DateTime.UtcNow,
                    ExpiryDate = DateTime.UtcNow.AddYears(1) // Parse från dekrypterad data
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"License key decryption failed: {ex.Message}");
                return null;
            }
        }

        private byte[] EncryptData(string data)
        {
            // Enkel AES-enkryptering för lokal lagring
            var key = Encoding.UTF8.GetBytes("FilKollenSecretKey123456789012"); // 32 bytes
            var iv = new byte[16]; // IV kan vara fast för lokal lagring
            
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            
            using var encryptor = aes.CreateEncryptor();
            var dataBytes = Encoding.UTF8.GetBytes(data);
            return encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);
        }

        private string DecryptData(byte[] encryptedData)
        {
            var key = Encoding.UTF8.GetBytes("FilKollenSecretKey123456789012");
            var iv = new byte[16];
            
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
            return Encoding.UTF8.GetString(decryptedBytes);
        }

        public License? GetCurrentLicense()
        {
            return _currentLicense;
        }

        private class TrialInfo
        {
            public DateTime StartDate { get; set; }
            public DateTime ExpiryDate { get; set; }
            public string MachineId { get; set; } = string.Empty;
        }
    }
}