using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FilKollen.Services
{
    public class LogViewerService : INotifyPropertyChanged
    {
        private readonly string _logDirectory;
        private ObservableCollection<LogEntry> _logEntries;
        private FileSystemWatcher _logWatcher;

        public ObservableCollection<LogEntry> LogEntries
        {
            get => _logEntries;
            set
            {
                _logEntries = value;
                OnPropertyChanged();
            }
        }

        public LogViewerService()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            _logEntries = new ObservableCollection<LogEntry>();
            
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            SetupFileWatcher();
            LoadExistingLogs();
        }

        private void SetupFileWatcher()
        {
            _logWatcher = new FileSystemWatcher(_logDirectory, "*.log")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _logWatcher.Changed += OnLogFileChanged;
            _logWatcher.Created += OnLogFileChanged;
        }

        private async void OnLogFileChanged(object sender, FileSystemEventArgs e)
        {
            // Vänta lite för att filen ska slutföras
            await Task.Delay(100);
            await LoadLogFileAsync(e.FullPath);
        }

        public async Task LoadExistingLogs()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "*.log")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(5); // Ladda bara senaste 5 loggfilerna

                foreach (var logFile in logFiles)
                {
                    await LoadLogFileAsync(logFile);
                }
            }
            catch (Exception ex)
            {
                AddLogEntry(LogLevel.Error, "LogViewer", $"Kunde inte ladda befintliga loggar: {ex.Message}");
            }
        }

        private async Task LoadLogFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                var lines = await File.ReadAllLinesAsync(filePath);
                var recentLines = lines.TakeLast(50); // Bara senaste 50 raderna per fil

                foreach (var line in recentLines)
                {
                    var logEntry = ParseLogLine(line);
                    if (logEntry != null)
                    {
                        // Lägg till i början så senaste visas först
                        App.Current?.Dispatcher.Invoke(() =>
                        {
                            LogEntries.Insert(0, logEntry);
                            
                            // Begränsa till max 200 entries
                            while (LogEntries.Count > 200)
                            {
                                LogEntries.RemoveAt(LogEntries.Count - 1);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogEntry(LogLevel.Error, "LogViewer", $"Fel vid läsning av {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        private LogEntry ParseLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            try
            {
                // Serilog format: 2025-01-08 14:30:45.123 [INF] Message
                var pattern = @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(\w{3})\] (.+)$";
                var match = Regex.Match(line, pattern);

                if (match.Success)
                {
                    var timestamp = DateTime.Parse(match.Groups[1].Value);
                    var levelStr = match.Groups[2].Value;
                    var message = match.Groups[3].Value;

                    var level = levelStr switch
                    {
                        "DBG" => LogLevel.Debug,
                        "INF" => LogLevel.Information,
                        "WRN" => LogLevel.Warning,
                        "ERR" => LogLevel.Error,
                        "FTL" => LogLevel.Fatal,
                        _ => LogLevel.Information
                    };

                    return new LogEntry
                    {
                        Timestamp = timestamp,
                        Level = level,
                        Source = ExtractSource(message),
                        Message = message
                    };
                }
            }
            catch
            {
                // Ignorera malformatterade rader
            }

            return null;
        }

        private string ExtractSource(string message)
        {
            // Försök extrahera källa från meddelandet
            if (message.Contains("BrowserCleaner:"))
                return "BrowserCleaner";
            if (message.Contains("FileScanner"))
                return "FileScanner";
            if (message.Contains("QuarantineManager"))
                return "QuarantineManager";
            if (message.Contains("ScheduleManager"))
                return "ScheduleManager";
            
            return "FilKollen";
        }

        public void AddLogEntry(LogLevel level, string source, string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Source = source,
                Message = message
            };

            App.Current?.Dispatcher.Invoke(() =>
            {
                LogEntries.Insert(0, entry);
                
                while (LogEntries.Count > 200)
                {
                    LogEntries.RemoveAt(LogEntries.Count - 1);
                }
            });
        }

        public void ClearLogs()
        {
            LogEntries.Clear();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _logWatcher?.Dispose();
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        
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

        public string LevelColor => Level switch
        {
            LogLevel.Debug => "#6C757D",
            LogLevel.Information => "#007BFF", 
            LogLevel.Warning => "#FFC107",
            LogLevel.Error => "#DC3545",
            LogLevel.Fatal => "#6F42C1",
            _ => "#000000"
        };
    }

    public enum LogLevel
    {
        Debug,
        Information,
        Warning,
        Error,
        Fatal
    }
}