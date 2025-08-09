// Services/ThemeService.cs
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
            // Kontrollera om användaren har ställt in att följa systemets tema
            try
            {
                // Läs Windows Registry för tema-inställning
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
                
                if (appsUseLightTheme is int lightTheme)
                {
                    // Om systemet använder mörkt tema, returnera true
                    return lightTheme == 0;
                }
            }
            catch
            {
                // Om vi inte kan läsa registry, använd sparad inställning
            }

            // Fallback till sparad inställning
            return IsDarkTheme;
        }

        private void ApplyTheme()
        {
            try
            {
                // Använd Dispatcher för UI-thread säkerhet
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var paletteHelper = new PaletteHelper();
                    var theme = paletteHelper.GetTheme();

                    // Sätt bas-tema
                    theme.SetBaseTheme(IsDarkTheme ? BaseTheme.Dark : BaseTheme.Light);

                    // Anpassa färger för FilKollen
                    if (IsDarkTheme)
                    {
                        // Mörka tema-färger
                        theme.SetPrimaryColor(System.Windows.Media.Color.FromRgb(102, 126, 234)); // #667eea
                        theme.SetSecondaryColor(System.Windows.Media.Color.FromRgb(255, 152, 0)); // #FF9800
                        
                        // Anpassa bakgrunder för glassmorphism
                        Application.Current.Resources["ModernWindowBackground"] = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(242, 30, 30, 30)); // #F21E1E1E - 95% opacity
                        Application.Current.Resources["ModernCardBackground"] = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(230, 45, 45, 48)); // #E62D2D30 - 90% opacity
                        Application.Current.Resources["ModernSidebarBackground"] = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(204, 37, 37, 38)); // #CC252526 - 80% opacity
                    }
                    else
                    {
                        // Ljusa tema-färger
                        theme.SetPrimaryColor(System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3
                        theme.SetSecondaryColor(System.Windows.Media.Color.FromRgb(255, 152, 0)); // #FF9800
                        
                        // Anpassa bakgrunder för glassmorphism
                        Application.Current.Resources["ModernWindowBackground"] = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(242, 248, 249, 250)); // #F2F8F9FA - 95% opacity
                        Application.Current.Resources["ModernCardBackground"] = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(230, 255, 255, 255)); // #E6FFFFFF - 90% opacity
                        Application.Current.Resources["ModernSidebarBackground"] = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(204, 248, 249, 250)); // #CCF8F9FA - 80% opacity
                    }

                    // Tillämpa tema
                    paletteHelper.SetTheme(theme);
                });
            }
            catch (Exception ex)
            {
                // Log error men fortsätt ändå
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
                        // Om config är null, använd system-tema
                        _isDarkTheme = ShouldUseDarkTheme();
                    }
                }
                else
                {
                    // Om ingen config-fil finns, använd system-tema
                    _isDarkTheme = ShouldUseDarkTheme();
                }
            }
            catch
            {
                // Vid fel, använd ljust tema som standard
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

    // Configuration model för tema-inställningar
    public class ThemeConfig
    {
        public bool IsDarkTheme { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}