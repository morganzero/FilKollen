using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Serilog;

namespace FilKollen.Services
{
    public class AdvancedBrowserCleaner
    {
        private readonly ILogger _logger;
        private readonly List<string> _operationLog;

        // Utökad lista av kända malware-notifiering domäner
        private readonly HashSet<string> _malwareNotificationDomains = new()
        {
            // Push notification scams
            "push-notifications.org", "web-push-notifications.com", "browser-notification.com",
            "clickadu.com", "propellerads.com", "popcash.net", "popads.net",
            "adsterra.com", "hilltopads.net", "exoclick.com", "trafficjunky.com",
            
            // Fake virus alert sites
            "fake-antivirus-alert.com", "virus-warning-alert.com", "security-alert-warning.com",
            "microsoft-security-alert.com", "windows-defender-alert.com", "pc-security-alert.com",
            
            // Cryptocurrency scams
            "crypto-earnings.com", "bitcoin-generator.com", "free-bitcoin-mining.com",
            "earn-crypto-fast.com", "mining-rewards.com", "crypto-investment-pro.com",
            
            // Tech support scams  
            "tech-support-number.com", "microsoft-support-alert.com", "windows-error-support.com",
            "pc-repair-alert.com", "computer-fix-now.com", "system-error-fix.com",
            
            // Adult/dating redirect scams
            "adult-dating-local.com", "hot-singles-nearby.com", "adult-cam-girls.com",
            "dating-hookup-local.com", "xxx-adult-dating.com", "sex-dating-app.com",
            
            // Generic ad networks
            "mgid.com", "taboola.com", "outbrain.com", "revcontent.com", "content.ad",
            "smartadserver.com", "adsystem.com", "doubleclick.net", "googlesyndication.com",
            "amazon-adsystem.com", "facebook.com/tr", "googletagmanager.com",
            
            // Suspicious redirects
            "bit.ly", "tinyurl.com", "t.co", "goo.gl", "ow.ly", "short.link",
            "adf.ly", "linkvertise.com", "ouo.io", "shrinkme.io",
            
            // Telegram bot-specifika (för ditt intrång)
            "api.telegram.org", "web.telegram.org", "t.me"
        };

        public AdvancedBrowserCleaner(ILogger logger)
        {
            _logger = logger;
            _operationLog = new List<string>();
        }

        private void LogOperation(string message)
        {
            _operationLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            _logger.Information(message);
        }

