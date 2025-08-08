using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using MaterialDesignThemes.Wpf;

namespace FilKollen.Services
{
    public class ThemeService : INotifyPropertyChanged
    {
        private bool _isDarkTheme;
        private readonly string _settingsFile;

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;
                    ApplyTheme();
                    SaveThemeSettings();
                    OnPropertyChanged();
                }
            }
        }

        public string ThemeDisplayName => IsDarkTheme ? "Mörkt läge" : "Ljust läge";
        public string ThemeIcon => IsDarkTheme ? "WeatherNight" : "WeatherSunny";

        public ThemeService()
        {
            _settingsFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FilKollen", "theme-settings.json");
                
            LoadThemeSettings();
            ApplyTheme();
        }

        public void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
        }

        private void ApplyTheme()
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            // Sätt bas-tema
            theme.SetBaseTheme(IsDarkTheme ? MaterialDesignThemes.Wpf.Theme.Dark : MaterialDesignThemes.Wpf.Theme.Light);

            // Modern färgpalett
            if (IsDarkTheme)
            {
                // Modern mörk tema - inspirerat av bilden
                theme.SetPrimaryColor(Color.FromRgb(99, 102, 241));    // Modern purple-blue
                theme.SetSecondaryColor(Color.FromRgb(236, 72, 153));  // Modern pink accent
                
                // Custom background colors för cards
                Application.Current.Resources["CustomCardBackground"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 41, 59));   // Dark slate
                Application.Current.Resources["CustomWindowBackground"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(15, 23, 42));   // Very dark slate
                Application.Current.Resources["CustomSidebarBackground"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(51, 65, 85));   // Medium slate
            }
            else
            {
                // Modern ljus tema
                theme.SetPrimaryColor(Color.FromRgb(59, 130, 246));    // Modern blue
                theme.SetSecondaryColor(Color.FromRgb(168, 85, 247));  // Modern purple accent
                
                // Custom background colors för cards
                Application.Current.Resources["CustomCardBackground"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 255, 255)); // Pure white
                Application.Current.Resources["CustomWindowBackground"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(248, 250, 252)); // Very light gray
                Application.Current.Resources["CustomSidebarBackground"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(241, 245, 249)); // Light gray
            }

            paletteHelper.SetTheme(theme);
        }

        private void LoadThemeSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
                    _isDarkTheme = settings?.IsDarkTheme ?? false;
                }
            }
            catch
            {
                _isDarkTheme = false; // Default till ljust tema
            }
        }

        private void SaveThemeSettings()
        {
            try
            {
                var settingsDir = Path.GetDirectoryName(_settingsFile);
                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                var settings = new ThemeSettings { IsDarkTheme = _isDarkTheme };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json);
            }
            catch
            {
                // Ignorera fel vid sparande
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ThemeSettings
    {
        public bool IsDarkTheme { get; set; }
    }
}