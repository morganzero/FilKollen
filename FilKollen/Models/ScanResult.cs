using System;
using System.IO;

namespace FilKollen.Models
{
    public class ScanResult
    {
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
        public string FileType { get; set; }
        public ThreatLevel ThreatLevel { get; set; }
        public string Reason { get; set; }
        public bool IsQuarantined { get; set; }
        public string FileHash { get; set; }
        
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
}
