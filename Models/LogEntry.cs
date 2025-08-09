using System;

namespace FilKollen.Models
{
    public enum LogLevel
    {
        Debug,
        Information,
        Warning,
        Error,
        Fatal
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; } = LogLevel.Information;
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int ThreadId { get; set; } = Environment.CurrentManagedThreadId;

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss");
        public string LevelIcon => Level switch
        {
            LogLevel.Debug => "🔍",
            LogLevel.Information => "ℹ️",
            LogLevel.Warning => "⚠️",
            LogLevel.Error => "❌",
            LogLevel.Fatal => "💀",
            _ => "📝"
        };
    }
}
