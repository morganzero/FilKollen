using System;

namespace FilKollen.Models
{
    public class QuarantineItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string OriginalPath { get; set; } = string.Empty;
        public string QuarantinedPath { get; set; } = string.Empty;
        public string QuarantinedFilePath { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public ThreatLevel ThreatLevel { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public DateTime QuarantineDate { get; set; } = DateTime.Now;
        public ScanResult? ScanResult { get; set; }
    }

    public class QuarantineResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? ErrorMessage { get; set; }
        public string? QuarantinedPath { get; set; }
        public string? FilePath { get; set; }
        public string? QuarantineId { get; set; }
    }

    public class CopyResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? ErrorMessage { get; set; }
        public string DestinationPath { get; set; } = string.Empty;
    }
}
