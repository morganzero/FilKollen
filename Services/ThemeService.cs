using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using System.Linq;

namespace FilKollen.Services
{
    public enum ThemeMode
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    public class ThemeService : INotifyPropertyChanged
    {
        private const string ThemeConfigFile = "theme.json";
        private ThemeMode _mode = ThemeMode.System;
        private bool _isDarkTheme = false;
        private readonly object _lockObject = new object();

        public ThemeMode Mode
        {
            get => _mode;
            private set
            {
                if (_mode != value)
                {
                    _mode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ModeDisplayName));
                }
            }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            private set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThemeDisplayName));
                }
            }
        }

        public string ModeDisplayName => Mode switch
        {
            ThemeMode.System => "System",
            ThemeMode.Light => "Ljust",
            ThemeMode.Dark => "Mörkt",
            _ => "System"
        };

        public string ThemeDisplayName => IsDarkTheme ? "Mörkt tema" : "Ljust tema";

        public event EventHandler? ThemeChanged;

        public ThemeService()
        {
            LoadThemeConfiguration();
            ApplyTheme(Mode);
        }

        public void ApplyTheme(ThemeMode mode)
        {
            lock (_lockObject)
            {
                Mode = mode;

                IsDarkTheme = mode switch
                {
                    ThemeMode.Light => false,
                    ThemeMode.Dark => true,
                    ThemeMode.System => DetectSystemDarkMode(),
                    _ => false
                };

                ApplyThemeToApplication();
                SaveThemeConfiguration();
                ThemeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool DetectSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
                return appsUseLightTheme is int lightTheme && lightTheme == 0;
            }
            catch
            {
                return false; // Default to light if can't detect
            }
        }

        private void ApplyThemeToApplication()
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var resources = Application.Current?.Resources;
                    if (resources?.MergedDictionaries == null) return;

                    // Bestäm vilken temafil som ska laddas
                    var themeFile = IsDarkTheme ? "Themes/Theme.Dark.xaml" : "Themes/Theme.Light.xaml";

                    try
                    {
                        var newTheme = new ResourceDictionary
                        {
                            Source = new Uri(themeFile, UriKind.Relative)
                        };

                        // Hitta och ersätt befintliga temafiler
                        var existingTheme = resources.MergedDictionaries
                            .FirstOrDefault(d => d.Source != null &&
                                          (d.Source.OriginalString.Contains("Theme.Light.xaml") ||
                                           d.Source.OriginalString.Contains("Theme.Dark.xaml")));

                        if (existingTheme != null)
                        {
                            var index = resources.MergedDictionaries.IndexOf(existingTheme);
                            resources.MergedDictionaries[index] = newTheme;
                        }
                        else
                        {
                            // Lägg till som första så att färgerna definieras före styles
                            resources.MergedDictionaries.Insert(0, newTheme);
                        }

                        System.Diagnostics.Debug.WriteLine($"Theme applied: {themeFile}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load theme file {themeFile}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply theme: {ex.Message}");
            }
        }

        private void LoadThemeConfiguration()
        {
            try
            {
                if (File.Exists(ThemeConfigFile))
                {
                    var json = File.ReadAllText(ThemeConfigFile);
                    var config = JsonSerializer.Deserialize<ThemeConfig>(json);

                    if (config != null && Enum.IsDefined(typeof(ThemeMode), config.Mode))
                    {
                        _mode = config.Mode;
                    }
                }
            }
            catch
            {
                _mode = ThemeMode.System; // Fallback to system
            }
        }

        private void SaveThemeConfiguration()
        {
            try
            {
                var config = new ThemeConfig
                {
                    Mode = Mode,
                    LastUpdated = DateTime.UtcNow
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(ThemeConfigFile, json);
            }
            catch
            {
                // Ignore save errors - not critical
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ThemeConfig
    {
        public ThemeMode Mode { get; set; } = ThemeMode.System;
        public DateTime LastUpdated { get; set; }
    }
}