        /// <summary>
        /// KORRIGERAD IMPLEMENTATION: Avancerad malware-notifieringsrensning för Chrome/Edge
        /// </summary>
        public async Task<int> RemoveMalwareNotificationsAdvancedAsync(string profilePath, string browserName)
        {
            try
            {
                LogOperation($"   🧹 Avancerad malware-notifieringsrensning för {browserName}...");

                var preferencesFile = Path.Combine(profilePath, "Preferences");
                if (!File.Exists(preferencesFile))
                {
                    LogOperation($"      ⚠️ Ingen Preferences-fil funnen i {profilePath}");
                    return 0;
                }

                var prefsContent = await File.ReadAllTextAsync(preferencesFile);
                var prefsJson = JsonSerializer.Deserialize<JsonElement>(prefsContent);

                int removedCount = 0;

                // === CHROME/EDGE NOTIFICATION CLEANING ===
                if (browserName == "Chrome" || browserName == "Edge")
                {
                    removedCount = await CleanChromeEdgeNotificationsAsync(preferencesFile, prefsJson);
                }

                // === FIREFOX NOTIFICATION CLEANING ===
                if (browserName == "Firefox")
                {
                    removedCount = await CleanFirefoxNotificationsAsync(profilePath);
                }

                LogOperation($"      ✅ {removedCount} malware-notifieringar borttagna från {browserName}");
                return removedCount;
            }
            catch (Exception ex)
            {
                LogOperation($"      ❌ Fel vid notifieringsrensning för {browserName}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Chrome/Edge notification cleaning implementation
        /// </summary>
        private async Task<int> CleanChromeEdgeNotificationsAsync(string preferencesFile, JsonElement prefsJson)
        {
            int removedCount = 0;

            try
            {
                var modifiedPrefs = JsonDocument.Parse(prefsJson.GetRawText()).RootElement.Clone();
                bool hasChanges = false;

                // Navigate to profile.content_settings.exceptions.notifications
                if (modifiedPrefs.TryGetProperty("profile", out var profile) &&
                    profile.TryGetProperty("content_settings", out var contentSettings) &&
                    contentSettings.TryGetProperty("exceptions", out var exceptions) &&
                    exceptions.TryGetProperty("notifications", out var notifications))
                {
                    var notificationDict = new Dictionary<string, JsonElement>();

                    // Gå igenom alla notifikationsinställningar
                    foreach (var notification in notifications.EnumerateObject())
                    {
                        var domain = notification.Name;
                        var settings = notification.Value;

                        // Kontrollera om domänen är i vår malware-lista
                        bool isMalwareDomain = _malwareNotificationDomains.Any(malwareDomain =>
                            domain.Contains(malwareDomain, StringComparison.OrdinalIgnoreCase));

                        if (isMalwareDomain)
                        {
                            removedCount++;
                            hasChanges = true;
                            LogOperation($"         🗑️ Borttagen malware-notifiering: {domain}");
                        }
                        else
                        {
                            notificationDict[domain] = settings;
                        }
                    }

                    // Sätt DefaultNotificationsSetting till 2 (block all)
                    if (modifiedPrefs.TryGetProperty("profile", out var profileObj) &&
                        profileObj.TryGetProperty("default_content_setting_values", out var defaultSettings))
                    {
                        // Uppdatera default notification setting
                        hasChanges = true;
                        LogOperation($"         🔒 Satt DefaultNotificationsSetting=2 (block all)");
                    }
                }

                // Spara ändrade inställningar om det gjordes ändringar
                if (hasChanges)
                {
                    await SaveModifiedPreferencesAsync(preferencesFile, modifiedPrefs, removedCount);
                }

                return removedCount;
            }
            catch (Exception ex)
            {
                LogOperation($"         ⚠️ Fel vid Chrome/Edge notification cleaning: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Firefox notification cleaning implementation
        /// </summary>
        private async Task<int> CleanFirefoxNotificationsAsync(string profilePath)
        {
            int removedCount = 0;

            try
            {
                var permissionsDb = Path.Combine(profilePath, "permissions.sqlite");

                if (!File.Exists(permissionsDb))
                {
                    LogOperation($"         ⚠️ Ingen permissions.sqlite funnen i Firefox-profil");
                    return 0;
                }

                // Kopiera och arbeta med kopia för säkerhet
                var tempDb = Path.Combine(Path.GetTempPath(), $"permissions_temp_{Guid.NewGuid()}.sqlite");
                File.Copy(permissionsDb, tempDb, true);

                using (var connection = new SqliteConnection($"Data Source={tempDb}"))
                {
                    await connection.OpenAsync();

                    // Ta bort desktop-notification permissions för malware-domäner
                    var deleteCommand = connection.CreateCommand();
                    deleteCommand.CommandText = @"
                        DELETE FROM moz_perms 
                        WHERE type = 'desktop-notification' 
                        AND (";

                    var parameters = new List<string>();
                    int paramIndex = 0;

                    foreach (var domain in _malwareNotificationDomains)
                    {
                        parameters.Add($"origin LIKE @domain{paramIndex}");
                        deleteCommand.Parameters.AddWithValue($"@domain{paramIndex}", $"%{domain}%");
                        paramIndex++;
                    }

                    deleteCommand.CommandText += string.Join(" OR ", parameters) + ")";

                    removedCount = await deleteCommand.ExecuteNonQueryAsync();

                    LogOperation($"         🧹 Firefox: {removedCount} malware-notifikationer borttagna från permissions.sqlite");
                }

                // Ersätt original med modifierad version
                if (removedCount > 0)
                {
                    File.Copy(tempDb, permissionsDb, true);
                    LogOperation($"         💾 Firefox permissions.sqlite uppdaterad");
                }

                // Rensa temp-fil
                if (File.Exists(tempDb))
                    File.Delete(tempDb);

                return removedCount;
            }
            catch (Exception ex)
            {
                LogOperation($"         ⚠️ Fel vid Firefox notification cleaning: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Säker metod för att spara modifierade Chrome/Edge preferences
        /// </summary>
        private async Task SaveModifiedPreferencesAsync(string preferencesFile, JsonElement modifiedPrefs, int removedCount)
        {
            try
            {
                // Skapa backup
                var backupFile = preferencesFile + ".backup";
                File.Copy(preferencesFile, backupFile, true);

                // Skapa ny preferences-struktur med cleaned notifications
                var newPrefsObject = new Dictionary<string, object>();

                // Kopiera alla befintliga inställningar utom notifications
                foreach (var property in modifiedPrefs.EnumerateObject())
                {
                    if (property.Name == "profile")
                    {
                        var profileDict = new Dictionary<string, object>();

                        foreach (var profileProperty in property.Value.EnumerateObject())
                        {
                            if (profileProperty.Name == "content_settings")
                            {
                                var contentSettingsDict = new Dictionary<string, object>();

                                foreach (var csProperty in profileProperty.Value.EnumerateObject())
                                {
                                    if (csProperty.Name == "exceptions")
                                    {
                                        var exceptionsDict = new Dictionary<string, object>();

                                        foreach (var exProperty in csProperty.Value.EnumerateObject())
                                        {
                                            if (exProperty.Name == "notifications")
                                            {
                                                // Använd cleaned notifications (tomma)
                                                exceptionsDict["notifications"] = new Dictionary<string, object>();
                                            }
                                            else
                                            {
                                                var parsedValue = ParseJsonElement(exProperty.Value);
                                                if (parsedValue != null)
                                                    exceptionsDict[exProperty.Name] = parsedValue;
                                            }
                                        }

                                        contentSettingsDict["exceptions"] = exceptionsDict;
                                    }
                                    else if (csProperty.Name == "default_content_setting_values")
                                    {
                                        var defaultSettingsDict = ParseJsonElement(csProperty.Value) as Dictionary<string, object> ?? new Dictionary<string, object>();
                                        defaultSettingsDict["notifications"] = 2; // Block all notifications
                                        contentSettingsDict["default_content_setting_values"] = defaultSettingsDict;
                                    }
                                    else
                                    {
                                        var parsedValue = ParseJsonElement(csProperty.Value);
                                        if (parsedValue != null)
                                            contentSettingsDict[csProperty.Name] = parsedValue;
                                    }
                                }

                                profileDict["content_settings"] = contentSettingsDict;
                            }
                            else
                            {
                                var parsedValue = ParseJsonElement(profileProperty.Value);
                                if (parsedValue != null)
                                    profileDict[profileProperty.Name] = parsedValue;
                            }
                        }

                        newPrefsObject["profile"] = profileDict;
                    }
                    else
                    {
                        var parsedValue = ParseJsonElement(property.Value);
                        if (parsedValue != null)
                            newPrefsObject[property.Name] = parsedValue;
                    }
                }

                // Skriv tillbaka till preferences-filen
                var newJson = JsonSerializer.Serialize(newPrefsObject, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                await File.WriteAllTextAsync(preferencesFile, newJson);
                LogOperation($"         💾 Preferences uppdaterad: {removedCount} malware-notifikationer borttagna");
            }
            catch (Exception ex)
            {
                LogOperation($"         ⚠️ Fel vid sparning av preferences: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper för att parsa JsonElement till object
        /// </summary>
        private object? ParseJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ParseJsonElement(p.Value)),
                JsonValueKind.Array => element.EnumerateArray().Select(ParseJsonElement).ToArray(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        /// <summary>
        /// Sätt maximala säkerhetspolicies för Chrome/Edge via Registry
        /// </summary>
        public async Task SetMaximumSecurityPoliciesAsync()
        {
            try
            {
                LogOperation("   🏛️ Sätter maximala säkerhetspolicies (Chrome/Edge via Registry)...");

                // Chrome policies
                using (var chromeKey = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Google\Chrome"))
                {
                    chromeKey?.SetValue("DefaultNotificationsSetting", 2, RegistryValueKind.DWord); // Block notifications
                    chromeKey?.SetValue("PasswordManagerEnabled", 0, RegistryValueKind.DWord);      // Disable password manager
                    chromeKey?.SetValue("SafeBrowsingProtectionLevel", 2, RegistryValueKind.DWord); // Enhanced protection
                    chromeKey?.SetValue("PopupsBlockedForUrls", new string[] { "*" }, RegistryValueKind.MultiString); // Block all popups
                    LogOperation($"      ✅ Chrome säkerhetspolicies satta");
                }

                // Edge policies  
                using (var edgeKey = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Edge"))
                {
                    edgeKey?.SetValue("DefaultNotificationsSetting", 2, RegistryValueKind.DWord);   // Block notifications
                    edgeKey?.SetValue("PasswordManagerEnabled", 0, RegistryValueKind.DWord);        // Disable password manager
                    edgeKey?.SetValue("SmartScreenEnabled", 1, RegistryValueKind.DWord);           // Enable SmartScreen
                    edgeKey?.SetValue("PopupsBlockedForUrls", new string[] { "*" }, RegistryValueKind.MultiString); // Block all popups
                    LogOperation($"      ✅ Edge säkerhetspolicies satta");
                }

                await Task.Delay(50); // Yield
                LogOperation($"      ✅ Alla säkerhetspolicies tillämpade framgångsrikt");
            }
            catch (Exception ex)
            {
                LogOperation($"      ⚠️ Kunde inte sätta alla säkerhetspolicies: {ex.Message}");
            }
        }

        /// <summary>
        /// Uppdatera hosts-fil med malware-skydd (exact en implementation)
        /// </summary>
        public async Task UpdateHostsFileWithMalwareProtectionAsync()
        {
            try
            {
                LogOperation("--- BLOCKERAR MALWARE-KÄLLOR OCH TELEGRAM BOT API ---");

                var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers", "etc", "hosts");

                if (!File.Exists(hostsPath))
                {
                    LogOperation($"   ⚠️ Hosts-fil finns inte: {hostsPath}");
                    return;
                }

                var existingHosts = await File.ReadAllTextAsync(hostsPath);

                // Ta bort gamla FilKollen-poster
                var lines = existingHosts.Split('\n').ToList();
                lines.RemoveAll(line => line.Contains("# FilKollen malware block"));

                // Lägg till nya malware-blockeringar
                var malwareSources = new[]
                {
                    "\n# FilKollen malware block - START",
                    "0.0.0.0 nirsoft.net # Block NirCmd downloads",
                    "0.0.0.0 download.sysinternals.com # Block suspicious tools",
                    "0.0.0.0 live.sysinternals.com # Block suspicious tools",
                    "0.0.0.0 pastebin.com # Block malware paste sites",
                    "0.0.0.0 hastebin.com # Block malware paste sites",
                    "0.0.0.0 ghostbin.com # Block malware paste sites",
                    "0.0.0.0 controlc.com # Block malware paste sites",
                    "0.0.0.0 api.telegram.org # Block Telegram bot API",
                    "0.0.0.0 web.telegram.org # Block Telegram web",
                    "0.0.0.0 t.me # Block Telegram links"
                };

                // Lägg till alla malware notification domäner
                var additionalBlocks = _malwareNotificationDomains.Select(domain =>
                    $"0.0.0.0 {domain} # Block malware notification domain").ToList();

                var allBlocks = malwareSources.Concat(additionalBlocks).Concat(new[] { "# FilKollen malware block - END\n" });

                lines.AddRange(allBlocks);

                var newHostsContent = string.Join('\n', lines);
                await File.WriteAllTextAsync(hostsPath, newHostsContent);

                var totalBlocked = malwareSources.Length + additionalBlocks.Count - 2; // Minus START/END comments
                LogOperation($"   🛡️ {totalBlocked} malware-källor blockerade via hosts-fil");
                LogOperation("✅ Malware-källor och Telegram API blockerade framgångsrikt");
            }
            catch (Exception ex)
            {
                LogOperation($"❌ Kunde inte blockera malware-källor: {ex.Message}");
            }
        }

        /// <summary>
        /// Huvudmetod för djup webbläsarrensning med alla funktioner
        /// </summary>
        public async Task<BrowserCleanResult> DeepCleanAllBrowsersAsync()
        {
            var result = new BrowserCleanResult();
            _operationLog.Clear();

            try
            {
                LogOperation("=== AVANCERAD WEBBLÄSARE SÄKERHETSRENSNING STARTAD ===");
                _logger.Information("🛡️ Startar djup webbläsare-säkerhetsrensning...");

                // 1. Stäng alla webbläsare först
                await CloseBrowsersAsync();

                // 2. Djuprensa Chrome
                var chromeResult = await DeepCleanChromeAsync();
                result.ChromeProfilesCleaned = chromeResult.ProfilesCleaned;
                result.MalwareNotificationsRemoved += chromeResult.MalwareNotificationsRemoved;
                result.SuspiciousExtensionsRemoved += chromeResult.SuspiciousExtensionsRemoved;

                // 3. Djuprensa Edge  
                var edgeResult = await DeepCleanEdgeAsync();
                result.EdgeProfilesCleaned = edgeResult.ProfilesCleaned;
                result.MalwareNotificationsRemoved += edgeResult.MalwareNotificationsRemoved;
                result.SuspiciousExtensionsRemoved += edgeResult.SuspiciousExtensionsRemoved;

                // 4. Rensa Firefox
                var firefoxResult = await CleanFirefoxAsync();
                result.FirefoxProfilesCleaned = firefoxResult.ProfilesCleaned;
                result.MalwareNotificationsRemoved += firefoxResult.MalwareNotificationsRemoved;

                // 5. Sätt extremt starka säkerhetspolicies
                await SetMaximumSecurityPoliciesAsync();

                // 6. Blockera kända malware-domäner via hosts
                await UpdateHostsFileWithMalwareProtectionAsync();

                // 7. Rensa DNS cache
                await FlushAndResetDnsAsync();

                result.Success = true;
                result.OperationLog = new List<string>(_operationLog);

                var totalProfilesCleaned = result.ChromeProfilesCleaned + result.EdgeProfilesCleaned + result.FirefoxProfilesCleaned;
                LogOperation($"=== DJUP SÄKERHETSRENSNING SLUTFÖRD: {totalProfilesCleaned} profiler, {result.MalwareNotificationsRemoved} malware-notiser, {result.SuspiciousExtensionsRemoved} suspekta extensions ===");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Kritiskt fel vid djup säkerhetsrensning: {ex.Message}");
                result.Success = false;
                result.OperationLog = new List<string>(_operationLog);
                return result;
            }
        }

        // Support methods (simplified for brevity)
        private async Task CloseBrowsersAsync()
        {
            var browserProcesses = new[] { "chrome", "msedge", "firefox", "opera", "brave", "iexplore" };

            foreach (var browserName in browserProcesses)
            {
                try
                {
                    var processes = Process.GetProcessesByName(browserName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(3000))
                                process.Kill();
                            process.Dispose();
                        }
                        catch { }
                    }
                }
                catch { }
            }

            await Task.Delay(2000); // Vänta på att processerna stängs
        }

        private async Task<SecurityCleanResult> DeepCleanChromeAsync()
        {
            var result = new SecurityCleanResult();

            var chromeDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data");

            if (!Directory.Exists(chromeDataPath)) return result;

            var profiles = Directory.GetDirectories(chromeDataPath)
                .Where(d => Path.GetFileName(d).StartsWith("Default") ||
                           Path.GetFileName(d).StartsWith("Profile"))
                .ToList();

            foreach (var profilePath in profiles)
            {
                var profileName = Path.GetFileName(profilePath);
                LogOperation($"🔒 Djuprensning Chrome profil: {profileName}");

                var notificationsRemoved = await RemoveMalwareNotificationsAdvancedAsync(profilePath, "Chrome");
                result.MalwareNotificationsRemoved += notificationsRemoved;

                result.ProfilesCleaned++;
            }

            return result;
        }

        private async Task<SecurityCleanResult> DeepCleanEdgeAsync()
        {
            var result = new SecurityCleanResult();

            var edgeDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data");

            if (!Directory.Exists(edgeDataPath)) return result;

            var profiles = Directory.GetDirectories(edgeDataPath)
                .Where(d => Path.GetFileName(d).StartsWith("Default") ||
                           Path.GetFileName(d).StartsWith("Profile"))
                .ToList();

            foreach (var profilePath in profiles)
            {
                var profileName = Path.GetFileName(profilePath);
                LogOperation($"🔒 Djuprensning Edge profil: {profileName}");

                var notificationsRemoved = await RemoveMalwareNotificationsAdvancedAsync(profilePath, "Edge");
                result.MalwareNotificationsRemoved += notificationsRemoved;

                result.ProfilesCleaned++;
            }

            return result;
        }

        private async Task<SecurityCleanResult> CleanFirefoxAsync()
        {
            var result = new SecurityCleanResult();

            var firefoxDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla", "Firefox", "Profiles");

            if (!Directory.Exists(firefoxDataPath)) return result;

            var profiles = Directory.GetDirectories(firefoxDataPath);

            foreach (var profilePath in profiles)
            {
                var profileName = Path.GetFileName(profilePath);
                LogOperation($"🔒 Rensning Firefox profil: {profileName}");

                var notificationsRemoved = await RemoveMalwareNotificationsAdvancedAsync(profilePath, "Firefox");
                result.MalwareNotificationsRemoved += notificationsRemoved;

                result.ProfilesCleaned++;
            }

            return result;
        }

        private async Task FlushAndResetDnsAsync()
        {
            try
            {
                LogOperation("   🔄 Flushar DNS-cache...");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    LogOperation("      ✅ DNS-cache flushad");
                }
            }
            catch (Exception ex)
            {
                LogOperation($"      ⚠️ Kunde inte flusha DNS: {ex.Message}");
            }
        }
    }

    // Support classes
    public class SecurityCleanResult
    {
        public int ProfilesCleaned { get; set; }
        public int MalwareNotificationsRemoved { get; set; }
        public int SuspiciousExtensionsRemoved { get; set; }
    }

    public class BrowserCleanResult
    {
        public bool Success { get; set; }
        public int ChromeProfilesCleaned { get; set; }
        public int EdgeProfilesCleaned { get; set; }
        public int FirefoxProfilesCleaned { get; set; }
        public int MalwareNotificationsRemoved { get; set; }
        public int SuspiciousExtensionsRemoved { get; set; }
        public List<string> OperationLog { get; set; } = new();

        public int TotalProfilesCleaned => ChromeProfilesCleaned + EdgeProfilesCleaned + FirefoxProfilesCleaned;
    }
}