using System;
using System.Threading.Tasks;
using Microsoft.Win32.TaskScheduler;
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
                using var ts = new TaskService();
                
                // Ta bort befintlig task om den finns
                ts.RootFolder.DeleteTask(TaskName, false);
                
                var td = ts.NewTask();
                td.RegistrationInfo.Description = "FilKollen automatisk skanning";
                td.RegistrationInfo.Author = "FilKollen";
                
                // Sätt trigger baserat på frekvens
                Trigger trigger = config.Frequency switch
                {
                    ScheduleFrequency.Daily => new DailyTrigger 
                    { 
                        StartBoundary = DateTime.Today.Add(config.ScheduledTime),
                        DaysInterval = 1 
                    },
                    ScheduleFrequency.Weekly => new WeeklyTrigger 
                    { 
                        StartBoundary = DateTime.Today.Add(config.ScheduledTime),
                        WeeksInterval = 1,
                        DaysOfWeek = DaysOfTheWeek.Monday 
                    },
                    ScheduleFrequency.Monthly => new MonthlyTrigger 
                    { 
                        StartBoundary = DateTime.Today.Add(config.ScheduledTime),
                        MonthsOfYear = MonthsOfTheYear.AllMonths,
                        DaysOfMonth = new[] { 1 }
                    },
                    _ => throw new ArgumentException("Okänd schema-frekvens")
                };
                
                td.Triggers.Add(trigger);
                
                // Sätt action - kör FilKollen med --scheduled parameter
                var execPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                td.Actions.Add(new ExecAction(execPath, "--scheduled"));
                
                // Kör med högsta rättigheter
                td.Principal.RunLevel = TaskRunLevel.Highest;
                td.Settings.AllowDemandStart = true;
                td.Settings.AllowHardTerminate = false;
                
                ts.RootFolder.RegisterTaskDefinition(TaskName, td);
                
                _logger.Information($"Schemalagd task skapad: {config.Frequency} kl {config.ScheduledTime}");
                return true;
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
                using var ts = new TaskService();
                ts.RootFolder.DeleteTask(TaskName, false);
                
                _logger.Information("Schemalagd task raderad");
                return true;
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
                using var ts = new TaskService();
                var task = ts.GetTask(TaskName);
                return task != null && task.Enabled;
            }
            catch
            {
                return false;
            }
        }

        public DateTime? GetNextRunTime()
        {
            try
            {
                using var ts = new TaskService();
                var task = ts.GetTask(TaskName);
                return task?.NextRunTime;
            }
            catch
            {
                return null;
            }
        }
    }
}
