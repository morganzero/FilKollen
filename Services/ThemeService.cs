using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace FilKollen.Services
{
    public class ThemeService : INotifyPropertyChanged
    {
        private const string ThemeConfigFile = "theme.json";
        private bool _isDarkTheme;
        private readonly object _lockObject = new object();

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

        public string ThemeDisplayName => IsDarkTheme ? "Mörkt tema" : "Ljust tema";

        public ThemeService()
        {
            LoadThemeConfiguration();
            ApplyTheme();
        }

        public void ToggleTheme()
        {
            lock (_lockObject)
            {
                IsDarkTheme = !IsDarkTheme;
                ApplyTheme();
                SaveThemeConfiguration();
            }
        }

        public void SetTheme(bool isDarkTheme)
        {
            lock (_lockObject)
            {
                if (IsDarkTheme != isDarkTheme)
                {
                    IsDarkTheme = isDarkTheme;
                    ApplyTheme();
                    SaveThemeConfiguration();
                }
            }
        }

        public bool ShouldUseDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
                
                if (appsUseLightTheme is int lightTheme)
                {
                    return lightTheme == 0;
                }
            }
            catch
            {
                // Om vi inte kan läsa registry, använd sparad inställning
            }

            return IsDarkTheme;
        }

        private void ApplyTheme()
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
var paletteHelper = new MaterialDesignThemes.Wpf.PaletteHelper();
var theme = paletteHelper.GetTheme();

MaterialDesignThemes.Wpf.IBaseTheme baseTheme =
    IsDarkTheme
        ? new MaterialDesignThemes.Wpf.MaterialDesignDarkTheme()
        : new MaterialDesignThemes.Wpf.MaterialDesignLightTheme();

theme.SetBaseTheme(baseTheme);
paletteHelper.SetTheme(theme);

                    // Anpassa färger för FilKollen
                    if (IsDarkTheme)
                    {
                        theme.SetPrimaryColor(System.Windows.Media.Color.FromRgb(102, 126, 234));
                        theme.SetSecondaryColor(System.Windows.Media.Color.FromRgb(255, 152, 0));
                    }
                    else
                    {
                        theme.SetPrimaryColor(System.Windows.Media.Color.FromRgb(33, 150, 243));
                        theme.SetSecondaryColor(System.Windows.Media.Color.FromRgb(255, 152, 0));
                    }

                    paletteHelper.SetTheme(theme);
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
                        _isDarkTheme = config.IsDarkTheme;
                    }
                    else
                    {
                        _isDarkTheme = ShouldUseDarkTheme();
                    }
                }
                else
                {
                    _isDarkTheme = ShouldUseDarkTheme();
                }
            }
            catch
            {
                _isDarkTheme = false;
            }
        }

        private void SaveThemeConfiguration()
        {
            try
            {
                var config = new ThemeConfig
                {
                    IsDarkTheme = IsDarkTheme,
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
                // Ignorera fel vid sparning - inte kritiskt
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
        public bool IsDarkTheme { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}