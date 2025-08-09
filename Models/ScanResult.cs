using System;

namespace FilKollen.Models
{
    public enum ThreatLevel { Low, Medium, High, Critical }

    public class ScanResult
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public ThreatLevel ThreatLevel { get; set; } = ThreatLevel.Medium;
        public string Reason { get; set; } = string.Empty;
        public string? FormattedSize { get; set; }
        public string? FileHash { get; set; }
        public bool IsQuarantined { get; set; }
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;
    }

    public class ScanProgress
    {
        public int TotalPaths { get; set; }
        public int CompletedPaths { get; set; }
        public int FailedPaths { get; set; }
        public int TotalFilesScanned { get; set; }
        public bool IsCompleted { get; set; }
        public bool HasError { get; set; }
        public string? ErrorMessage { get; set; }
        public string CurrentPath { get; set; } = string.Empty;
        public int FilesScanned { get; set; }
        public int SuspectsFound { get; set; }
        public double Percent { get; set; }
    }
}
