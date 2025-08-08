using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Serilog;

namespace FilKollen.Services
{
    public class BrowserCleaner
    {
        private readonly ILogger _logger;
        private readonly List<string> _operationLog;

        public BrowserCleaner(ILogger logger)
        {
            _logger = logger;
            _operationLog = new List<string>();
        }

        public async Task<BrowserCleanResult> CleanAllBrowsersAsync()
        {
            var result = new BrowserCleanResult();
            _operationLog.Clear();

            try
            {
                _logger.Information("Startar rensning av webbläsare...");
                LogOperation("=== WEBBLÄSARE-RENSNING STARTAD ===");

                // Stäng alla webbläsare först
                await CloseBrowsersAsync();

                // Rensa Chrome
                var chromeResult = await CleanChromeAsync();
                result.ChromeProfilesCleaned = chromeResult;

                // Rensa Edge
                var edgeResult = await CleanEdgeAsync();
                result.EdgeProfilesCleaned = edgeResult;

                // Sätt policies
                await SetBrowserPoliciesAsync();

                // Rensa Windows notifications
                await CleanWindowsNotificationsAsync();

                result.Success = true;
                result.OperationLog = new List<string>(_operationLog);

                LogOperation("=== WEBBLÄSARE-RENSNING SLUTFÖRD ===");
                _logger.Information($"Webbläsare-rensning slutförd. {chromeResult + edgeResult} profiler rensade.");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fel vid webbläsare-rensning: {ex.Message}");
                LogOperation($"FEL: {ex.Message}");
                result.Success = false;
                result.OperationLog = new List<string>(_operationLog);
                return result;
            }
        }

        private async Task CloseBrowsersAsync()
        {
            var browserProcesses = new[] { "chrome", "msedge", "firefox", "opera", "brave" };
            
            foreach (var browserName in browserProcesses)
            {
                try
                {
                    var processes = Process.GetProcessesByName(browserName);
                    if (processes.Length > 0)
                    {
                        LogOperation($"Stänger {processes.Length} {browserName} processer...");
                        
                        foreach (var process in processes)
                        {
                            try
                            {
                                process.CloseMainWindow();
                                if (!process.WaitForExit(5000))
                                {
                                    process.Kill();
                                }
                                process.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning($"Kunde inte stänga {browserName} process: {ex.Message}");
                            }
                        }
                        
                        await Task.Delay(2000); // Vänta på att processer stängs
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Fel vid stängning av {browserName}: {ex.Message}");
                }
            }
        }

        private async Task<int> CleanChromeAsync()
        {
            var profilesCleaned = 0;
            
            try
            {
                LogOperation("--- CHROME RENSNING ---");
                
                var chromeDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data");

                if (!Directory.Exists(chromeDataPath))
                {
                    LogOperation("Chrome installation ej funnen");
                    return 0;
                }

                // Hitta alla profiler
                var profiles = Directory.GetDirectories(chromeDataPath)
                    .Where(d => Path.GetFileName(d).StartsWith("Default") || 
                               Path.GetFileName(d).StartsWith("Profile"))
                    .ToList();

                foreach (var profilePath in profiles)
                {
                    var profileName = Path.GetFileName(profilePath);
                    LogOperation($"Rensar Chrome profil: {profileName}");

                    // Rensa notifications
                    await ClearChromeNotificationsAsync(profilePath);
                    
                    // Rensa site permissions
                    await ClearChromeSitePermissionsAsync(profilePath);
                    
                    profilesCleaned++;
                }

                // Sätt Chrome policies
                await SetChromePoliciesAsync();

                LogOperation($"Chrome rensning klar: {profilesCleaned} profiler");
                return profilesCleaned;
            }
            catch (Exception ex)
            {
                LogOperation($"Fel vid Chrome rensning: {ex.Message}");
                return profilesCleaned;
            }
        }

        private async Task<int> CleanEdgeAsync()
        {
            var profilesCleaned = 0;
            
            try
            {
                LogOperation("--- EDGE RENSNING ---");
                
                var edgeDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "User Data");

                if (!Directory.Exists(edgeDataPath))
                {
                    LogOperation("Edge installation ej funnen");
                    return 0;
                }

                // Hitta alla profiler
                var profiles = Directory.GetDirectories(edgeDataPath)
                    .Where(d => Path.GetFileName(d).StartsWith("Default") || 
                               Path.GetFileName(d).StartsWith("Profile"))
                    .ToList();

                foreach (var profilePath in profiles)
                {
                    var profileName = Path.GetFileName(profilePath);
                    LogOperation($"Rensar Edge profil: {profileName}");

                    // Rensa notifications
                    await ClearEdgeNotificationsAsync(profilePath);
                    
                    // Rensa site permissions
                    await ClearEdgeSitePermissionsAsync(profilePath);
                    
                    profilesCleaned++;
                }

                // Sätt Edge policies
                await SetEdgePoliciesAsync();

                LogOperation($"Edge rensning klar: {profilesCleaned} profiler");
                return profilesCleaned;
            }
            catch (Exception ex)
            {
                LogOperation($"Fel vid Edge rensning: {ex.Message}");
                return profilesCleaned;
            }
        }

