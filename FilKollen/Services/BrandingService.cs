#nullable disable
using System;
using System.IO;
using System.Text.Json;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class BrandingService
    {
        private const string BRANDING_CONFIG_FILE = "branding.json";
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
            return new BrandingConfig();
        }
    }
}