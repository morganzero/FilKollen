namespace FilKollen.Models
{
    public class QuarantineItem
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string QuarantinedPath { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public ThreatLevel ThreatLevel { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class QuarantineResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? QuarantinedPath { get; set; }
    }

    public class CopyResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string DestinationPath { get; set; } = string.Empty;
    }
}
