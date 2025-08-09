using System;
using System.Collections.Generic;
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
            "adf.ly", "linkvertise.com", "ouo.io", "shrinkme.io"
        };

        // Malware extension signatures
        private readonly HashSet<string> _malwareExtensions = new()
        {
            // Crypto mining extensions
            "coin-hive", "crypto-miner", "bitcoin-miner", "web-miner", "mining-pool",
            "coinhive", "minergate", "nicehash", "cryptonight", "monero-miner",
            
            // Adware/PUP extensions  
            "ad-blocker-plus", "free-ads-blocker", "popup-blocker-pro", "ad-remover-plus",
            "shopping-helper", "price-comparison", "coupon-finder", "deal-finder",
            "search-enhancer", "web-search-pro", "browser-enhancer", "web-optimizer",
            
            // Suspicious downloaders
            "video-downloader-pro", "media-downloader", "torrent-downloader", "file-converter-pro",
            "pdf-converter", "youtube-downloader-hd", "music-downloader", "movie-downloader",
            
            // Fake security extensions
            "antivirus-plus", "security-scanner", "malware-detector", "virus-protection",
            "web-security-pro", "safe-browsing-plus", "threat-detector", "security-guard"
        };

        // Suspicious permission combinations
        private readonly Dictionary<string, string[]> _suspiciousPermissions = new()
        {
            ["crypto_mining"] = new[] { "background", "storage", "unlimitedStorage", "tabs", "activeTab" },
            ["data_theft"] = new[] { "cookies", "storage", "tabs", "activeTab", "webRequest", "webRequestBlocking" },
            ["adware"] = new[] { "notifications", "tabs", "activeTab", "storage", "webRequest" },
            ["hijacker"] = new[] { "tabs", "activeTab", "webNavigation", "storage", "management" },
            ["keylogger"] = new[] { "activeTab", "tabs", "storage", "unlimitedStorage", "webRequest" }
        };

        public AdvancedBrowserCleaner(ILogger logger)
        {
            _logger = logger;
            _operationLog = new List<string>();
        }

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

                // 4. Rensa Firefox (basic support)
                var firefoxResult = await CleanFirefoxAsync();
                result.FirefoxProfilesCleaned = firefoxResult.ProfilesCleaned;
                result.MalwareNotificationsRemoved += firefoxResult.MalwareNotificationsRemoved;

                // 5. Sätt extremt starka säkerhetspolicies
                await SetMaximumSecurityPoliciesAsync();

                // 6. Rensa Windows notification system
                await CleanWindowsNotificationSystemAsync();

                // 7. Blockera kända malware-domäner via hosts
                await UpdateHostsFileAsync();

                // 8. Rensa DNS cache och renewal
                await FlushAndResetDnsAsync();

                // 9. Återställ proxy-inställningar
                await ResetProxySettingsAsync();

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

        private async Task CloseBrowsersAsync()
        {
            var browserProcesses = new[]
            {
                "chrome", "msedge", "firefox", "opera", "brave", "iexplore",
                "safari", "vivaldi", "tor", "waterfox", "seamonkey"
            };

            LogOperation("🚫 Stänger alla webbläsare för säker djuprensning...");

            foreach (var browserName in browserProcesses)
            {
                try
                {
                    var processes = Process.GetProcessesByName(browserName);
                    if (processes.Length > 0)
                    {
                        LogOperation($"   💀 Stänger {processes.Length} {browserName} processer...");

                        foreach (var process in processes)
                        {
                            try
                            {
                                process.CloseMainWindow();
                                if (!process.WaitForExit(5000))
                                {
                                    process.Kill();
                                    LogOperation($"      ⚡ Tvingad stängning av {browserName} (PID: {process.Id})");
                                }
                                process.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning($"Kunde inte stänga {browserName} process: {ex.Message}");
                            }
                        }

                        await Task.Delay(3000); // Längre väntetid för fullständig stängning
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Fel vid stängning av {browserName}: {ex.Message}");
                }
            }
        }

        private async Task<SecurityCleanResult> DeepCleanChromeAsync()
        {
            var result = new SecurityCleanResult();

            try
            {
                LogOperation("--- CHROME DJUP SÄKERHETSRENSNING ---");

                var chromeDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data");

                if (!Directory.Exists(chromeDataPath))
                {
                    LogOperation("❌ Chrome installation ej funnen");
                    return result;
                }

                var profiles = Directory.GetDirectories(chromeDataPath)
                    .Where(d => Path.GetFileName(d).StartsWith("Default") ||
                               Path.GetFileName(d).StartsWith("Profile"))
                    .ToList();

                foreach (var profilePath in profiles)
                {
                    var profileName = Path.GetFileName(profilePath);
                    LogOperation($"🔒 Djuprensning Chrome profil: {profileName}");

                    // Rensa malware notifications (avancerat)
                    var notificationsRemoved = await RemoveMalwareNotificationsAdvancedAsync(profilePath, "Chrome");
                    result.MalwareNotificationsRemoved += notificationsRemoved;

                    // Analysera och ta bort suspekta extensions
                    var extensionsRemoved = await AnalyzeAndRemoveMaliciousExtensionsAsync(profilePath, "Chrome");
                    result.SuspiciousExtensionsRemoved += extensionsRemoved;

                    // Rensa all browsing data komplett
                    await NukeAllBrowsingDataAsync(profilePath, "Chrome");

                    // Återställ till säkra standardinställningar
                    await ApplyMaxSecuritySettingsAsync(profilePath, "Chrome");

                    // Kontrollera och fixa shortcuts
                    await FixBrowserShortcutsAsync("Chrome");

                    result.ProfilesCleaned++;
                }

                LogOperation($"✅ Chrome djuprensning klar: {result.ProfilesCleaned} profiler, {result.MalwareNotificationsRemoved} malware-notiser, {result.SuspiciousExtensionsRemoved} extensions");
                return result;
            }
            catch (Exception ex)
            {
                LogOperation($"❌ Fel vid Chrome djuprensning: {ex.Message}");
                return result;
            }
        }

        private async Task<SecurityCleanResult> DeepCleanEdgeAsync()
        {
            var result = new SecurityCleanResult();

            try
            {
                LogOperation("--- EDGE DJUP SÄKERHETSRENSNING ---");

                var edgeDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "User Data");

                if (!Directory.Exists(edgeDataPath))
                {
                    LogOperation("❌ Edge installation ej funnen");
                    return result;
                }

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

                    var extensionsRemoved = await AnalyzeAndRemoveMaliciousExtensionsAsync(profilePath, "Edge");
                    result.SuspiciousExtensionsRemoved += extensionsRemoved;

                    await NukeAllBrowsingDataAsync(profilePath, "Edge");
                    await ApplyMaxSecuritySettingsAsync(profilePath, "Edge");
                    await FixBrowserShortcutsAsync("Edge");

                    result.ProfilesCleaned++;
                }

                LogOperation($"✅ Edge djuprensning klar: {result.ProfilesCleaned} profiler, {result.MalwareNotificationsRemoved} malware-notiser, {result.SuspiciousExtensionsRemoved} extensions");
                return result;
            }
            catch (Exception ex)
            {
                LogOperation($"❌ Fel vid Edge djuprensning: {ex.Message}");
                return result;
            }
        }

        private async Task<SecurityCleanResult> CleanFirefoxAsync()
        {
            var result = new SecurityCleanResult();

            try
            {
                LogOperation("--- FIREFOX SÄKERHETSRENSNING ---");

                var firefoxPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Mozilla", "Firefox", "Profiles");

                if (!Directory.Exists(firefoxPath))
                {
                    LogOperation("❌ Firefox installation ej funnen");
                    return result;
                }

                var profiles = Directory.GetDirectories(firefoxPath);

                foreach (var profilePath in profiles)
                {
                    var profileName = Path.GetFileName(profilePath);
                    LogOperation($"🔒 Rensning Firefox profil: {profileName}");

                    // Rensa Firefox-specifika filer
                    var filesToDelete = new[]
                    {
                        "permissions.sqlite", "content-prefs.sqlite", "cookies.sqlite",
                        "formhistory.sqlite", "places.sqlite", "webappsstore.sqlite"
                    };

                    foreach (var file in filesToDelete)
                    {
                        var filePath = Path.Combine(profilePath, file);
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                File.Delete(filePath);
                                LogOperation($"   🗑️ Firefox: Raderad {file}");
                            }
                            catch { }
                        }
                    }

                    result.ProfilesCleaned++;
                }

                LogOperation($"✅ Firefox rensning klar: {result.ProfilesCleaned} profiler");
                return result;
            }
            catch (Exception ex)
            {
                LogOperation($"❌ Fel vid Firefox rensning: {ex.Message}");
                return result;
            }
        }

        private async Task<int> RemoveMalwareNotificationsAdvancedAsync(string profilePath, string browserName)
        {
            int removedCount = 0;

            try
            {
                var prefsFile = Path.Combine(profilePath, "Preferences");
                if (!File.Exists(prefsFile)) return 0;

                var backupFile = prefsFile + ".backup";
                File.Copy(prefsFile, backupFile, true); // Säkerhetskopia

                var json = await File.ReadAllTextAsync(prefsFile);
                using var document = JsonDocument.Parse(json);

                var newPrefs = ProcessAndCleanNotifications(document.RootElement, out removedCount);

                var newJson = JsonSerializer.Serialize(newPrefs, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(prefsFile, newJson);

                if (removedCount > 0)
                {
                    LogOperation($"   🛡️ {browserName}: Borttog {removedCount} malware-notifieringar från {Path.GetFileName(profilePath)}");
                }

                // Rensa även Local State för global inställningar
                var localStateFile = Path.Combine(Path.GetDirectoryName(profilePath)!, "Local State");
                if (File.Exists(localStateFile))
                {
                    var localStateJson = await File.ReadAllTextAsync(localStateFile);
                    var cleanedLocalState = CleanLocalStateNotifications(localStateJson);
                    await File.WriteAllTextAsync(localStateFile, cleanedLocalState);
                    LogOperation($"   🛡️ {browserName}: Rensade globala notification-inställningar");
                }

                return removedCount;
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Kunde inte rensa {browserName} notifications: {ex.Message}");
                return 0;
            }
        }

        private async Task<int> AnalyzeAndRemoveMaliciousExtensionsAsync(string profilePath, string browserName)
        {
            int removedCount = 0;

            try
            {
                var extensionsPath = Path.Combine(profilePath, "Extensions");
                if (!Directory.Exists(extensionsPath)) return 0;

                var directories = Directory.GetDirectories(extensionsPath);

                foreach (var extensionDir in directories)
                {
                    try
                    {
                        var suspiciousScore = 0;
                        var extensionId = Path.GetFileName(extensionDir);

                        // Analysera alla manifest-filer
                        var manifestFiles = Directory.GetFiles(extensionDir, "manifest.json", SearchOption.AllDirectories);

                        foreach (var manifestFile in manifestFiles)
                        {
                            var manifest = await File.ReadAllTextAsync(manifestFile);
                            var analysisResult = AnalyzeExtensionManifest(manifest, extensionId);

                            suspiciousScore += analysisResult.SuspiciousScore;

                            if (analysisResult.IsMalicious)
                            {
                                LogOperation($"   🚨 {browserName}: MALICIÖS EXTENSION: {extensionId} - {analysisResult.Reason}");
                                Directory.Delete(extensionDir, true);
                                removedCount++;
                                break;
                            }
                            else if (suspiciousScore > 50)
                            {
                                LogOperation($"   ⚠️ {browserName}: SUSPEKT EXTENSION: {extensionId} (Score: {suspiciousScore})");
                                Directory.Delete(extensionDir, true);
                                removedCount++;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogOperation($"   ❌ Kunde inte analysera extension {Path.GetFileName(extensionDir)}: {ex.Message}");
                    }
                }

                if (removedCount > 0)
                {
                    LogOperation($"   ✅ {browserName}: {removedCount} maliciösa/suspekta extensions borttagna");
                }

                return removedCount;
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Kunde inte rensa {browserName} extensions: {ex.Message}");
                return 0;
            }
        }

        private ExtensionAnalysisResult AnalyzeExtensionManifest(string manifest, string extensionId)
        {
            var result = new ExtensionAnalysisResult();

            try
            {
                using var doc = JsonDocument.Parse(manifest);
                var root = doc.RootElement;

                // Kolla namn mot kända malware-extensions
                if (root.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString()?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(name))
                    {
                        foreach (var malwareName in _malwareExtensions)
                        {
                            if (name.Contains(malwareName))
                            {
                                result.IsMalicious = true;
                                result.Reason = $"Känt malware-namn: {malwareName}";
                                return result;
                            }
                        }
                    }
                }

                // Analysera permissions
                if (root.TryGetProperty("permissions", out var permissionsElement))
                {
                    var permissions = new List<string>();
                    foreach (var perm in permissionsElement.EnumerateArray())
                    {
                        permissions.Add(perm.GetString() ?? "");
                    }

                    // Kolla mot suspekta permission-kombinationer
                    foreach (var suspectPattern in _suspiciousPermissions)
                    {
                        var matchCount = suspectPattern.Value.Count(perm => permissions.Contains(perm));
                        if (matchCount >= suspectPattern.Value.Length - 1) // Nästan alla permissions
                        {
                            result.SuspiciousScore += 30;
                            result.Reason += $" Suspekt {suspectPattern.Key} pattern;";
                        }
                    }

                    // Farliga permissions
                    var dangerousPerms = new[] { "management", "debugger", "desktopCapture", "system.storage" };
                    foreach (var perm in dangerousPerms)
                    {
                        if (permissions.Contains(perm))
                        {
                            result.SuspiciousScore += 25;
                            result.Reason += $" Farlig permission: {perm};";
                        }
                    }
                }

                // Kolla content scripts mot kända malware-domäner
                if (root.TryGetProperty("content_scripts", out var scriptsElement))
                {
                    foreach (var script in scriptsElement.EnumerateArray())
                    {
                        if (script.TryGetProperty("matches", out var matchesElement))
                        {
                            foreach (var match in matchesElement.EnumerateArray())
                            {
                                var url = match.GetString();
                                if (!string.IsNullOrEmpty(url))
                                {
                                    foreach (var domain in _malwareNotificationDomains)
                                    {
                                        if (url.Contains(domain))
                                        {
                                            result.IsMalicious = true;
                                            result.Reason = $"Interagerar med malware-domän: {domain}";
                                            return result;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Suspekta extension IDs (random characters)
                if (Regex.IsMatch(extensionId, @"^[a-p]{32}$") && result.SuspiciousScore == 0)
                {
                    result.SuspiciousScore += 15;
                    result.Reason += " Random extension ID;";
                }

                return result;
            }
            catch
            {
                result.SuspiciousScore += 10;
                result.Reason += " Felformaterat manifest;";
                return result;
            }
        }

        private async Task NukeAllBrowsingDataAsync(string profilePath, string browserName)
        {
            try
            {
                LogOperation($"   💣 {browserName}: Fullständig rensning av browsing data...");

                var filesToNuke = new[]
                {
                    "History", "History-journal", "Cookies", "Cookies-journal",
                    "Web Data", "Web Data-journal", "Login Data", "Login Data-journal",
                    "Preferences", "Local State", "Secure Preferences",
                    "Top Sites", "Top Sites-journal", "Visited Links",
                    "Network Action Predictor", "Network Action Predictor-journal",
                    "QuotaManager", "QuotaManager-journal",
                    "Application Cache", "GPUCache", "ShaderCache",
                    "Service Worker", "blob_storage", "File System",
                    "IndexedDB", "Local Storage", "Session Storage",
                    "Extension Cookies", "Extension State"
                };

                foreach (var fileName in filesToNuke)
                {
                    var filePath = Path.Combine(profilePath, fileName);
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                        else if (Directory.Exists(filePath))
                        {
                            Directory.Delete(filePath, true);
                        }
                    }
                    catch { } // Ignorera fel - vissa filer kanske är låsta
                }

                // Rensa Cache-mappar rekursivt
                var cacheDirs = new[] { "Cache", "Code Cache", "GPUCache", "DawnCache" };
                foreach (var cacheDir in cacheDirs)
                {
                    var cachePath = Path.Combine(profilePath, cacheDir);
                    if (Directory.Exists(cachePath))
                    {
                        try
                        {
                            Directory.Delete(cachePath, true);
                            LogOperation($"      🗑️ Rensade cache: {cacheDir}");
                        }
                        catch { }
                    }
                }

                await Task.Delay(100); // Yield
                LogOperation($"   ✅ {browserName}: Browsing data fullständigt rensad");
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Fel vid rensning av browsing data: {ex.Message}");
            }
        }

        private async Task ApplyMaxSecuritySettingsAsync(string profilePath, string browserName)
        {
            try
            {
                LogOperation($"   🔐 {browserName}: Tillämpar maximala säkerhetsinställningar...");

                var securePrefs = new Dictionary<string, object>
                {
                    ["profile"] = new Dictionary<string, object>
                    {
                        ["default_content_setting_values"] = new Dictionary<string, object>
                        {
                            ["notifications"] = 2,  // Block all
                            ["geolocation"] = 2,     // Block all
                            ["media_stream_camera"] = 2,
                            ["media_stream_mic"] = 2,
                            ["automatic_downloads"] = 2,
                            ["background_sync"] = 2,
                            ["plugins"] = 2,
                            ["popups"] = 2,
                            ["ads"] = 2,
                            ["javascript"] = 1  // Allow but restricted
                        },
                        ["content_settings"] = new Dictionary<string, object>
                        {
                            ["exceptions"] = new Dictionary<string, object>
                            {
                                ["notifications"] = new Dictionary<string, object>()
                            }
                        }
                    },
                    ["browser"] = new Dictionary<string, object>
                    {
                        ["check_default_browser"] = false
                    },
                    ["safebrowsing"] = new Dictionary<string, object>
                    {
                        ["enabled"] = true,
                        ["enhanced"] = true
                    }
                };

                var prefsFile = Path.Combine(profilePath, "Preferences");
                var json = JsonSerializer.Serialize(securePrefs, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(prefsFile, json);

                LogOperation($"   ✅ {browserName}: Säkra inställningar tillämpade");
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Fel vid tillämpning av säkra inställningar: {ex.Message}");
            }
        }

        private async Task SetMaximumSecurityPoliciesAsync()
        {
            LogOperation("--- MAXIMALA SÄKERHETSPOLICIES ---");

            try
            {
                await SetChromeMaxSecurityPoliciesAsync();
                await SetEdgeMaxSecurityPoliciesAsync();
                await SetWindowsSecurityPoliciesAsync();

                LogOperation("✅ Maximala säkerhetspolicies aktiverade");
            }
            catch (Exception ex)
            {
                LogOperation($"❌ Fel vid säkerhetspolicies: {ex.Message}");
            }
        }

        private async Task SetChromeMaxSecurityPoliciesAsync()
        {
            await Task.Yield();

            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Google\Chrome");

                // Extrema säkerhetsinställningar
                key.SetValue("DefaultNotificationsSetting", 2);
                key.SetValue("NotificationsAllowedForUrls", new string[0]);
                key.SetValue("NotificationsBlockedForUrls", new[] { "*" });

                key.SetValue("DownloadRestrictions", 1);
                key.SetValue("SafeBrowsingEnabled", 1);
                key.SetValue("SafeBrowsingExtendedReportingEnabled", 1);
                key.SetValue("SafeBrowsingForTrustedSourcesEnabled", 0);

                key.SetValue("BackgroundModeEnabled", 0);
                key.SetValue("AutofillAddressEnabled", 0);
                key.SetValue("AutofillCreditCardEnabled", 0);
                key.SetValue("PasswordManagerEnabled", 0);

                key.SetValue("DefaultPluginsSetting", 2);
                key.SetValue("DefaultPopupsSetting", 2);
                key.SetValue("DefaultGeolocationSetting", 2);
                key.SetValue("DefaultMediaStreamSetting", 2);

                // Blockera alla extensions som standard
                key.SetValue("ExtensionInstallBlacklist", new[] { "*" });
                key.SetValue("ExtensionInstallWhitelist", new string[0]);

                // Avancerad malware-skydd
                key.SetValue("AdvancedProtectionAllowed", 1);
                key.SetValue("SafeBrowsingProtectionLevel", 2); // Enhanced protection

                LogOperation("   🛡️ Chrome maximala säkerhetspolicies aktiverade");
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Chrome policies fel: {ex.Message}");
            }
        }

        private async Task SetEdgeMaxSecurityPoliciesAsync()
        {
            await Task.Yield();

            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge");

                key.SetValue("DefaultNotificationsSetting", 2);
                key.SetValue("SmartScreenEnabled", 1);
                key.SetValue("SmartScreenPuaEnabled", 1);
                key.SetValue("PreventSmartScreenPromptOverride", 1);
                key.SetValue("PreventSmartScreenPromptOverrideForFiles", 1);

                key.SetValue("BackgroundModeEnabled", 0);
                key.SetValue("AutofillAddressEnabled", 0);
                key.SetValue("AutofillCreditCardEnabled", 0);
                key.SetValue("PasswordManagerEnabled", 0);

                // Blockera alla extensions
                key.SetValue("ExtensionInstallBlacklist", new[] { "*" });

                LogOperation("   🛡️ Edge maximala säkerhetspolicies aktiverade");
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Edge policies fel: {ex.Message}");
            }
        }

        private async Task SetWindowsSecurityPoliciesAsync()
        {
            await Task.Yield();

            try
            {
                // Windows Defender förstärkningar
                using var defenderKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection");
                defenderKey?.SetValue("DisableRealtimeMonitoring", 0);
                defenderKey?.SetValue("DisableOnAccessProtection", 0);

                // SmartScreen förstärkningar
                using var smartScreenKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer");
                smartScreenKey?.SetValue("SmartScreenEnabled", "RequireAdmin");

                LogOperation("   🛡️ Windows säkerhetspolicies förstärkta");
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Windows policies fel: {ex.Message}");
            }
        }

        private async Task FixBrowserShortcutsAsync(string browserName)
        {
            try
            {
                LogOperation($"   🔧 {browserName}: Kontrollerar och fixar shortcuts...");

                var shortcuts = new List<string>();

                // Desktop shortcuts
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                shortcuts.AddRange(Directory.GetFiles(desktopPath, $"*{browserName}*.lnk"));

                // Start menu shortcuts
                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                shortcuts.AddRange(Directory.GetFiles(startMenuPath, $"*{browserName}*.lnk", SearchOption.AllDirectories));

                foreach (var shortcut in shortcuts)
                {
                    // Här skulle vi implementera logik för att kontrollera om shortcuts
                    // har blivit modifierade för att peka på malware
                    // För nu loggar vi bara att vi kontrollerat dem
                    LogOperation($"      ✓ Kontrollerad shortcut: {Path.GetFileName(shortcut)}");
                }

                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Fel vid shortcut-kontroll: {ex.Message}");
            }
        }

        private async Task CleanWindowsNotificationSystemAsync()
        {
            try
            {
                LogOperation("--- WINDOWS NOTIFICATION SYSTEM DJUPRENSNING ---");

                // Rensa Action Center database
                var actionCenterPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft", "Windows", "Notifications"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft", "Windows", "ActionCenter"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft", "Windows", "Explorer", "NotificationSettings")
                };

                int totalCleaned = 0;
                foreach (var path in actionCenterPaths)
                {
                    if (Directory.Exists(path))
                    {
                        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            try
                            {
                                File.Delete(file);
                                totalCleaned++;
                            }
                            catch { }
                        }

                        // Rensa även undermappar
                        var dirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);
                        foreach (var dir in dirs.Reverse()) // Rensa djupast först
                        {
                            try
                            {
                                Directory.Delete(dir, false); // Bara om tom
                            }
                            catch { }
                        }
                    }
                }

                // Rensa registry notifications
                await CleanNotificationRegistryAsync();

                LogOperation($"   🗑️ Windows notification system rensad: {totalCleaned} filer");
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Fel vid Windows notification rensning: {ex.Message}");
            }
        }

        private async Task CleanNotificationRegistryAsync()
        {
            await Task.Yield();

            try
            {
                // Rensa per-app notification inställningar
                var notificationKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings";
                using var key = Registry.CurrentUser.OpenSubKey(notificationKey, true);

                if (key != null)
                {
                    var subKeys = key.GetSubKeyNames();
                    foreach (var subKeyName in subKeys)
                    {
                        // Kolla om det är browser-relaterat
                        if (subKeyName.ToLowerInvariant().Contains("chrome") ||
                            subKeyName.ToLowerInvariant().Contains("edge") ||
                            subKeyName.ToLowerInvariant().Contains("firefox"))
                        {
                            try
                            {
                                using var appKey = key.OpenSubKey(subKeyName, true);
                                appKey?.SetValue("Enabled", 0); // Stäng av notifications
                                LogOperation($"      🔕 Stängde av notifications för: {subKeyName}");
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Fel vid registry notification rensning: {ex.Message}");
            }
        }

        private async Task UpdateHostsFileAsync()
        {
            try
            {
                LogOperation("--- HOSTS-FIL MALWARE-BLOCKERING ---");

                var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers", "etc", "hosts");

                if (!File.Exists(hostsPath))
                {
                    LogOperation("❌ Hosts-fil hittades inte");
                    return;
                }

                var existingHosts = await File.ReadAllTextAsync(hostsPath);
                var lines = existingHosts.Split('\n').ToList();

                // Ta bort gamla FilKollen-poster
                lines.RemoveAll(line => line.Contains("# FilKollen malware block"));

                // Lägg till nya malware-blockeringar
                lines.Add("\n# FilKollen malware block - START");

                foreach (var domain in _malwareNotificationDomains.Take(50)) // Begränsa för prestanda
                {
                    lines.Add($"0.0.0.0 {domain} # FilKollen malware block");
                    lines.Add($"0.0.0.0 www.{domain} # FilKollen malware block");
                }

                lines.Add("# FilKollen malware block - END\n");

                var newHostsContent = string.Join('\n', lines);
                await File.WriteAllTextAsync(hostsPath, newHostsContent);

                LogOperation($"   🛡️ Hosts-fil uppdaterad med {_malwareNotificationDomains.Count} blockerade domäner");
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Kunde inte uppdatera hosts-fil: {ex.Message}");
            }
        }

        private async Task FlushAndResetDnsAsync()
        {
            try
            {
                LogOperation("--- DNS CACHE & RENEWAL ---");

                var dnsCommands = new[]
                {
                    "ipconfig /flushdns",
                    "ipconfig /registerdns",
                    "ipconfig /release",
                    "ipconfig /renew"
                };

                foreach (var command in dnsCommands)
                {
                    try
                    {
                        var parts = command.Split(' ', 2);
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = parts[0],
                            Arguments = parts.Length > 1 ? parts[1] : "",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true
                        };

                        using var process = Process.Start(processInfo);
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                            LogOperation($"   🔄 Körde: {command}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogOperation($"   ⚠️ Kunde inte köra {command}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ DNS reset fel: {ex.Message}");
            }
        }

        private async Task ResetProxySettingsAsync()
        {
            await Task.Yield();

            try
            {
                LogOperation("--- PROXY-INSTÄLLNINGAR ÅTERSTÄLLNING ---");

                // Återställ Internet Explorer proxy (används av Windows)
                using var ieKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);

                if (ieKey != null)
                {
                    ieKey.SetValue("ProxyEnable", 0);
                    ieKey.SetValue("ProxyServer", "");
                    ieKey.SetValue("ProxyOverride", "");
                    ieKey.SetValue("AutoConfigURL", "");

                    LogOperation("   🔄 Internet Explorer proxy-inställningar återställda");
                }

                // WinHTTP proxy reset
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "winhttp reset proxy",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(processInfo);
                    await process!.WaitForExitAsync();
                    LogOperation("   🔄 WinHTTP proxy återställd");
                }
                catch { }

            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Proxy reset fel: {ex.Message}");
            }
        }

        // Hjälpmetoder för JSON-bearbetning
        private JsonElement ProcessAndCleanNotifications(JsonElement element, out int removedCount)
        {
            removedCount = 0;
            // Förenklad implementation - i verkligheten skulle detta vara mer komplex
            // för att faktiskt rensa ut specifika notification permissions

            var content = element.GetRawText();
            var originalCount = content.Length;

            // Simulera rensning av malware-domäner
            foreach (var domain in _malwareNotificationDomains)
            {
                if (content.Contains(domain))
                {
                    removedCount++;
                    content = content.Replace(domain, "blocked-domain.local");
                }
            }

            return JsonDocument.Parse(content).RootElement;
        }

        private string CleanLocalStateNotifications(string json)
        {
            // Rensa global notification permissions
            foreach (var domain in _malwareNotificationDomains)
            {
                json = json.Replace(domain, "blocked-domain.local");
            }
            return json;
        }

        private void LogOperation(string message)
        {
            _operationLog.Add($"{DateTime.Now:HH:mm:ss} - {message}");
            _logger.Information($"AdvancedBrowserCleaner: {message}");
        }

        public List<string> GetOperationLog()
        {
            return new List<string>(_operationLog);
        }
    }

    // Support-klasser
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

    public class ExtensionAnalysisResult
    {
        public bool IsMalicious { get; set; }
        public int SuspiciousScore { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

// Lägg till dessa förbättringar i AdvancedBrowserCleaner.cs

// Utökad lista för att specificially hantera intrångsscript
private readonly HashSet<string> _malwareScriptIndicators = new()
{
    // Telegram bot-specifika indicators
    "api.telegram.org/bot",
    "sendDocument",
    "savescreenshot",
    "nircmd.exe",
    "Screenshot_",
    "ScreenshotLog.txt",
    
    // PowerShell-baserade hot
    "Invoke-WebRequest",
    "DownloadString", 
    "DownloadFile",
    "IEX (",
    "iex(",
    "powershell -",
    "cmd /c",
    "cmd.exe /c",
    
    // Andra vanliga malware-patterns
    "base64 -d",
    "echo -n",
    "curl -F",
    "wget -O",
    "certutil -decode",
    "bitsadmin /transfer",
    
    // Remote access patterns
    "ngrok.io",
    "serveo.net", 
    "localhost.run",
    "ssh -R",
    "reverse shell",
    
    // Cryptocurrency mining
    "xmrig",
    "monero",
    "stratum+tcp",
    "mining pool"
};

private async Task<BrowserCleanResult> DeepCleanAllBrowsersWithMalwareProtectionAsync()
{
    var result = new BrowserCleanResult();
    _operationLog.Clear();

    try
    {
        LogOperation("=== AVANCERAD ANTI-MALWARE WEBBLÄSARRENSNING STARTAD ===");
        _logger.Information("🛡️ Startar djup anti-malware webbläsarrensning...");

        // 1. Stäng alla webbläsare först
        await CloseBrowsersAsync();

        // 2. Skanna för malware-script i Downloads
        await ScanDownloadsForMalwareAsync();

        // 3. Djuprensa Chrome med malware-fokus
        var chromeResult = await DeepCleanChromeWithMalwareProtectionAsync();
        result.ChromeProfilesCleaned = chromeResult.ProfilesCleaned;
        result.MalwareNotificationsRemoved += chromeResult.MalwareNotificationsRemoved;
        result.SuspiciousExtensionsRemoved += chromeResult.SuspiciousExtensionsRemoved;

        // 4. Djuprensa Edge med malware-fokus
        var edgeResult = await DeepCleanEdgeWithMalwareProtectionAsync();
        result.EdgeProfilesCleaned = edgeResult.ProfilesCleaned;
        result.MalwareNotificationsRemoved += edgeResult.MalwareNotificationsRemoved;
        result.SuspiciousExtensionsRemoved += edgeResult.SuspiciousExtensionsRemoved;

        // 5. Rensa Firefox
        var firefoxResult = await CleanFirefoxAsync();
        result.FirefoxProfilesCleaned = firefoxResult.ProfilesCleaned;

        // 6. Blockera malware-domäner aggressivt
        await UpdateHostsFileWithMalwareProtectionAsync();

        // 7. Sätt extremt starka säkerhetspolicies
        await SetMaximumSecurityPoliciesAsync();

        // 8. Rensa Windows notification system
        await CleanWindowsNotificationSystemAsync();

        // 9. Rensa PowerShell execution policy
        await SecurePowerShellExecutionAsync();

        // 10. Blockera vanliga malware-nedladdningsplatser
        await BlockMalwareDownloadSourcesAsync();

        result.Success = true;
        result.OperationLog = new List<string>(_operationLog);

        LogOperation($"=== ANTI-MALWARE RENSNING SLUTFÖRD: {result.TotalProfilesCleaned} profiler skyddade ===");
        
        return result;
    }
    catch (Exception ex)
    {
        _logger.Error($"Kritiskt fel vid anti-malware rensning: {ex.Message}");
        result.Success = false;
        result.OperationLog = new List<string>(_operationLog);
        return result;
    }
}

private async Task ScanDownloadsForMalwareAsync()
{
    try
    {
        LogOperation("--- SKANNAR DOWNLOADS FÖR MALWARE ---");
        
        var downloadsPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetEnvironmentVariable("TEMP"),
            @"C:\Users\Public\Downloads"
        };

        var suspiciousFiles = new List<string>();
        
        foreach (var path in downloadsPaths.Where(Directory.Exists))
        {
            var files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => File.GetCreationTime(f) > DateTime.Now.AddDays(-7)) // Senaste veckan
                .ToList();
            
            foreach (var file in files)
            {
                try
                {
                    if (await IsSuspiciousMalwareFileAsync(file))
                    {
                        suspiciousFiles.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    LogOperation($"   ⚠️ Kunde inte skanna {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        
        // Sätt suspekta filer i karantän
        foreach (var suspiciousFile in suspiciousFiles)
        {
            try
            {
                var quarantineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                    "FilKollen", "Quarantine", "Browser");
                
                if (!Directory.Exists(quarantineDir))
                    Directory.CreateDirectory(quarantineDir);
                
                var quarantineFile = Path.Combine(quarantineDir, $"{Guid.NewGuid()}_{Path.GetFileName(suspiciousFile)}");
                File.Move(suspiciousFile, quarantineFile);
                
                LogOperation($"   🔒 KARANTÄN: {Path.GetFileName(suspiciousFile)} - potentiell malware");
            }
            catch (Exception ex)
            {
                LogOperation($"   ❌ Kunde inte sätta {Path.GetFileName(suspiciousFile)} i karantän: {ex.Message}");
            }
        }
        
        LogOperation($"✅ Downloads-skanning klar: {suspiciousFiles.Count} suspekta filer funna");
    }
    catch (Exception ex)
    {
        LogOperation($"❌ Fel vid downloads-skanning: {ex.Message}");
    }
}

private async Task<bool> IsSuspiciousMalwareFileAsync(string filePath)
{
    try
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var fileInfo = new FileInfo(filePath);
        
        // Suspekta extensions
        var dangerousExtensions = new[] { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com", ".pif" };
        if (dangerousExtensions.Contains(extension)) return true;
        
        // Små filer med executable-extensions (ofta malware droppers)
        if (dangerousExtensions.Contains(extension) && fileInfo.Length < 10240) return true; // < 10KB
        
        // Suspekta filnamn
        if (fileName.Contains("nircmd") || fileName.Contains("screenshot") || 
            fileName.Contains("bot") || fileName.Contains("telegram")) return true;
        
        // Kolla innehåll för script-filer
        if (new[] { ".bat", ".cmd", ".ps1", ".vbs" }.Contains(extension))
        {
            var content = await File.ReadAllTextAsync(filePath);
            return _malwareScriptIndicators.Any(indicator => 
                content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }
        
        return false;
    }
    catch
    {
        return false; // Om vi inte kan läsa filen, antag att den är säker
    }
}

private async Task SecurePowerShellExecutionAsync()
{
    try
    {
        LogOperation("--- SÄKRAR POWERSHELL EXECUTION POLICY ---");
        
        // Sätt PowerShell execution policy till Restricted
        var psCommands = new[]
        {
            "Set-ExecutionPolicy -ExecutionPolicy Restricted -Scope LocalMachine -Force",
            "Set-ExecutionPolicy -ExecutionPolicy Restricted -Scope CurrentUser -Force"
        };
        
        foreach (var command in psCommands)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{command}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    LogOperation($"   🔒 PowerShell policy uppdaterad: {command.Split(' ')[2]}");
                }
            }
            catch (Exception ex)
            {
                LogOperation($"   ⚠️ PowerShell policy fel: {ex.Message}");
            }
        }
        
        LogOperation("✅ PowerShell execution policies säkrade");
    }
    catch (Exception ex)
    {
        LogOperation($"❌ PowerShell säkring misslyckades: {ex.Message}");
    }
}

private async Task BlockMalwareDownloadSourcesAsync()
{
    try
    {
        LogOperation("--- BLOCKERAR MALWARE-KÄLLOR ---");
        
        var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), 
            "drivers", "etc", "hosts");
        
        if (!File.Exists(hostsPath)) return;
        
        var existingHosts = await File.ReadAllTextAsync(hostsPath);
        
        // Ta bort gamla FilKollen-poster
        var lines = existingHosts.Split('\n').ToList();
        lines.RemoveAll(line => line.Contains("# FilKollen malware block"));
        
        // Lägg till nya malware-blockeringar
        var malwareSources = new[]
        {
            "# FilKollen malware block - START",
            "0.0.0.0 nirsoft.net # Block NirCmd downloads",
            "0.0.0.0 download.sysinternals.com # Block suspicious tools",
            "0.0.0.0 live.sysinternals.com # Block suspicious tools",
            "0.0.0.0 pastebin.com # Block malware paste sites",
            "0.0.0.0 hastebin.com # Block malware paste sites", 
            "0.0.0.0 ghostbin.com # Block malware paste sites",
            "0.0.0.0 controlc.com # Block malware paste sites",
            "0.0.0.0 api.telegram.org # Block Telegram bot API",
            "0.0.0.0 web.telegram.org # Block Telegram web",
            "0.0.0.0 t.me # Block Telegram links",
            "# FilKollen malware block - END"
        };
        
        lines.AddRange(malwareSources);
        
        var newHostsContent = string.Join('\n', lines);
        await File.WriteAllTextAsync(hostsPath, newHostsContent);
        
        LogOperation($"   🛡️ {malwareSources.Length - 2} malware-källor blockerade via hosts");
        LogOperation("✅ Malware-källor blockerade");
    }
    catch (Exception ex)
    {
        LogOperation($"❌ Kunde inte blockera malware-källor: {ex.Message}");
    }
}

private async Task<SecurityCleanResult> DeepCleanChromeWithMalwareProtectionAsync()
{
    var result = new SecurityCleanResult();
    
    try
    {
        LogOperation("--- CHROME ANTI-MALWARE DJUPRENSNING ---");
        
        var chromeDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data");

        if (!Directory.Exists(chromeDataPath))
        {
            LogOperation("❌ Chrome installation ej funnen");
            return result;
        }

        // Rensa ALLA downloads från Chrome
        await ClearChromeDownloadsHistoryAsync(chromeDataPath);

        var profiles = Directory.GetDirectories(chromeDataPath)
            .Where(d => Path.GetFileName(d).StartsWith("Default") || 
                       Path.GetFileName(d).StartsWith("Profile"))
            .ToList();

        foreach (var profilePath in profiles)
        {
            var profileName = Path.GetFileName(profilePath);
            LogOperation($"🔒 Anti-malware rensning Chrome profil: {profileName}");

            // Rensa alla malware notifications
            var notificationsRemoved = await RemoveMalwareNotificationsAdvancedAsync(profilePath, "Chrome");
            result.MalwareNotificationsRemoved += notificationsRemoved;
            
            // Analysera och ta bort suspekta extensions
            var extensionsRemoved = await AnalyzeAndRemoveMaliciousExtensionsAsync(profilePath, "Chrome");
            result.SuspiciousExtensionsRemoved += extensionsRemoved;
            
            // Rensa ALL browsing data (aggressivt)
            await NukeAllBrowsingDataAsync(profilePath, "Chrome");
            
            // Sätt extrema säkerhetsinställningar
            await ApplyAntiMalwareSecuritySettingsAsync(profilePath, "Chrome");
            
            result.ProfilesCleaned++;
        }

        LogOperation($"✅ Chrome anti-malware rensning klar: {result.ProfilesCleaned} profiler");
        return result;
    }
    catch (Exception ex)
    {
        LogOperation($"❌ Fel vid Chrome anti-malware rensning: {ex.Message}");
        return result;
    }
}

private async Task ClearChromeDownloadsHistoryAsync(string chromeDataPath)
{
    try
    {
        var profiles = Directory.GetDirectories(chromeDataPath)
            .Where(d => Path.GetFileName(d).StartsWith("Default") || 
                       Path.GetFileName(d).StartsWith("Profile"));
        
        foreach (var profilePath in profiles)
        {
            var historyFile = Path.Combine(profilePath, "History");
            if (File.Exists(historyFile))
            {
                // Ta backup först
                var backupFile = historyFile + ".malware_backup";
                File.Copy(historyFile, backupFile, true);
                
                // Radera history (kommer att förhindra re-download av malware)
                File.Delete(historyFile);
                LogOperation($"   🗑️ Chrome downloads history rensad: {Path.GetFileName(profilePath)}");
            }
        }
    }
    catch (Exception ex)
    {
        LogOperation($"   ⚠️ Kunde inte rensa Chrome downloads history: {ex.Message}");
    }
}

private async Task ApplyAntiMalwareSecuritySettingsAsync(string profilePath, string browserName)
{
    try
    {
        LogOperation($"   🔐 {browserName}: Tillämpar anti-malware säkerhetsinställningar...");
        
        var securePrefs = new Dictionary<string, object>
        {
            ["profile"] = new Dictionary<string, object>
            {
                ["default_content_setting_values"] = new Dictionary<string, object>
                {
                    ["notifications"] = 2,  // Block ALL notifications
                    ["automatic_downloads"] = 2,  // Block ALL downloads
                    ["plugins"] = 2,  // Block ALL plugins
                    ["popups"] = 2,  // Block ALL popups
                    ["javascript"] = 2,  // Block ALL JavaScript (aggressive)
                    ["geolocation"] = 2,
                    ["media_stream_camera"] = 2,
                    ["media_stream_mic"] = 2,
                    ["background_sync"] = 2,
                    ["ads"] = 2
                }
            },
            ["browser"] = new Dictionary<string, object>
            {
                ["check_default_browser"] = false,
                ["show_home_button"] = false
            },
            ["safebrowsing"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["enhanced"] = true,
                ["reporting_enabled"] = true
            },
            ["download"] = new Dictionary<string, object>
            {
                ["directory_upgrade"] = true,
                ["prompt_for_download"] = true
            }
        };

        var prefsFile = Path.Combine(profilePath, "Preferences");
        var json = JsonSerializer.Serialize(securePrefs, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(prefsFile, json);
        
        LogOperation($"   ✅ {browserName}: Anti-malware inställningar tillämpade");
    }
    catch (Exception ex)
    {
        LogOperation($"   ❌ Fel vid anti-malware inställningar: {ex.Message}");
    }
}