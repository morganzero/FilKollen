using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FilKollen.Models;
using Serilog;

namespace FilKollen.Services
{
    public class ScheduleManager
    {
        private readonly ILogger _logger;
        private const string TaskName = "FilKollen_AutoScan";

        public ScheduleManager(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<bool> CreateScheduledTaskAsync(AppConfig config)
        {
            try
            {
                // Först ta bort befintlig task
                await DeleteScheduledTaskAsync();

                // Sätt upp parametrar för schtasks
                var frequency = config.Frequency switch
                {
                    ScheduleFrequency.Daily => "DAILY",
                    ScheduleFrequency.Weekly => "WEEKLY",
                    ScheduleFrequency.Monthly => "MONTHLY",
                    _ => "DAILY"
                };

                var time = config.ScheduledTime.ToString(@"HH\:mm");
                var execPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                // Skapa scheduled task med Windows schtasks
                var args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{execPath}\\\" --scheduled\" " +
                          $"/SC {frequency} /ST {time} /RL HIGHEST /F";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        _logger.Information($"Schemalagd task skapad: {config.Frequency} kl {config.ScheduledTime}");
                        return true;
                    }
                    else
                    {
                        var error = await process.StandardError.ReadToEndAsync();
                        _logger.Error($"Fel vid skapande av task: {error}");
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid skapande av schemalagd task: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteScheduledTaskAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{TaskName}\" /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        _logger.Information("Schemalagd task raderad");
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid radering av schemalagd task: {ex.Message}");
                return false;
            }
        }

        public bool IsTaskScheduled()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch { }
            
            return false;
        }

        public DateTime? GetNextRunTime()
        {
            // Förenklad - räkna ut nästa körning baserat på schema
            if (IsTaskScheduled())
            {
                // Returnera morgon imorgon kl 02:00 som exempel
                return DateTime.Today.AddDays(1).Add(TimeSpan.FromHours(2));
            }
            
            return null;
        }
    }
}