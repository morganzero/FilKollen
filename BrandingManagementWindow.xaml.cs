// Windows/BrandingManagementWindow.xaml.cs
using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FilKollen.Models;
using FilKollen.Services;
using Microsoft.Win32;
using Serilog;

namespace FilKollen.Windows
{
    public partial class BrandingManagementWindow : Window, INotifyPropertyChanged
    {
        private readonly BrandingService _brandingService;
        private readonly ILogger _logger;
        private string? _selectedLogoPath;
        
        public BrandingConfig CurrentBranding { get; set; }

        public BrandingManagementWindow(BrandingService brandingService, ILogger logger)
        {
            InitializeComponent();
            
            _brandingService = brandingService;
            _logger = logger;
            CurrentBranding = _brandingService.GetCurrentBranding();
            
            DataContext = this;
            LoadCurrentBranding();
            
            _logger.Information("Branding management window opened");
        }

        private void LoadCurrentBranding()
        {
            try
            {
                // Ladda aktuell branding-information
                CompanyNameTextBox.Text = CurrentBranding.CompanyName;
                ProductNameTextBox.Text = CurrentBranding.ProductName;
                ContactEmailTextBox.Text = CurrentBranding.ContactEmail;
                WebsiteTextBox.Text = CurrentBranding.Website;
                PrimaryColorTextBox.Text = CurrentBranding.PrimaryColor;
                SecondaryColorTextBox.Text = CurrentBranding.SecondaryColor;
                
                // Uppdatera preview
                UpdateCurrentBrandingDisplay();
                
                _logger.Information("Current branding loaded into form");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load current branding: {ex.Message}");
                MessageBox.Show($"Fel vid laddning av aktuell branding: {ex.Message}",
                    "Laddningsfel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateCurrentBrandingDisplay()
        {
            CurrentCompanyNameText.Text = CurrentBranding.CompanyName;
            CurrentProductNameText.Text = CurrentBranding.ProductName;
            CurrentContactEmailText.Text = CurrentBranding.ContactEmail;
            CurrentWebsiteText.Text = CurrentBranding.Website;
            LastUpdatedText.Text = CurrentBranding.LastUpdated.ToString("yyyy-MM-dd HH:mm");
            
            // Ladda och visa aktuell logo
            try
            {
                if (File.Exists(CurrentBranding.LogoPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(Path.GetFullPath(CurrentBranding.LogoPath));
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    CurrentLogoPreview.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to load current logo: {ex.Message}");
            }
        }

        private void BrowseLogoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Välj Logo-fil",
                    Filter = "PNG-filer (*.png)|*.png",
                    FilterIndex = 1,
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    _selectedLogoPath = openFileDialog.FileName;
                    LogoPathTextBox.Text = _selectedLogoPath;
                    
                    // Validera och visa preview
                    ValidateAndPreviewLogo(_selectedLogoPath);
                    
                    _logger.Information($"Logo file selected: {_selectedLogoPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error selecting logo file: {ex.Message}");
                MessageBox.Show($"Fel vid val av logo-fil: {ex.Message}",
                    "Filfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ValidateAndPreviewLogo(string logoPath)
        {
            try
            {
                var validation = _brandingService.ValidateCustomLogo(logoPath);
                
                LogoValidationText.Text = validation.ErrorMessage;
                LogoValidationText.Foreground = validation.IsValid ? Brushes.Green : Brushes.Red;
                
                if (validation.IsValid)
                {
                    // Visa preview av logon
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(logoPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    
                    LogoPreviewImage.Source = bitmap;
                    LogoPreviewPanel.Visibility = Visibility.Visible;
                    
                    _logger.Information("Logo validation successful - preview displayed");
                }
                else
                {
                    LogoPreviewPanel.Visibility = Visibility.Collapsed;
                    _logger.Warning($"Logo validation failed: {validation.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error validating logo: {ex.Message}");
                LogoValidationText.Text = $"Fel vid validering: {ex.Message}";
                LogoValidationText.Foreground = Brushes.Red;
                LogoPreviewPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void PreviewBrandingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var previewConfig = CreateBrandingConfigFromForm();
                
                var previewMessage = 
                    $"🎨 BRANDING FÖRHANDSVISNING\n\n" +
                    $"Företag: {previewConfig.CompanyName}\n" +
                    $"Produkt: {previewConfig.ProductName}\n" +
                    $"Kontakt: {previewConfig.ContactEmail}\n" +
                    $"Webbsida: {previewConfig.Website}\n" +
                    $"Primärfärg: {previewConfig.PrimaryColor}\n" +
                    $"Sekundärfärg: {previewConfig.SecondaryColor}\n" +
                    $"Logo: {(_selectedLogoPath != null ? "Anpassad logo vald" : "Standard logo")}\n\n" +
                    $"Klicka 'Tillämpa Branding' för att aktivera denna konfiguration.";

                MessageBox.Show(previewMessage, "Branding Förhandsvisning", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                _logger.Information("Branding preview displayed");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error creating branding preview: {ex.Message}");
                MessageBox.Show($"Fel vid förhandsvisning: {ex.Message}",
                    "Förhandsvisningsfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ApplyBrandingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateForm())
                {
                    MessageBox.Show("Kontrollera att alla obligatoriska fält är korrekt ifyllda.",
                        "Ofullständig information", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ApplyBrandingButton.IsEnabled = false;
                ApplyBrandingButton.Content = "🔄 Tillämpar...";

                var newBrandingConfig = CreateBrandingConfigFromForm();
                
                var success = await _brandingService.ApplyCustomBrandingAsync(newBrandingConfig, _selectedLogoPath);
                
                if (success)
                {
                    CurrentBranding = _brandingService.GetCurrentBranding();
                    UpdateCurrentBrandingDisplay();
                    
                    _logger.Information($"Branding successfully applied for: {newBrandingConfig.CompanyName}");
                    
                    var result = MessageBox.Show(
                        $"🎉 BRANDING TILLÄMPAT!\n\n" +
                        $"✅ Företag: {newBrandingConfig.CompanyName}\n" +
                        $"✅ Produkt: {newBrandingConfig.ProductName}\n" +
                        $"✅ Logo: {(_selectedLogoPath != null ? "Anpassad" : "Standard")}\n\n" +
                        $"Branding-ändringarna kräver omstart av applikationen.\n\n" +
                        $"Vill du starta om FilKollen nu?",
                        "Branding Tillämpat",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                }
                else
                {
                    MessageBox.Show(
                        "❌ Kunde inte tillämpa branding-ändringarna.\n\n" +
                        "Kontrollera att alla filer är giltiga och försök igen.",
                        "Branding-fel",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error applying branding: {ex.Message}");
                MessageBox.Show($"Fel vid tillämpning av branding: {ex.Message}",
                    "Tillämpningsfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ApplyBrandingButton.IsEnabled = true;
                ApplyBrandingButton.Content = "✅ Tillämpa Branding";
            }
        }

        private async void ResetBrandingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "🔄 ÅTERSTÄLL TILL STANDARD BRANDING?\n\n" +
                    "Detta kommer att:\n" +
                    "• Återställa till FilKollen standard-logo\n" +
                    "• Återställa företagsinformation till standard\n" +
                    "• Ta bort alla anpassade färger\n" +
                    "• Radera anpassade logo-filer\n\n" +
                    "Denna åtgärd kan inte ångras. Fortsätt?",
                    "Bekräfta Återställning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ResetBrandingButton.IsEnabled = false;
                    ResetBrandingButton.Content = "🔄 Återställer...";

                    await _brandingService.ResetToDefaultBrandingAsync();
                    
                    CurrentBranding = _brandingService.GetCurrentBranding();
                    LoadCurrentBranding();
                    
                    // Rensa formulär
                    _selectedLogoPath = null;
                    LogoPathTextBox.Text = string.Empty;
                    LogoPreviewPanel.Visibility = Visibility.Collapsed;
                    
                    _logger.Information("Branding reset to default");
                    
                    MessageBox.Show(
                        "✅ Branding har återställts till standard!\n\n" +
                        "Starta om applikationen för att se ändringarna.",
                        "Återställning Slutförd",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error resetting branding: {ex.Message}");
                MessageBox.Show($"Fel vid återställning: {ex.Message}",
                    "Återställningsfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ResetBrandingButton.IsEnabled = true;
                ResetBrandingButton.Content = "🔄 Återställ Standard";
            }
        }

        private async void ExportConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Exportera Branding Konfiguration",
                    Filter = "JSON-filer (*.json)|*.json",
                    FileName = $"FilKollen_Branding_{DateTime.Now:yyyyMMdd}.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var configJson = await _brandingService.ExportBrandingConfigAsync();
                    await File.WriteAllTextAsync(saveFileDialog.FileName, configJson);
                    
                    _logger.Information($"Branding config exported to: {saveFileDialog.FileName}");
                    
                    MessageBox.Show(
                        $"📄 Branding-konfiguration exporterad!\n\n" +
                        $"Fil: {Path.GetFileName(saveFileDialog.FileName)}\n" +
                        $"Sökväg: {saveFileDialog.FileName}\n\n" +
                        $"Denna fil kan användas för att importera samma branding till andra installationer.",
                        "Export Slutförd",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error exporting branding config: {ex.Message}");
                MessageBox.Show($"Fel vid export: {ex.Message}",
                    "Exportfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Importera Branding Konfiguration",
                    Filter = "JSON-filer (*.json)|*.json",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var configJson = await File.ReadAllTextAsync(openFileDialog.FileName);
                    var success = await _brandingService.ImportBrandingConfigAsync(configJson);
                    
                    if (success)
                    {
                        CurrentBranding = _brandingService.GetCurrentBranding();
                        LoadCurrentBranding();
                        
                        _logger.Information($"Branding config imported from: {openFileDialog.FileName}");
                        
                        MessageBox.Show(
                            $"📥 Branding-konfiguration importerad!\n\n" +
                            $"Företag: {CurrentBranding.CompanyName}\n" +
                            $"Produkt: {CurrentBranding.ProductName}\n\n" +
                            $"Starta om applikationen för att se ändringarna.",
                            "Import Slutförd",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(
                            "❌ Kunde inte importera branding-konfigurationen.\n\n" +
                            "Kontrollera att filen är giltig och försök igen.",
                            "Importfel",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error importing branding config: {ex.Message}");
                MessageBox.Show($"Fel vid import: {ex.Message}",
                    "Importfel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateForm()
        {
            // Kontrollera obligatoriska fält
            if (string.IsNullOrWhiteSpace(CompanyNameTextBox.Text))
            {
                CompanyNameTextBox.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(ProductNameTextBox.Text))
            {
                ProductNameTextBox.Focus();
                return false;
            }

            // Validera e-postadress om angiven
            if (!string.IsNullOrWhiteSpace(ContactEmailTextBox.Text))
            {
                try
                {
                    var addr = new System.Net.Mail.MailAddress(ContactEmailTextBox.Text);
                    if (addr.Address != ContactEmailTextBox.Text)
                        return false;
                }
                catch
                {
                    ContactEmailTextBox.Focus();
                    return false;
                }
            }

            // Validera färgkoder om angivna
            if (!string.IsNullOrWhiteSpace(PrimaryColorTextBox.Text))
            {
                if (!IsValidHexColor(PrimaryColorTextBox.Text))
                {
                    PrimaryColorTextBox.Focus();
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(SecondaryColorTextBox.Text))
            {
                if (!IsValidHexColor(SecondaryColorTextBox.Text))
                {
                    SecondaryColorTextBox.Focus();
                    return false;
                }
            }

            // Validera logo om vald
            if (!string.IsNullOrEmpty(_selectedLogoPath))
            {
                var logoValidation = _brandingService.ValidateCustomLogo(_selectedLogoPath);
                if (!logoValidation.IsValid)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsValidHexColor(string colorCode)
        {
            if (string.IsNullOrWhiteSpace(colorCode))
                return false;

            if (!colorCode.StartsWith("#"))
                return false;

            if (colorCode.Length != 7)
                return false;

            try
            {
                Convert.ToInt32(colorCode.Substring(1), 16);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private BrandingConfig CreateBrandingConfigFromForm()
        {
            return new BrandingConfig
            {
                CompanyName = CompanyNameTextBox.Text?.Trim() ?? string.Empty,
                ProductName = ProductNameTextBox.Text?.Trim() ?? string.Empty,
                ContactEmail = ContactEmailTextBox.Text?.Trim() ?? string.Empty,
                Website = WebsiteTextBox.Text?.Trim() ?? string.Empty,
                PrimaryColor = !string.IsNullOrWhiteSpace(PrimaryColorTextBox.Text) ? 
                    PrimaryColorTextBox.Text.Trim() : "#2196F3",
                SecondaryColor = !string.IsNullOrWhiteSpace(SecondaryColorTextBox.Text) ? 
                    SecondaryColorTextBox.Text.Trim() : "#FF9800",
                LogoPath = _selectedLogoPath ?? CurrentBranding.LogoPath
            };
        }

private void RestartApplication()
        {
            try
            {
                // KORRIGERAT: Använd AppContext.BaseDirectory istället för Assembly.Location
                var currentExecutable = System.IO.Path.Combine(System.AppContext.BaseDirectory, "FilKollen.exe");
                System.Diagnostics.Process.Start(currentExecutable);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to restart application: {ex.Message}");
                MessageBox.Show(
                    "Kunde inte starta om applikationen automatiskt.\n" +
                    "Starta om FilKollen manuellt för att se branding-ändringarna.",
                    "Omstartfel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}