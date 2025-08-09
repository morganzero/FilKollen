using System;
using System.IO;

namespace FilKollen.Models
{
    public enum ThreatLevel { Low, Medium, High, Critical }

    public class ScanResult
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public ThreatLevel ThreatLevel { get; set; } = ThreatLevel.Medium;
        public string Reason { get; set; } = string.Empty;
        public string FormattedSize { get; set; } = string.Empty;
        public string? FileHash { get; set; }
        public bool IsQuarantined { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now; public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
        public string FileType { get; set; } = string.Empty;
        public ThreatLevel ThreatLevel { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool IsQuarantined { get; set; }
        public string FileHash { get; set; } = string.Empty;

        public string FileName => Path.GetFileName(FilePath);
        public string FormattedSize => FormatFileSize(FileSize);

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public enum ThreatLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
    public class ScanResult
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public ThreatLevel ThreatLevel { get; set; } = ThreatLevel.Medium;
        public string Reason { get; set; } = string.Empty;
        public string FormattedSize { get; set; } = string.Empty;
        public string? FileHash { get; set; }
        public bool IsQuarantined { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    public class ScanProgress
    {
        public string CurrentPath { get; set; } = string.Empty;
        public int FilesScanned { get; set; }
        public int SuspectsFound { get; set; }
        public double Percent { get; set; }
    }
}
