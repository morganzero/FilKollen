using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FilKollen.Models;
using FilKollen.Services;
using FilKollen.ViewModels;
using FilKollen.Windows;
using Microsoft.Win32;
using Serilog;
using FilKollen.Commands;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using FilKollen.Models;


namespace FilKollen
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
public event PropertyChangedEventHandler? PropertyChanged;
        private async Task<bool> InitializeApplicationSafelyAsync()
        {
            var initTasks = new Dictionary<string, Func<Task>>
            {
                ["Services"] = InitializeServicesAsync,
                ["UI"] = InitializeUIComponentsAsync,
                ["Security"] = InitializeSecurityComponentsAsync,
                ["Licensing"] = InitializeLicensingAsync,
                ["Monitoring"] = InitializeMonitoringAsync
            };

            var failedComponents = new List<string>();
            var initTimeout = TimeSpan.FromSeconds(30);

            foreach (var task in initTasks)
            {
                try
                {
                    using var cancellationTokenSource = new CancellationTokenSource(initTimeout);

                    _logger.Information($"Initialiserar komponent: {task.Key}");

                    var initTask = task.Value();
                    var timeoutTask = Task.Delay(initTimeout, cancellationTokenSource.Token);

                    var completedTask = await Task.WhenAny(initTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        _logger.Error($"Timeout vid initialisering av {task.Key}");
                        failedComponents.Add($"{task.Key} (Timeout)");
                        continue;
                    }

                    await initTask; // Re-await för att få eventuella exceptions
                    _logger.Information($"✅ {task.Key} initialiserad framgångsrikt");
                }
                catch (Exception ex)
                {
                    _logger.Error($"❌ Initialisering av {task.Key} misslyckades: {ex.Message}");
                    failedComponents.Add($"{task.Key} ({ex.GetType().Name})");

                    // Vissa komponenter är kritiska
                    if (task.Key == "Licensing" || task.Key == "Services")
                    {
                        ShowCriticalErrorDialog($"Kritisk komponent {task.Key} kunde inte initialiseras", ex);
                        return false;
                    }
                }
            }

            if (failedComponents.Any())
            {
                var message = $"Vissa komponenter kunde inte initialiseras:\n\n{string.Join("\n", failedComponents)}\n\nFilKollen kommer att köras med begränsad funktionalitet.";

                var result = MessageBox.Show(message, "Delvis initialisering",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                {
                    return false;
                }
            }

            return true;
        }

        private void ShowCriticalErrorDialog(string message, Exception ex)
        {
            var detailedMessage = $"{message}\n\n" +
                                 $"Feltyp: {ex.GetType().Name}\n" +
                                 $"Meddelande: {ex.Message}\n\n" +
                                 $"Teknisk information:\n{ex.StackTrace}";

            var errorWindow = new TaskDialog
            {
                WindowTitle = "FilKollen - Kritiskt Fel",
                MainInstruction = "Ett kritiskt fel uppstod vid start",
                Content = message,
                ExpandedInformation = detailedMessage,
                FooterText = "Kontakta support om problemet kvarstår",
                MainIcon = TaskDialogIcon.Error,
                CommonButtons = TaskDialogCommonButtons.OK
            };

            try
            {
                errorWindow.Show();
            }
            catch
            {
                // Fallback om TaskDialog inte fungerar
                MessageBox.Show(detailedMessage, "FilKollen - Kritiskt Fel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // FÖRBÄTTRING: Robust scanning med progress tracking och error recovery
        private async Task<List<ScanResult>> PerformRobustScanningAsync(IProgress<ScanProgress>? progress = null)
        {
            var allResults = new List<ScanResult>();
            var scanProgress = new ScanProgress();

            try
            {
                _logger.Information("🔍 Startar robust säkerhetsskanning...");

                // Få alla sökvägar som ska skannas
                var scanPaths = GetScanPaths();
                scanProgress.TotalPaths = scanPaths.Count;
                progress?.Report(scanProgress);

                var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
                var tasks = new List<Task<List<ScanResult>>>();

                foreach (var path in scanPaths)
                {
                    tasks.Add(ScanPathWithSemaphoreAsync(path, semaphore, scanProgress, progress));
                }

                // Vänta på alla scanning tasks med timeout
                var timeout = TimeSpan.FromMinutes(10);
                var completedTasks = new List<Task<List<ScanResult>>>();

                try
                {
                    var allTasksCompleted = Task.WhenAll(tasks);
                    var timeoutTask = Task.Delay(timeout);

                    var completedTask = await Task.WhenAny(allTasksCompleted, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        _logger.Warning("Scanning timeout - avbryter kvarvarande tasks");

                        // Samla resultat från slutförda tasks
                        completedTasks.AddRange(tasks.Where(t => t.IsCompleted && t.Status == TaskStatus.RanToCompletion));
                    }
                    else
                    {
                        completedTasks.AddRange(tasks);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fel vid parallell skanning: {ex.Message}");

                    // Samla resultat från lyckade tasks
                    completedTasks.AddRange(tasks.Where(t => t.IsCompleted && t.Status == TaskStatus.RanToCompletion));
                }

                // Samla alla resultat från slutförda tasks
                foreach (var task in completedTasks)
                {
                    try
                    {
                        var results = await task;
                        allResults.AddRange(results);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Fel vid hämtning av scan-resultat: {ex.Message}");
                    }
                }

                scanProgress.IsCompleted = true;
                scanProgress.CompletedPaths = scanPaths.Count;
                progress?.Report(scanProgress);

                _logger.Information($"✅ Robust skanning slutförd: {allResults.Count} hot funna");
                return allResults.OrderByDescending(r => r.ThreatLevel).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error($"Kritiskt fel vid robust skanning: {ex.Message}");
                scanProgress.HasError = true;
                scanProgress.ErrorMessage = ex.Message;
                progress?.Report(scanProgress);

                return allResults; // Returnera det vi hann skanna
            }
        }

        private async Task<List<ScanResult>> ScanPathWithSemaphoreAsync(
            string path, SemaphoreSlim semaphore, ScanProgress progress, IProgress<ScanProgress>? progressReporter)
        {
            await semaphore.WaitAsync();

            try
            {
                _logger.Debug($"Skannar sökväg: {path}");

                var results = await _fileScanner.ScanDirectoryAsync(path);

                lock (progress)
                {
                    progress.CompletedPaths++;
                    progress.TotalFilesScanned += results.Count;
                }

                progressReporter?.Report(progress);
                return results;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fel vid skanning av {path}: {ex.Message}");

                lock (progress)
                {
                    progress.CompletedPaths++;
                    progress.FailedPaths++;
                }

                progressReporter?.Report(progress);
                return new List<ScanResult>();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private List<string> GetScanPaths()
        {
            var paths = new List<string>();

            // Standard temp-sökvägar
            var standardPaths = new[]
            {
                Environment.GetEnvironmentVariable("TEMP"),
                Environment.GetEnvironmentVariable("TMP"),
                @"C:\Windows\Temp",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
            };

            foreach (var path in standardPaths)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }

            // Lägg till konfigurerade sökvägar
            if (_config?.ScanPaths != null)
            {
                foreach (var configPath in _config.ScanPaths)
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(configPath);
                    if (Directory.Exists(expandedPath) && !paths.Contains(expandedPath))
                    {
                        paths.Add(expandedPath);
                    }
                }
            }

            return paths;
        }
    }
}
