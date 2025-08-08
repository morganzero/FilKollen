    public class LicenseRequest
    {
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public LicenseType LicenseType { get; set; }
        public int DurationMonths { get; set; } = 12;
        public string Notes { get; set; } = string.Empty;
        public DateTime RequestDate { get; set; } = DateTime.UtcNow;
        public string RequestedBy { get; set; } = string.Empty; // För återförsäljare
    }