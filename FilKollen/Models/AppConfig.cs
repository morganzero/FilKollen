using System;
using System.Collections.Generic;

namespace FilKollen.Models
{
    public class AppConfig
    {
        public bool AutoDelete { get; set; } = false;
        public int QuarantineDays { get; set; } = 30;
        public bool EnableScheduling { get; set; } = false;
        public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
        public TimeSpan ScheduledTime { get; set; } = new TimeSpan(2, 0, 0);
        public List<string> ScanPaths { get; set; } = new();
        public List<string> SuspiciousExtensions { get; set; } = new();
        public List<string> WhitelistPaths { get; set; } = new();
        public bool ShowNotifications { get; set; } = true;
        public bool PlaySoundAlerts { get; set; } = false;
    }
    
    public enum ScheduleFrequency
    {
        Daily,
        Weekly,
        Monthly
    }
}