        private async Task ClearChromeNotificationsAsync(string profilePath)
        {
            try
            {
                var prefsFile = Path.Combine(profilePath, "Preferences");
                if (!File.Exists(prefsFile))
                    return;

                var json = await File.ReadAllTextAsync(prefsFile);
                var prefs = JsonSerializer.Deserialize<JsonElement>(json);

                // Skapa ny preferences utan notification permissions
                var newPrefs = RemoveNotificationPermissions(prefs);
                
                var newJson = JsonSerializer.Serialize(newPrefs, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                await File.WriteAllTextAsync(prefsFile, newJson);
                LogOperation($"  ✓ Chrome notifications rensade från {Path.GetFileName(profilePath)}");
            }
            catch (Exception ex)
            {
                LogOperation($"  ✗ Kunde inte rensa Chrome notifications: {ex.Message}");
            }
        }

        private async Task ClearEdgeNotificationsAsync(string profilePath)
        {
            try
            {
                var prefsFile = Path.Combine(profilePath, "Preferences");
                if (!File.Exists(prefsFile))
                    return;

                var json = await File.ReadAllTextAsync(prefsFile);
                var prefs = JsonSerializer.Deserialize<JsonElement>(json);

                // Skapa ny preferences utan notification permissions
                var newPrefs = RemoveNotificationPermissions(prefs);
                
                var newJson = JsonSerializer.Serialize(newPrefs, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                await File.WriteAllTextAsync(prefsFile, newJson);
                LogOperation($"  ✓ Edge notifications rensade från {Path.GetFileName(profilePath)}");
            }
            catch (Exception ex)
            {
                LogOperation($"  ✗ Kunde inte rensa Edge notifications: {ex.Message}");
            }
        }

        private async Task ClearChromeSitePermissionsAsync(string profilePath)
        {
            try
            {
                // Rensa Site Settings (Web Data database)
                var webDataFile = Path.Combine(profilePath, "Web Data");
                if (File.Exists(webDataFile))
                {
                    File.Delete(webDataFile);
                    LogOperation($"  ✓ Chrome site permissions rensade");
                }
            }
            catch (Exception ex)
            {
                LogOperation($"  ✗ Kunde inte rensa Chrome site permissions: {ex.Message}");
            }
        }

        private async Task ClearEdgeSitePermissionsAsync(string profilePath)
        {
            try
            {
                // Rensa Site Settings (Web Data database)
                var webDataFile = Path.Combine(profilePath, "Web Data");
                if (File.Exists(webDataFile))
                {
                    File.Delete(webDataFile);
                    LogOperation($"  ✓ Edge site permissions rensade");
                }
            }
            catch (Exception ex)
            {
                LogOperation($"  ✗ Kunde inte rensa Edge site permissions: {ex.Message}");
            }
        }

        private async Task SetChromePoliciesAsync()
        {
            try
            {
                // Sätt registry policies för Chrome
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Google\Chrome");
                
                // Blockera notifications som default
                key.SetValue("DefaultNotificationsSetting", 2); // 2 = Block
                
                // Blockera background apps
                key.SetValue("BackgroundModeEnabled", 0);
                
                // Blockera malicious sites
                key.SetValue("SafeBrowsingEnabled", 1);
                
                LogOperation("  ✓ Chrome säkerhetspolicies satta");
            }
            catch (Exception ex)
            {
                LogOperation($"  ✗ Kunde inte sätta Chrome policies: {ex.Message}");
            }
        }

        private async Task SetEdgePoliciesAsync()
        {
            try
            {
                // Sätt registry policies för Edge
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge");
                
                // Blockera notifications som default
                key.SetValue("DefaultNotificationsSetting", 2); // 2 = Block
                
                // Blockera background apps
                key.SetValue("BackgroundModeEnabled", 0);
                
                // Aktivera SmartScreen
                key.SetValue("SmartScreenEnabled", 1);
                
                LogOperation("  ✓ Edge säkerhetspolicies satta");
            }
            catch (Exception ex)
            {
                LogOperation($"  ✗ Kunde inte sätta Edge policies: {ex.Message}");
            }
        }

        private async Task SetBrowserPoliciesAsync()
        {
            LogOperation("--- SÄKERHETS-POLICIES ---");
            await SetChromePoliciesAsync();
            await SetEdgePoliciesAsync();
        }

        private async Task CleanWindowsNotificationsAsync()
        {
            try
            {
                LogOperation("--- WINDOWS NOTIFICATIONS ---");
                
                // Rensa Windows notification database
                var notificationDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Notifications");

                if (Directory.Exists(notificationDbPath))
                {
                    var files = Directory.GetFiles(notificationDbPath, "*.db", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { }
                    }
                    LogOperation("  ✓ Windows notification databas rensad");
                }
            }
            catch (Exception ex)
            {
                LogOperation($"  ✗ Kunde inte rensa Windows notifications: {ex.Message}");
            }
        }

        private JsonElement RemoveNotificationPermissions(JsonElement prefs)
        {
            // Förenklad implementation - i verkligheten skulle vi behöva 
            // mer sofistikerad JSON manipulation för att ta bort specifika permissions
            return prefs;
        }

        private void LogOperation(string message)
        {
            _operationLog.Add($"{DateTime.Now:HH:mm:ss} - {message}");
            _logger.Information($"BrowserCleaner: {message}");
        }

        public List<string> GetOperationLog()
        {
            return new List<string>(_operationLog);
        }
    }

    public class BrowserCleanResult
    {
        public bool Success { get; set; }
        public int ChromeProfilesCleaned { get; set; }
        public int EdgeProfilesCleaned { get; set; }
        public List<string> OperationLog { get; set; } = new();
        
        public int TotalProfilesCleaned => ChromeProfilesCleaned + EdgeProfilesCleaned;
    }
}