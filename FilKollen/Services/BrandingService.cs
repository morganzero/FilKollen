using System;
using System.Drawing;
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
        private const string BRANDING_FOLDER = "Resources/Branding";
        private readonly ILogger _logger;
        private BrandingConfig _currentBranding;

        public BrandingService(ILogger logger)
        {
            _logger = logger;
            _currentBranding = LoadBrandingConfig();
        }

        public BrandingConfig GetCurrentBranding()
        {
            return _currentBranding;
        }

        public async Task<bool> ApplyCustomBrandingAsync(BrandingConfig config, string? customLogoPath = null)
        {
            try
            {
                _logger.Information($"Applying custom branding for: {config.CompanyName}");

                // Validera och hantera custom logo om angiven
                if (!string.IsNullOrEmpty(customLogoPath))
                {
                    var logoValidation = ValidateCustomLogo(customLogoPath);
                    if (!logoValidation.IsValid)
                    {
                        _logger.Warning($"Custom logo validation failed: {logoValidation.ErrorMessage}");
                        return false;
                    }

                    // Kopiera logo till branding-mappen
                    var customLogoFileName = $"custom-logo-{DateTime.UtcNow:yyyyMMddHHmmss}.png";
                    var customLogoDestination = Path.Combine(BRANDING_FOLDER, customLogoFileName);
                    
                    Directory.CreateDirectory(BRANDING_FOLDER);
                    File.Copy(customLogoPath, customLogoDestination, true);
                    
                    config.LogoPath = customLogoDestination;
                    _logger.Information($"Custom logo saved to: {customLogoDestination}");
                }

                // Uppdatera konfiguration
                config.LastUpdated = DateTime.UtcNow;
                _currentBranding = config;

                // Spara konfiguration
                await SaveBrandingConfigAsync(config);

                _logger.Information("Custom branding applied successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to load branding config: {ex.Message}");
            }

            _logger.Information("Using default branding configuration");
            return new BrandingConfig(); // Standardkonfiguration
        }

        private async Task SaveBrandingConfigAsync(BrandingConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(BRANDING_CONFIG_FILE, json);
                _logger.Information("Branding configuration saved");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save branding config: {ex.Message}");
                throw;
            }
        }

        private void CleanupCustomLogos()
        {
            try
            {
                if (!Directory.Exists(BRANDING_FOLDER)) return;

                var customLogoFiles = Directory.GetFiles(BRANDING_FOLDER, "custom-logo-*.png");
                foreach (var file in customLogoFiles)
                {
                    File.Delete(file);
                    _logger.Information($"Deleted custom logo: {file}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to cleanup custom logos: {ex.Message}");
            }
        }

        public bool HasCustomBranding()
        {
            return _currentBranding.CompanyName != "FilKollen Security" || 
                   _currentBranding.LogoPath.Contains("custom-logo");
        }
    }

    public class LogoValidationResult
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }

        public LogoValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            ErrorMessage = message;
        }
    }
}