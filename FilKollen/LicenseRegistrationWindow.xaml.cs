// Windows/LicenseRegistrationWindow.xaml.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using FilKollen.Models;
using FilKollen.Services;
using Serilog;

namespace FilKollen.Windows
{
    public partial class LicenseRegistrationWindow : Window, INotifyPropertyChanged
    {
        private readonly LicenseService _licenseService;
        private readonly LicenseKeyGenerator _keyGenerator;
        private readonly ILogger _logger;
        private LicenseKeyInfo? _currentLicenseInfo;
        
        public bool IsTrialMode { get; set; }

        public LicenseRegistrationWindow(LicenseService licenseService, ILogger logger)
        {
            InitializeComponent();
            
            _licenseService = licenseService;
            _keyGenerator = new LicenseKeyGenerator(logger);
            _logger = logger;
            
            DataContext = this;
            
            // Kontrollera om vi är i trial-läge
            CheckTrialStatus();
            
            _logger.Information("License registration window opened");
        }

        private void CheckTrialStatus()
        {
            var trialTime = _licenseService.GetRemainingTrialTime();
            IsTrialMode = trialTime.HasValue && trialTime.GetValueOrDefault() > TimeSpan.Zero;
            
            if (IsTrialMode)
            {
                TrialTimeRemainingText.Text = FormatTimeSpan(trialTime.GetValueOrDefault());
                TrialExpiryDateText.Text = DateTime.UtcNow.Add(trialTime.GetValueOrDefault()).ToString("yyyy-MM-dd");
                StatusHeaderText.Text = "Trial-period aktiv - Registrera licens för fortsatt användning";
            }
            else
            {
                StatusHeaderText.Text = "Trial-period har gått ut - Licensregistrering krävs";
                TrialStatusCard.Visibility = Visibility.Collapsed;
                ContinueTrialButton.Visibility = Visibility.Collapsed;
            }
            
            OnPropertyChanged(nameof(IsTrialMode));
        }

        private void LicenseKeyTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var licenseKey = LicenseKeyTextBox.Text?.Trim().ToUpper() ?? string.Empty;
            
            // Auto-formattering av licensnyckel
            if (licenseKey.Length > 0 && !licenseKey.Contains("-"))
            {
                licenseKey = FormatLicenseKeyInput(licenseKey);
                LicenseKeyTextBox.Text = licenseKey;
                LicenseKeyTextBox.SelectionStart = licenseKey.Length;
            }
            
            ValidateLicenseKeyAsync(licenseKey);
        }

        private string FormatLicenseKeyInput(string input)
        {
            // Ta bort alla icke-alfanumeriska tecken
            var cleaned = Regex.Replace(input, @"[^A-Z0-9]", "");
            
            if (cleaned.Length <= 4)
                return "FILK";
            
            // Formatera som FILK-XXXX-XXXX-XXXX-XXXX
            var formatted = "FILK";
            for (int i = 4; i < cleaned.Length && i < 20; i += 4)
            {
                var group = cleaned.Substring(i, Math.Min(4, cleaned.Length - i));
                formatted += "-" + group;
            }
            
            return formatted;
        }

