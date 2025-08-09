namespace FilKollen.Models
{
    public enum LogLevel { Debug, Info, Warning, Error, Critical }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
