#nullable disable
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Drawing;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class BrandingService
    {
        private const string BRANDING_CONFIG_FILE = "branding.json";
        private const string BRANDING_DIR = "Resources/Branding";
        private readonly ILogger _logger;
        private BrandingConfig _currentBranding;

        public BrandingService(ILogger logger)
        {
            _logger = logger;
            EnsureBrandingDirectoryExists();
            _currentBranding = LoadBrandingConfig();
        }

        public BrandingConfig GetCurrentBranding()
        {
            return _currentBranding;
        }

        public async Task<bool> ApplyCustomBrandingAsync(BrandingConfig brandingConfig, string logoPath = null)
        {
            try
            {
                // Kopiera custom logo om det finns
                if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                {
                    var customLogoPath = Path.Combine(BRANDING_DIR, "custom-logo.png");
                    File.Copy(logoPath, customLogoPath, true);
                    brandingConfig.LogoPath = customLogoPath;
                }

                // Uppdatera timestamp
                brandingConfig.LastUpdated = DateTime.UtcNow;

                // Spara konfiguration
                var json = JsonSerializer.Serialize(brandingConfig, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(BRANDING_CONFIG_FILE, json);

                // Uppdatera current branding
                _currentBranding = brandingConfig;

                _logger.Information($"Custom branding applied for: {brandingConfig.CompanyName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply custom branding: {ex.Message}");
                return false;
            }
        }

        public async Task ResetToDefaultBrandingAsync()
        {
            try
            {
                // Ta bort custom config-fil
                if (File.Exists(BRANDING_CONFIG_FILE))
                {
                    File.Delete(BRANDING_CONFIG_FILE);
                }

                // Ta bort custom logo
                var customLogoPath = Path.Combine(BRANDING_DIR, "custom-logo.png");
                if (File.Exists(customLogoPath))
                {
                    File.Delete(customLogoPath);
                }

                // Ladda default branding
                _currentBranding = CreateDefaultBranding();

                _logger.Information("Branding reset to default");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to reset branding: {ex.Message}");
                throw;
            }
        }

        public LogoValidationResult ValidateCustomLogo(string logoPath)
        {
            var result = new LogoValidationResult();

            try
            {
                if (!File.Exists(logoPath))
                {
                    result.ErrorMessage = "❌ Filen finns inte";
                    return result;
                }

                var fileInfo = new FileInfo(logoPath);
                
                // Kontrollera filstorlek
                if (fileInfo.Length > BrandingConfig.MAX_LOGO_SIZE_KB * 1024)
                {
                    result.ErrorMessage = $"❌ Filen är för stor (max {BrandingConfig.MAX_LOGO_SIZE_KB} KB)";
                    return result;
                }

                // Kontrollera filformat
                if (!logoPath.ToLowerInvariant().EndsWith(".png"))
                {
                    result.ErrorMessage = "❌ Endast PNG-format stöds";
                    return result;
                }

                // Kontrollera bildstorlek
                using (var image = Image.FromFile(logoPath))
                {
                    if (image.Width != BrandingConfig.REQUIRED_LOGO_WIDTH || 
                        image.Height != BrandingConfig.REQUIRED_LOGO_HEIGHT)
                    {
                        result.ErrorMessage = $"❌ Felaktig storlek. Krävs: {BrandingConfig.REQUIRED_LOGO_WIDTH}x{BrandingConfig.REQUIRED_LOGO_HEIGHT} pixels";
                        return result;
                    }
                }

                result.IsValid = true;
                result.ErrorMessage = "✅ Logo-fil är giltig";
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"❌ Fel vid validering: {ex.Message}";
                return result;
            }
        }

        public async Task<string> ExportBrandingConfigAsync()
        {
            try
            {
                var exportData = new BrandingExportData
                {
                    BrandingConfig = _currentBranding,
                    ExportDate = DateTime.UtcNow,
                    ExportVersion = "2.0.0"
                };

                // Inkludera logo-data om custom logo finns
                var customLogoPath = Path.Combine(BRANDING_DIR, "custom-logo.png");
                if (File.Exists(customLogoPath))
                {
                    exportData.LogoData = await File.ReadAllBytesAsync(customLogoPath);
                }

                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                return json;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to export branding config: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> ImportBrandingConfigAsync(string configJson)
        {
            try
            {
                var exportData = JsonSerializer.Deserialize<BrandingExportData>(configJson);
                if (exportData?.BrandingConfig == null)
                {
                    _logger.Warning("Invalid branding config data");
                    return false;
                }

                // Importera logo om det finns
                if (exportData.LogoData != null && exportData.LogoData.Length > 0)
                {
                    var customLogoPath = Path.Combine(BRANDING_DIR, "custom-logo.png");
                    await File.WriteAllBytesAsync(customLogoPath, exportData.LogoData);
                    exportData.BrandingConfig.LogoPath = customLogoPath;
                }

                // Tillämpa importerad branding
                return await ApplyCustomBrandingAsync(exportData.BrandingConfig);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to import branding config: {ex.Message}");
                return false;
            }
        }

        private void EnsureBrandingDirectoryExists()
        {
            if (!Directory.Exists(BRANDING_DIR))
            {
                Directory.CreateDirectory(BRANDING_DIR);
            }
        }

        private BrandingConfig LoadBrandingConfig()
        {
            try
            {
                if (File.Exists(BRANDING_CONFIG_FILE))
                {
                    var json = File.ReadAllText(BRANDING_CONFIG_FILE);
                    var config = JsonSerializer.Deserialize<BrandingConfig>(json);
                    if (config != null)
                    {
                        _logger.Information($"Loaded custom branding for: {config.CompanyName}");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to load branding config: {ex.Message}");
            }

            _logger.Information("Using default branding configuration");
            return CreateDefaultBranding();
        }

        private BrandingConfig CreateDefaultBranding()
        {
            return new BrandingConfig
            {
                CompanyName = "FilKollen Security",
                ProductName = "FilKollen",
                LogoPath = Path.Combine(BRANDING_DIR, "default-logo.png"),
                PrimaryColor = "#2196F3",
                SecondaryColor = "#FF9800",
                ContactEmail = "support@filkollen.com",
                Website = "https://filkollen.com",
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    // Support klasser
    public class LogoValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public class BrandingExportData
    {
        public BrandingConfig BrandingConfig { get; set; }
        public byte[] LogoData { get; set; }
        public DateTime ExportDate { get; set; }
        public string ExportVersion { get; set; } = "2.0.0";
    }
}