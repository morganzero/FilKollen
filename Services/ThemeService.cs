using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace FilKollen.Services
{
    public enum ThemeMode
    {
        System,
        Light,
        Dark
    }

    public class ThemeService : INotifyPropertyChanged
    {
        private const string ThemeConfigFile = "theme.json";
        private ThemeMode _mode = ThemeMode.System;
        private bool _isDarkTheme = true;
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
                    ThemeMode.System => DetectSystemDark(),
                    _ => true
                };

                ApplyThemeToApplication();
                SaveThemeConfiguration();
                ThemeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool DetectSystemDark()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
                return appsUseLightTheme is int lightTheme && lightTheme == 0;
            }
            catch
            {
                return true; // Default to dark
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

                    // Replace the first dictionary (colors) while keeping Glass.xaml
                    var colorDictionary = new ResourceDictionary
                    {
                        Source = new Uri(IsDarkTheme
                            ? "Themes/Colors.Dark.xaml"
                            : "Themes/Colors.Light.xaml",
                            UriKind.Relative)
                    };

                    // Replace first dictionary (colors)
                    if (resources.MergedDictionaries.Count > 0)
                    {
                        resources.MergedDictionaries[0] = colorDictionary;
                    }
                    else
                    {
                        resources.MergedDictionaries.Add(colorDictionary);
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

                    if (config != null)
                    {
                        _mode = config.Mode;
                        // Don't set _isDarkTheme here, let ApplyTheme handle it
                    }
                }
            }
            catch
            {
                _mode = ThemeMode.System;
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

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(ThemeConfigFile, json);
            }
            catch
            {
                // Ignore saving errors - not critical
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