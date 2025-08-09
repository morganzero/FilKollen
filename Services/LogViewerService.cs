using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Concurrent;
using FilKollen.Models;

namespace FilKollen.Services
{
    // Endast EN partial-klass här. Ingen LogEntry/LogLevel-deklaration i denna fil.
    public partial class LogViewerService : INotifyPropertyChanged, IDisposable
    {
        private readonly string _logDirectory;
        private readonly ConcurrentQueue<LogEntry> _logQueue = new();
        private readonly SemaphoreSlim _logProcessingSemaphore = new(1, 1);
        private FileSystemWatcher? _logWatcher;

        private ObservableCollection<LogEntry> _logEntries = new();
        public ObservableCollection<LogEntry> LogEntries
        {
            get => _logEntries;
            set { _logEntries = value; OnPropertyChanged(); }
        }

        public LogViewerService()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(_logDirectory)) Directory.CreateDirectory(_logDirectory);
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
                System.Diagnostics.Debug.WriteLine($"Failed to setup log file watcher: {ex.Message}");
            }
        }

        private async void OnLogFileChanged(object? sender, FileSystemEventArgs e)
        {
            try { await Task.Delay(100); await LoadLogFileAsync(e.FullPath); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error processing log file change: {ex.Message}"); }
        }

        public async Task LoadExistingLogsAsync()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "*.log")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(5);

                foreach (var logFile in logFiles) await LoadLogFileAsync(logFile);

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
                if (!File.Exists(filePath)) return;
                var lines = await File.ReadAllLinesAsync(filePath);
                var recentLines = lines.TakeLast(50);

                foreach (var line in recentLines)
                {
                    var logEntry = ParseLogLine(line);
                    if (logEntry != null)
                    {
                        if (Application.Current != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                LogEntries.Insert(0, logEntry);
                                while (LogEntries.Count > 200) LogEntries.RemoveAt(LogEntries.Count - 1);
                            });
                        }
                        else
                        {
                            LogEntries.Insert(0, logEntry);
                            while (LogEntries.Count > 200) LogEntries.RemoveAt(LogEntries.Count - 1);
                        }
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
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                var pattern = @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(\w{3})\] (.+)$";
                var m = Regex.Match(line, pattern);
                if (!m.Success) return null;

                var timestamp = DateTime.Parse(m.Groups[1].Value);
                var levelStr = m.Groups[2].Value;
                var message = m.Groups[3].Value;

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
            catch { return null; }
        }

        private string ExtractSource(string message)
        {
            if (message.Contains("BrowserCleaner:")) return "BrowserCleaner";
            if (message.Contains("FileScanner")) return "FileScanner";
            if (message.Contains("QuarantineManager")) return "QuarantineManager";
            if (message.Contains("ScheduleManager")) return "ScheduleManager";
            if (message.Contains("RealTime")) return "RealTime";
            if (message.Contains("Protection")) return "Protection";
            if (message.Contains("Security")) return "Security";
            if (message.Contains("License")) return "License";
            if (message.Contains("Branding")) return "Branding";
            return "FilKollen";
        }

        public void AddLogEntry(LogLevel level, string source, string message)
        {
            var entry = new LogEntry { Timestamp = DateTime.Now, Level = level, Source = source, Message = message };
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LogEntries.Insert(0, entry);
                        while (LogEntries.Count > 200) LogEntries.RemoveAt(LogEntries.Count - 1);
                    });
                }
                else
                {
                    LogEntries.Insert(0, entry);
                    while (LogEntries.Count > 200) LogEntries.RemoveAt(LogEntries.Count - 1);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to add log entry: {ex.Message}"); }
        }

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
            if (level >= LogLevel.Error) _ = Task.Run(ProcessLogQueueAsync);
        }

        private async Task ProcessLogQueueAsync()
        {
            if (!await _logProcessingSemaphore.WaitAsync(100)) return;
            try
            {
                const int MaxBatch = 50;
                var batch = new List<LogEntry>();
                while (_logQueue.TryDequeue(out var e) && batch.Count < MaxBatch) batch.Add(e);
                if (batch.Any()) await ProcessLogBatchAsync(batch);
            }
            finally { _logProcessingSemaphore.Release(); }
        }

        private async Task ProcessLogBatchAsync(List<LogEntry> entries)
        {
            try
            {
                if (Application.Current != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var e in entries) LogEntries.Insert(0, e);
                        while (LogEntries.Count > 500) LogEntries.RemoveAt(LogEntries.Count - 1);
                    });
                }
                else
                {
                    foreach (var e in entries) LogEntries.Insert(0, e);
                    while (LogEntries.Count > 500) LogEntries.RemoveAt(LogEntries.Count - 1);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to process log batch: {ex.Message}"); }
        }

        public void ClearLogs()
        {
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LogEntries.Clear();
                        AddLogEntry(LogLevel.Information, "System", "📝 Loggar rensade av användare");
                    });
                }
                else
                {
                    LogEntries.Clear();
                    AddLogEntry(LogLevel.Information, "System", "📝 Loggar rensade av användare");
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to clear logs: {ex.Message}"); }
        }

        public void ExportLogs(string filePath)
        {
            try
            {
                var lines = new List<string>
                {
                    $"FilKollen Security Log Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    new string('=', 60),
                    ""
                };
                foreach (var e in LogEntries.Reverse())
                    lines.Add($"{e.Timestamp:yyyy-MM-dd HH:mm:ss} [{e.Level}] {e.Source}: {e.Message}");

                File.WriteAllLines(filePath, lines);
                AddLogEntry(LogLevel.Information, "Export", $"📄 Loggar exporterade till: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                AddLogEntry(LogLevel.Error, "Export", $"❌ Misslyckades exportera loggar: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Dispose()
        {
            try { _logWatcher?.Dispose(); _logWatcher = null; }
            catch { }
        }
    }
}
