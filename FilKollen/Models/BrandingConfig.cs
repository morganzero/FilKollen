#nullable disable
using System;

namespace FilKollen.Models
{
    public class BrandingConfig
    {
        public string CompanyName { get; set; } = "FilKollen Security";
        public string ProductName { get; set; } = "FilKollen";
        public string LogoPath { get; set; } = "Resources/Branding/default-logo.png";
        public string PrimaryColor { get; set; } = "#2196F3";
        public string SecondaryColor { get; set; } = "#FF9800";
        public string ContactEmail { get; set; } = "support@filkollen.com";
        public string Website { get; set; } = "https://filkollen.com";
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        // Logo requirements (for validation)
        public const int REQUIRED_LOGO_WIDTH = 128;
        public const int REQUIRED_LOGO_HEIGHT = 32;
        public const int MAX_LOGO_SIZE_KB = 50;
        public const string REQUIRED_LOGO_FORMAT = "PNG";
    }
}