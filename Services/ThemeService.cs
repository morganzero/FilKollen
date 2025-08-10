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
                    var paletteHelper = new PaletteHelper();
                    var theme = paletteHelper.GetTheme();

                    // KORRIGERAT: Använd rätt variabelnamn
                    IBaseTheme baseTheme = IsDarkTheme
                        ? new MaterialDesignDarkTheme()
                        : new MaterialDesignLightTheme();

                    theme.SetBaseTheme(baseTheme);

                    // FÖRBÄTTRADE färger för FilKollen
                    if (IsDarkTheme)
                    {
                        // Mörka tema-färger - moderna blå/turkos nyanser
                        theme.SetPrimaryColor(System.Windows.Media.Color.FromRgb(59, 130, 246));   // Blue-500
                        theme.SetSecondaryColor(System.Windows.Media.Color.FromRgb(6, 182, 212));  // Cyan-500
                        
                        // Anpassa Paper och Card bakgrunder för mörkt tema
                        theme.Paper = System.Windows.Media.Color.FromRgb(15, 23, 42);  // Slate-900
                        theme.CardBackground = System.Windows.Media.Color.FromRgb(30, 41, 59);    // Slate-800
                    }
                    else
                    {
                        // Ljusa tema-färger - livfulla men professionella
                        theme.SetPrimaryColor(System.Windows.Media.Color.FromRgb(37, 99, 235));    // Blue-600
                        theme.SetSecondaryColor(System.Windows.Media.Color.FromRgb(8, 145, 178));  // Cyan-600
                        
                        // Ljusa bakgrunder
                        theme.Paper = System.Windows.Media.Color.FromRgb(248, 250, 252);  // Slate-50
                        theme.CardBackground = System.Windows.Media.Color.FromRgb(255, 255, 255); // White
                    }

                    paletteHelper.SetTheme(theme);

                    // Uppdatera även custom resurser
                    UpdateCustomResources();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply theme: {ex.Message}");
            }
        }

        private void UpdateCustomResources()
        {
            try
            {
                var resources = Application.Current?.Resources;
                if (resources == null) return;

                // Uppdatera custom tema-resurser baserat på aktuellt tema
                if (IsDarkTheme)
                {
                    // Mörka tema-resurser
                    resources["ModernWindowBackground"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(15, 23, 42));   // Slate-900
                    resources["ModernCardBackground"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(30, 41, 59));   // Slate-800
                    resources["ModernTextPrimary"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(248, 250, 252)); // Slate-50
                    resources["ModernTextSecondary"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(203, 213, 225)); // Slate-300
                }
                else
                {
                    // Ljusa tema-resurser
                    resources["ModernWindowBackground"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(248, 249, 250)); // Gray-50
                    resources["ModernCardBackground"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 255, 255)); // White
                    resources["ModernTextPrimary"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(33, 37, 41));    // Gray-900
                    resources["ModernTextSecondary"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(108, 117, 125)); // Gray-600
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update custom resources: {ex.Message}");
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