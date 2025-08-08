using System;

namespace FilKollen.Models
{
    public class License
    {
        public string LicenseKey { get; set; } = string.Empty;
        public LicenseType Type { get; set; }
        public DateTime IssuedDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string ProductVersion { get; set; } = "2.0.0";
        public string MachineId { get; set; } = string.Empty;
        public bool IsActivated { get; set; }
        public DateTime LastValidated { get; set; }

        public bool IsValid => DateTime.UtcNow <= ExpiryDate && IsActivated;
        public bool IsExpired => DateTime.UtcNow > ExpiryDate;
        public TimeSpan TimeRemaining => ExpiryDate - DateTime.UtcNow;
        
        public string FormattedTimeRemaining
        {
            get
            {
                var remaining = TimeRemaining;
                if (remaining.TotalDays > 1)
                    return $"{remaining.Days} dagar";
                if (remaining.TotalHours > 1)
                    return $"{remaining.Hours} timmar";
                return $"{remaining.Minutes} minuter";
            }
        }
    }

    public enum LicenseType
    {
        Trial = 0,
        Monthly = 1,
        Yearly = 2,
        Lifetime = 3
    }

    public enum LicenseStatus
    {
        Valid,
        Expired,
        Invalid,
        TrialExpired,
        TrialActive,
        NotFound,
        MachineIdMismatch,
        NetworkError
    }
}