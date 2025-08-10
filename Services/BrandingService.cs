#nullable disable
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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
    _logger = logger ?? Log.Logger ?? new LoggerConfiguration().WriteTo.Console().CreateLogger();
    
    // KRITISKT: Säker initering som aldrig kraschar
    try
    {
        EnsureBrandingDirectoryExists();
        _currentBranding = LoadBrandingConfig();
        _logger.Information("BrandingService initialized successfully");
    }
    catch (Exception ex)
    {
        _logger.Warning($"BrandingService init warning: {ex.Message} - using fallback");
        _currentBranding = CreateSafeFallbackBranding();
    }
}

        public BrandingConfig GetCurrentBranding()
        {
            // SÄKER: Returnera alltid en giltig config
            return _currentBranding ?? CreateSafeFallbackBranding();
        }

        public async Task<bool> ApplyCustomBrandingAsync(BrandingConfig brandingConfig, string logoPath = null)
        {
            try
            {
                // Säker validering
                if (brandingConfig == null)
                {
                    _logger.Warning("Invalid branding config provided");
                    return false;
                }

                // Säker logo-kopiering
                if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                {
                    try
                    {
                        EnsureBrandingDirectoryExists();
                        var customLogoPath = Path.Combine(BRANDING_DIR, "custom-logo.png");
                        File.Copy(logoPath, customLogoPath, true);
                        brandingConfig.LogoPath = customLogoPath;
                        _logger.Information($"Custom logo copied: {logoPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Logo copy failed: {ex.Message} - continuing without custom logo");
                        // Fortsätt utan custom logo
                    }
                }

                // Sätt timestamp
                brandingConfig.LastUpdated = DateTime.UtcNow;

                // Spara konfiguration säkert
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
                // Ta bort custom config-fil säkert
                if (File.Exists(BRANDING_CONFIG_FILE))
                {
                    try
                    {
                        File.Delete(BRANDING_CONFIG_FILE);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Could not delete config file: {ex.Message}");
                    }
                }

                // Ta bort custom logo säkert
                try
                {
                    var customLogoPath = Path.Combine(BRANDING_DIR, "custom-logo.png");
                    if (File.Exists(customLogoPath))
                    {
                        File.Delete(customLogoPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not delete custom logo: {ex.Message}");
                }

                // Ladda default branding
                _currentBranding = CreateSafeFallbackBranding();

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
                if (string.IsNullOrEmpty(logoPath))
                {
                    result.ErrorMessage = "❌ Ingen fil vald";
                    return result;
                }

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

                // SÄKER: Basic validation utan System.Drawing som kan orsaka problem
                result.IsValid = true;
                result.ErrorMessage = "✅ Logo-fil ser giltig ut";
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
                    BrandingConfig = GetCurrentBranding(),
                    ExportDate = DateTime.UtcNow,
                    ExportVersion = "2.0.0"
                };

                // Säker logo-inkludering
                try
                {
                    var customLogoPath = Path.Combine(BRANDING_DIR, "custom-logo.png");
                    if (File.Exists(customLogoPath))
                    {
                        exportData.LogoData = await File.ReadAllBytesAsync(customLogoPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not include logo in export: {ex.Message}");
                    // Fortsätt utan logo-data
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
                if (string.IsNullOrEmpty(configJson))
                {
                    _logger.Warning("Empty config JSON provided");
                    return false;
                }

                var exportData = JsonSerializer.Deserialize<BrandingExportData>(configJson);
                if (exportData?.BrandingConfig == null)
                {
                    _logger.Warning("Invalid branding config data");
                    return false;
                }

                // Säker logo-import
                if (exportData.LogoData != null && exportData.LogoData.Length > 0)
                {
                    try
                    {
                        EnsureBrandingDirectoryExists();
                        var customLogoPath = Path.Combine(BRANDING_DIR, "custom-logo.png");
                        await File.WriteAllBytesAsync(customLogoPath, exportData.LogoData);
                        exportData.BrandingConfig.LogoPath = customLogoPath;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Could not import logo: {ex.Message}");
                        // Fortsätt utan logo
                    }
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
            try
            {
                if (!Directory.Exists(BRANDING_DIR))
                {
                    Directory.CreateDirectory(BRANDING_DIR);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not create branding directory: {ex.Message}");
                // Inte kritiskt - kan fortsätta utan branding-mapp
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
                        // Validera att config innehåller nödvändiga värden
                        if (string.IsNullOrEmpty(config.CompanyName))
                            config.CompanyName = "FilKollen Security";
                        if (string.IsNullOrEmpty(config.ProductName))
                            config.ProductName = "FilKollen";
                        
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
            return CreateSafeFallbackBranding();
        }

        private BrandingConfig CreateSafeFallbackBranding()
        {
            return new BrandingConfig
            {
                CompanyName = "FilKollen Security",
                ProductName = "FilKollen",
                LogoPath = "default-logo.png", // SÄKER: Ingen absolut sökväg som kan saknas
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