        private async void ValidateLicenseKeyAsync(string licenseKey)
        {
            try
            {
                LicenseKeyValidationText.Visibility = Visibility.Collapsed;
                LicenseInfoPanel.Visibility = Visibility.Collapsed;
                RegisterButton.IsEnabled = false;
                
                if (string.IsNullOrEmpty(licenseKey) || licenseKey.Length < 24)
                {
                    return;
                }

                // Validera licensnyckel
                var isValid = _keyGenerator.ValidateLicenseKey(licenseKey);
                
                if (isValid)
                {
                    _currentLicenseInfo = _keyGenerator.ExtractLicenseInfo(licenseKey);
                    
                    if (_currentLicenseInfo != null)
                    {
                        // Visa licensinformation
                        LicenseTypeText.Text = _currentLicenseInfo.TypeDisplayName;
                        LicenseExpiryText.Text = _currentLicenseInfo.FormattedExpiryDate;
                        LicenseStatusText.Text = _currentLicenseInfo.IsValid ? "✅ Giltig" : "❌ Utgången";
                        LicenseStatusText.Foreground = _currentLicenseInfo.IsValid ? 
                            Brushes.Green : Brushes.Red;
                        
                        LicenseInfoPanel.Visibility = Visibility.Visible;
                        
                        if (_currentLicenseInfo.IsValid)
                        {
                            LicenseKeyValidationText.Text = "✅ Giltig licensnyckel";
                            LicenseKeyValidationText.Foreground = Brushes.Green;
                            RegisterButton.IsEnabled = CanRegister();
                        }
                        else
                        {
                            LicenseKeyValidationText.Text = "❌ Licensnyckeln har gått ut";
                            LicenseKeyValidationText.Foreground = Brushes.Red;
                        }
                        
                        LicenseKeyValidationText.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    LicenseKeyValidationText.Text = "❌ Ogiltig licensnyckel";
                    LicenseKeyValidationText.Foreground = Brushes.Red;
                    LicenseKeyValidationText.Visibility = Visibility.Visible;
                    _currentLicenseInfo = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"License key validation error: {ex.Message}");
                LicenseKeyValidationText.Text = "❌ Fel vid validering av licensnyckel";
                LicenseKeyValidationText.Foreground = Brushes.Red;
                LicenseKeyValidationText.Visibility = Visibility.Visible;
            }
        }

        private bool CanRegister()
        {
            return _currentLicenseInfo != null &&
                   _currentLicenseInfo.IsValid &&
                   !string.IsNullOrWhiteSpace(CustomerNameTextBox.Text) &&
                   !string.IsNullOrWhiteSpace(CustomerEmailTextBox.Text) &&
                   IsValidEmail(CustomerEmailTextBox.Text);
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLicenseInfo == null || !CanRegister())
            {
                MessageBox.Show("Kontrollera att alla fält är korrekt ifyllda.", 
                    "Ofullständig information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                RegisterButton.IsEnabled = false;
                RegisterButton.Content = "🔄 Registrerar...";

                var success = await _licenseService.RegisterLicenseAsync(
                    LicenseKeyTextBox.Text,
                    CustomerNameTextBox.Text,
                    CustomerEmailTextBox.Text);

                if (success)
                {
                    _logger.Information($"License successfully registered for {CustomerEmailTextBox.Text}");
                    
                    MessageBox.Show(
                        $"🎉 LICENSREGISTRERING SLUTFÖRD!\n\n" +
                        $"✅ Licens: {_currentLicenseInfo.TypeDisplayName}\n" +
                        $"📅 Giltig till: {_currentLicenseInfo.FormattedExpiryDate}\n" +
                        $"👤 Registrerad på: {CustomerNameTextBox.Text}\n\n" +
                        $"FilKollen Pro-funktioner är nu aktiverade!\n" +
                        $"Applikationen startar om för att tillämpa licensändringarna.",
                        "Licensregistrering Slutförd",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Starta om applikationen
                    RestartApplication();
                }
                else
                {
                    MessageBox.Show(
                        "❌ Licensregistreringen misslyckades.\n\n" +
                        "Kontrollera din internetanslutning och försök igen.\n" +
                        "Om problemet kvarstår, kontakta support.",
                        "Registreringsfel",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"License registration error: {ex.Message}");
                MessageBox.Show(
                    $"Ett fel uppstod vid licensregistreringen:\n\n{ex.Message}",
                    "Registreringsfel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RegisterButton.IsEnabled = CanRegister();
                RegisterButton.Content = "🔑 Registrera Licens";
            }
        }

        private void ContinueTrialButton_Click(object sender, RoutedEventArgs e)
        {
            var remainingTime = _licenseService.GetRemainingTrialTime();
            
            if (remainingTime.HasValue && remainingTime.GetValueOrDefault() > TimeSpan.Zero)
            {
                _logger.Information("User chose to continue trial period");
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(
                    "⏰ Trial-perioden har gått ut.\n\n" +
                    "För att fortsätta använda FilKollen måste du registrera en licens.",
                    "Trial Utgången",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void PurchaseLicenseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var purchaseUrl = "https://filkollen.com/purchase";
                Process.Start(new ProcessStartInfo(purchaseUrl) { UseShellExecute = true });
                _logger.Information("User navigated to purchase page");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to open purchase URL: {ex.Message}");
                MessageBox.Show(
                    "Kunde inte öppna köpsidan.\n\n" +
                    "Besök manuellt: https://filkollen.com/purchase",
                    "Webbläsarfel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            var helpMessage = 
                "🔑 LICENSREGISTRERING HJÄLP\n\n" +
                "Licensnyckel format:\n" +
                "FILK-XXXX-XXXX-XXXX-XXXX\n\n" +
                "Vanliga problem:\n" +
                "• Kontrollera att nyckeln är korrekt inskriven\n" +
                "• Säkerställ internetanslutning för validering\n" +
                "• Använd den e-postadress som användes vid köpet\n\n" +
                "Trial-period:\n" +
                "• 14 dagar från första start\n" +
                "• Alla funktioner tillgängliga under trial\n" +
                "• Automatisk påminnelse 3 dagar före utgång\n\n" +
                "Support: support@filkollen.com";

            MessageBox.Show(helpMessage, "Licensregistrering Hjälp", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ContactButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var contactUrl = "mailto:support@filkollen.com?subject=FilKollen Licenssupport";
                Process.Start(new ProcessStartInfo(contactUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to open email client: {ex.Message}");
                MessageBox.Show(
                    "Kunde inte öppna e-postklient.\n\n" +
                    "Kontakta support direkt: support@filkollen.com",
                    "E-postfel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void RestartApplication()
        {
            try
            {
                var currentExecutable = System.Reflection.Assembly.GetExecutingAssembly().Location;
                Process.Start(currentExecutable);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to restart application: {ex.Message}");
                MessageBox.Show(
                    "Kunde inte starta om applikationen automatiskt.\n" +
                    "Starta om FilKollen manuellt för att aktivera licensen.",
                    "Omstartfel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                
                Application.Current.Shutdown();
            }
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
            {
                return $"{(int)timeSpan.TotalDays} dagar, {timeSpan.Hours} timmar";
            }
            else if (timeSpan.TotalHours >= 1)
            {
                return $"{timeSpan.Hours} timmar, {timeSpan.Minutes} minuter";
            }
            else
            {
                return $"{timeSpan.Minutes} minuter";
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Om vi inte är i trial-läge och ingen licens är registrerad, förhindra stängning
            if (!IsTrialMode && _licenseService.GetCurrentLicense() == null)
            {
                var result = MessageBox.Show(
                    "⚠️ VARNING: Ingen giltig licens registrerad\n\n" +
                    "Om du stänger detta fönster utan att registrera en licens " +
                    "kommer FilKollen att avslutas.\n\n" +
                    "Vill du verkligen fortsätta utan licensregistrering?",
                    "Ingen Licens Registrerad",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                // Användaren valde att avsluta utan licens
                _logger.Warning("User closed license registration without valid license");
                Application.Current.Shutdown();
            }

            base.OnClosing(e);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}