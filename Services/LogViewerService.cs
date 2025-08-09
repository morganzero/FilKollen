using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using FilKollen.Models;
using Timer = System.Timers.Timer;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FilKollen.Services
{

    public partial class LogViewerService
    {
        private readonly ConcurrentQueue<LogEntry> _logQueue = new();
        private readonly Timer _logProcessingTimer;
        private readonly SemaphoreSlim _logProcessingSemaphore = new(1, 1);
        
        // FÖRBÄTTRING: Asynkron log-bearbetning för bättre prestanda
        public async Task AddLogEntryAsync(LogLevel level, string source, string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Source = source,
                Message = message,
                ThreadId = Environment.CurrentManagedThreadId
            };

            _logQueue.Enqueue(entry);
            
            // Trigger immediate processing för kritiska meddelanden
            if (level >= LogLevel.Error)
            {
                _ = Task.Run(ProcessLogQueueAsync);
            }
        }

        private async Task ProcessLogQueueAsync()
        {
            if (!await _logProcessingSemaphore.WaitAsync(100))
                return; // Undvik concurrent processing
            
            try
            {
                const int MaxBatchSize = 50;
                var batch = new List<LogEntry>();
                
                while (_logQueue.TryDequeue(out var entry) && batch.Count < MaxBatchSize)
                {
                    batch.Add(entry);
                }
                
                if (batch.Any())
                {
                    await ProcessLogBatchAsync(batch);
                }
            }
            finally
            {
                _logProcessingSemaphore.Release();
            }
        }

        private async Task ProcessLogBatchAsync(List<LogEntry> logEntries)
        {
            try
            {
                await Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var entry in logEntries)
                    {
                        LogEntries.Insert(0, entry);
                    }
                    
                    // Trim till max antal entries för minnesoptimering
                    while (LogEntries.Count > 500)
                    {
                        LogEntries.RemoveAt(LogEntries.Count - 1);
                    }
                }) ?? Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to process log batch: {ex.Message}");
            }
        }
    }
public LogLevel Level { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int ThreadId { get; set; }
        
        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");
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

    public partial class LogViewerService : INotifyPropertyChanged, IDisposable
    {
        private readonly string _logDirectory;
        private ObservableCollection<LogEntry> _logEntries;
        private FileSystemWatcher? _logWatcher;

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
            _ = Task.Run(LoadExistingLogsAsync);
        }

        private void SetupFileWatcher()
        {
            try
            {
                _logWatcher = new FileSystemWatcher(_logDirectory, "*.log")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                _logWatcher.Changed += OnLogFileChanged;
                _logWatcher.Created += OnLogFileChanged;
            }
            catch (Exception ex)
            {
                // Logga fel men fortsätt utan file watcher
                System.Diagnostics.Debug.WriteLine($"Failed to setup log file watcher: {ex.Message}");
            }
        }

        private async void OnLogFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Vänta lite för att filen ska slutföras
                await Task.Delay(100);
                await LoadLogFileAsync(e.FullPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing log file change: {ex.Message}");
            }
        }

        public async Task LoadExistingLogsAsync()
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

                // Lägg till välkomstmeddelande om inga loggar finns
                if (!LogEntries.Any())
                {
                    AddLogEntry(LogLevel.Information, "FilKollen", "🛡️ FilKollen Real-time Security startad");
                    AddLogEntry(LogLevel.Information, "System", "Systemkontroll genomförd - redo för säkerhetsskanning");
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
                        Application.Current?.Dispatcher.Invoke(() =>
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

        private LogEntry? ParseLogLine(string line)
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
            if (message.Contains("RealTime"))
                return "RealTime";
            if (message.Contains("Protection"))
                return "Protection";
            if (message.Contains("Security"))
                return "Security";
            if (message.Contains("License"))
                return "License";
            if (message.Contains("Branding"))
                return "Branding";

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

            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    LogEntries.Insert(0, entry);

                    while (LogEntries.Count > 200)
                    {
                        LogEntries.RemoveAt(LogEntries.Count - 1);
                    }
                });
            }
            catch (Exception ex)
            {
                // Fallback om Dispatcher inte är tillgängligt
                System.Diagnostics.Debug.WriteLine($"Failed to add log entry: {ex.Message}");
            }
        }

        public void ClearLogs()
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    LogEntries.Clear();
                    AddLogEntry(LogLevel.Information, "System", "📝 Loggar rensade av användare");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear logs: {ex.Message}");
            }
        }

        public void ExportLogs(string filePath)
        {
            try
            {
                var lines = new List<string>();
                lines.Add($"FilKollen Security Log Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                lines.Add("=".PadRight(60, '='));
                lines.Add("");

                foreach (var entry in LogEntries.Reverse())
                {
                    lines.Add($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Source}: {entry.Message}");
                }

                File.WriteAllLines(filePath, lines);
                AddLogEntry(LogLevel.Information, "Export", $"📄 Loggar exporterade till: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                AddLogEntry(LogLevel.Error, "Export", $"❌ Misslyckades exportera loggar: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                _logWatcher?.Dispose();
                _logWatcher = null;
            }
            catch
            {
                // Ignorera fel vid disposal
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
public LogLevel Level { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        
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
}