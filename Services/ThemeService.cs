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
                    var resources = Application.Current?.Resources;
                    if (resources == null) return;

                    // Uppdatera MaterialDesign tema
                    var paletteHelper = new PaletteHelper();
                    var theme = paletteHelper.GetTheme();

                    IBaseTheme baseTheme = IsDarkTheme
                        ? new MaterialDesignDarkTheme()
                        : new MaterialDesignLightTheme();

                    theme.SetBaseTheme(baseTheme);

                    // FÖRBÄTTRADE TRANSPARENTA FÄRGER
                    if (IsDarkTheme)
                    {
                        // Mörka tema-färger med transparency
                        theme.SetPrimaryColor(System.Windows.Media.Color.FromRgb(102, 126, 234));   // #667eea
                        theme.SetSecondaryColor(System.Windows.Media.Color.FromRgb(0, 212, 170));   // #00d4aa

                        // Dynamiska färger för mörkt tema
                        resources["AppBackgroundColor"] = System.Windows.Media.Color.FromRgb(10, 10, 10);      // #0a0a0a
                        resources["AppSurfaceColor"] = System.Windows.Media.Color.FromArgb(64, 22, 22, 41);    // #40161629
                        resources["AppCardColor"] = System.Windows.Media.Color.FromArgb(96, 26, 26, 53);       // #601a1a35
                        resources["AppTextColor"] = System.Windows.Media.Color.FromRgb(255, 255, 255);         // #ffffff
                        resources["AppTextSecondaryColor"] = System.Windows.Media.Color.FromRgb(184, 198, 219); // #b8c6db
                    }
                    else
                    {
                        // Ljusa tema-färger med transparency
                        theme.SetPrimaryColor(System.Windows.Media.Color.FromRgb(59, 130, 246));    // #3b82f6
                        theme.SetSecondaryColor(System.Windows.Media.Color.FromRgb(6, 182, 212));   // #06b6d4

                        // Dynamiska färger för ljust tema
                        resources["AppBackgroundColor"] = System.Windows.Media.Color.FromRgb(248, 250, 252);   // #f8fafc
                        resources["AppSurfaceColor"] = System.Windows.Media.Color.FromArgb(96, 255, 255, 255); // #60ffffff
                        resources["AppCardColor"] = System.Windows.Media.Color.FromArgb(128, 255, 255, 255);   // #80ffffff
                        resources["AppTextColor"] = System.Windows.Media.Color.FromRgb(30, 41, 59);            // #1e293b
                        resources["AppTextSecondaryColor"] = System.Windows.Media.Color.FromRgb(71, 85, 105);  // #475569
                    }

                    paletteHelper.SetTheme(theme);

                    // Uppdatera glassmorfism-effekter
                    UpdateGlassmorphismEffects();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Misslyckades att tillämpa tema: {ex.Message}");
            }
        }

        private void UpdateGlassmorphismEffects()
        {
            try
            {
                var resources = Application.Current?.Resources;
                if (resources == null) return;

                if (IsDarkTheme)
                {
                    // Mörka glassmorfism-effekter
                    var darkGlassGradient = new System.Windows.Media.LinearGradientBrush();
                    darkGlassGradient.StartPoint = new System.Windows.Point(0, 0);
                    darkGlassGradient.EndPoint = new System.Windows.Point(1, 1);
                    darkGlassGradient.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(32, 255, 255, 255), 0));
                    darkGlassGradient.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(16, 255, 255, 255), 0.5));
                    darkGlassGradient.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(8, 255, 255, 255), 1));

                    resources["GlassMorphGradient"] = darkGlassGradient;

                    var darkGlassBorder = new System.Windows.Media.LinearGradientBrush();
                    darkGlassBorder.StartPoint = new System.Windows.Point(0, 0);
                    darkGlassBorder.EndPoint = new System.Windows.Point(1, 1);
                    darkGlassBorder.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(64, 0, 212, 170), 0));  // Cyan
                    darkGlassBorder.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(32, 102, 126, 234), 1)); // Purple

                    resources["GlassMorphBorder"] = darkGlassBorder;
                }
                else
                {
                    // Ljusa glassmorfism-effekter
                    var lightGlassGradient = new System.Windows.Media.LinearGradientBrush();
                    lightGlassGradient.StartPoint = new System.Windows.Point(0, 0);
                    lightGlassGradient.EndPoint = new System.Windows.Point(1, 1);
                    lightGlassGradient.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(48, 255, 255, 255), 0));
                    lightGlassGradient.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(24, 255, 255, 255), 0.5));
                    lightGlassGradient.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(12, 255, 255, 255), 1));

                    resources["GlassMorphGradient"] = lightGlassGradient;

                    var lightGlassBorder = new System.Windows.Media.LinearGradientBrush();
                    lightGlassBorder.StartPoint = new System.Windows.Point(0, 0);
                    lightGlassBorder.EndPoint = new System.Windows.Point(1, 1);
                    lightGlassBorder.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(80, 59, 130, 246), 0));   // Blue
                    lightGlassBorder.GradientStops.Add(new System.Windows.Media.GradientStop(
                        System.Windows.Media.Color.FromArgb(40, 6, 182, 212), 1));    // Cyan

                    resources["GlassMorphBorder"] = lightGlassBorder;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Misslyckades att uppdatera glassmorfism: {ex.Message}");
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
                _isDarkTheme = true; // Default till mörkt tema
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