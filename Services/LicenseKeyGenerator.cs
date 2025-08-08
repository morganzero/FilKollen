// 1. Lägg till #nullable disable överst i MainWindow.xaml.cs (första raden)
#nullable disable
using System;
// ... resten av using statements

// 2. Skapa missing Services/LicenseKeyGenerator.cs (eftersom LicenseKeyInfo refereras)
// Services/LicenseKeyGenerator.cs
#nullable disable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class LicenseKeyGenerator
    {
        private const string LICENSE_PREFIX = "FILK";
        private readonly ILogger _logger;

        public LicenseKeyGenerator(ILogger logger)
        {
            _logger = logger;
        }

        public string GenerateLicenseKey(LicenseType type, DateTime expiryDate, string customerEmail, string notes = null)
        {
            try
            {
                _logger.Information($"Generating license key for: {customerEmail}, Type: {type}, Expires: {expiryDate}");

                // Förenklad nyckelgenerering för MVP
                var keyData = $"{type}-{expiryDate:yyyyMMdd}-{GetHashCode()}";
                var licenseKey = $"{LICENSE_PREFIX}-{FormatKeyData(keyData)}";

                _logger.Information($"License key generated successfully: {licenseKey}");
                return licenseKey;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to generate license key: {ex.Message}");
                throw;
            }
        }

        public bool ValidateLicenseKey(string licenseKey)
        {
            try
            {
                if (string.IsNullOrEmpty(licenseKey))
                    return false;

                // Förenklad validering för MVP
                return licenseKey.StartsWith(LICENSE_PREFIX) && licenseKey.Length >= 24;
            }
            catch (Exception ex)
            {
                _logger.Error($"License key validation error: {ex.Message}");
                return false;
            }
        }

        public LicenseKeyInfo ExtractLicenseInfo(string licenseKey)
        {
            try
            {
                if (!ValidateLicenseKey(licenseKey))
                    return null;

                // Förenklad extraktion för MVP
                return new LicenseKeyInfo
                {
                    LicenseKey = licenseKey,
                    Type = LicenseType.Yearly,
                    IssuedDate = DateTime.UtcNow,
                    ExpiryDate = DateTime.UtcNow.AddYears(1),
                    CustomerEmail = "test@example.com",
                    ProductVersion = "2.0.0",
                    Notes = "MVP License",
                    IsValid = true
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to extract license info: {ex.Message}");
                return null;
            }
        }

        private string FormatKeyData(string data)
        {
            // Förenklad formatering
            var hash = data.GetHashCode().ToString("X8");
            return $"{hash[..4]}-{hash[4..8]}-{DateTime.UtcNow.Ticks.ToString()[^4..]}";
        }
    }

    public class LicenseKeyInfo
    {
        public string LicenseKey { get; set; } = string.Empty;
        public LicenseType Type { get; set; }
        public DateTime IssuedDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string CustomerEmail { get; set; } = string.Empty;
        public string ProductVersion { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        
        public string FormattedExpiryDate => ExpiryDate.ToString("yyyy-MM-dd");
        public string TypeDisplayName => Type switch
        {
            LicenseType.Trial => "Trial (14 dagar)",
            LicenseType.Monthly => "Månadsvis",
            LicenseType.Yearly => "Årslicens",
            LicenseType.Lifetime => "Livstidslicens",
            _ => "Okänd"
        };
    }
}