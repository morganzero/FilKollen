using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using FilKollen.Models;
using FilKollen.Services;
using Serilog;
using Application = System.Windows.Application;

namespace FilKollen.Services
{
    public class SystemTrayService : IDisposable
    {
        private NotifyIcon _notifyIcon = null!;
        private readonly RealTimeProtectionService _protectionService;
        private readonly LogViewerService _logViewer;
        private readonly ILogger _logger;
        private Icon _protectedIcon = null!;
        private Icon _unprotectedIcon = null!;
        private Icon _alertIcon = null!;

        public event EventHandler? ShowMainWindowRequested;
        public event EventHandler? ExitApplicationRequested;
        public event EventHandler? QuickScanRequested;
        public event EventHandler? ShowSettingsRequested;
        public event EventHandler? ToggleProtectionRequested;

        public SystemTrayService(RealTimeProtectionService protectionService,
            LogViewerService logViewer, ILogger logger)
        {
            _protectionService = protectionService;
            _logViewer = logViewer;
            _logger = logger;

            InitializeIcons();
            InitializeTrayIcon();

            // Prenumerera på protection events
            _protectionService.ProtectionStatusChanged += OnProtectionStatusChanged;
            _protectionService.ThreatDetected += OnThreatDetected;
        }

        private void InitializeIcons()
        {
            try
            {
                // Skapa dynamiska ikoner med status-indikatorer
                _protectedIcon = CreateDynamicIcon(Color.FromArgb(59, 168, 228), true);  // Blå med grön indikator
                _unprotectedIcon = CreateDynamicIcon(Color.FromArgb(59, 168, 228), false); // Blå med orange varning
                _alertIcon = CreateDynamicIcon(Color.FromArgb(255, 152, 0), true); // Orange för alerts
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to create custom icons: {ex.Message}");
                // Fallback till systemikoner
                _protectedIcon = SystemIcons.Shield;
                _unprotectedIcon = SystemIcons.Error;
                _alertIcon = SystemIcons.Warning;
            }
        }

        private Icon CreateDynamicIcon(Color baseColor, bool isProtected)
        {
            var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                
                // Huvudikon (FilKollen sköld)
                using (var brush = new SolidBrush(baseColor))
                {
                    var shieldPoints = new[]
                    {
                        new Point(8, 1),    // Top center
                        new Point(13, 4),   // Top right
                        new Point(13, 10),  // Bottom right
                        new Point(8, 15),   // Bottom point
                        new Point(3, 10),   // Bottom left
                        new Point(3, 4)     // Top left
                    };
                    g.FillPolygon(brush, shieldPoints);
                }
                
                // Status-indikator (liten cirkel)
                Color indicatorColor = isProtected 
                    ? Color.FromArgb(34, 197, 94)    // Grön för skyddat
                    : Color.FromArgb(251, 146, 60);  // Orange för oskyddat
                    
                using (var brush = new SolidBrush(indicatorColor))
                {
                    g.FillEllipse(brush, 10, 10, 6, 6);
                }
                
                // Vit kant runt indikator
                using (var pen = new Pen(Color.White, 1))
                {
                    g.DrawEllipse(pen, 10, 10, 6, 6);
                }
            }
            
            return Icon.FromHandle(bitmap.GetHicon());
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = _unprotectedIcon,
                Text = "FilKollen - Säkerhetsscanner",
                Visible = true
            };

            // FÖRBÄTTRAD funktionell context menu
            var contextMenu = new ContextMenuStrip();

            // Visa/Dölj FilKollen
            var showHideItem = contextMenu.Items.Add("📊 Visa FilKollen");
            showHideItem.Click += (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);

            contextMenu.Items.Add(new ToolStripSeparator());

            // Auto-skydd toggle
            var protectionItem = contextMenu.Items.Add("🛡️ Auto-skydd: AV");
            protectionItem.Click += (s, e) => ToggleProtection();

            // Snabbskanning
            var scanItem = contextMenu.Items.Add("🔍 Snabbskanna");
            scanItem.Click += (s, e) => QuickScanRequested?.Invoke(this, EventArgs.Empty);

            contextMenu.Items.Add(new ToolStripSeparator());

            // Inställningar
            var settingsItem = contextMenu.Items.Add("⚙️ Inställningar");
            settingsItem.Click += (s, e) => ShowSettingsRequested?.Invoke(this, EventArgs.Empty);

            contextMenu.Items.Add(new ToolStripSeparator());

            // Avsluta
            var exitItem = contextMenu.Items.Add("❌ Avsluta");
            exitItem.Click += (s, e) => ExitApplicationRequested?.Invoke(this, EventArgs.Empty);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double-click för att visa huvudfönster
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);

            UpdateTrayStatus();
        }

        private void ToggleProtection()
        {
            try
            {
                ToggleProtectionRequested?.Invoke(this, EventArgs.Empty);
                
                // Uppdatera meny-text direkt
                if (_notifyIcon.ContextMenuStrip?.Items.Count > 2)
                {
                    var stats = _protectionService.GetProtectionStats();
                    var protectionItem = _notifyIcon.ContextMenuStrip.Items[2];
                    protectionItem.Text = stats.IsActive ? "🛡️ Auto-skydd: PÅ" : "🛡️ Auto-skydd: AV";
                }
                
                _logger.Information($"Protection toggled via system tray");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to toggle protection from tray: {ex.Message}");
            }
        }

        private void OnProtectionStatusChanged(object? sender, ProtectionStatusChangedEventArgs e)
        {
            UpdateTrayStatus();

            var status = e.IsActive ? "AKTIVERAT" : "INAKTIVERAT";
            var icon = e.IsActive ? ToolTipIcon.Info : ToolTipIcon.Warning;

            ShowNotification("FilKollen Auto-skydd",
                $"Auto-skydd {status}", icon);

            // Uppdatera context menu
            if (_notifyIcon.ContextMenuStrip?.Items.Count > 2)
            {
                var protectionItem = _notifyIcon.ContextMenuStrip.Items[2];
                protectionItem.Text = e.IsActive ? "🛡️ Auto-skydd: PÅ" : "🛡️ Auto-skydd: AV";
            }
        }

        private void OnThreatDetected(object? sender, ThreatDetectedEventArgs e)
        {
            var threat = e.Threat;
            var fileName = System.IO.Path.GetFileName(threat.FilePath);

            // Temporärt visa alert-ikon
            _notifyIcon.Icon = _alertIcon;
            
            // Återställ ikon efter 3 sekunder
            System.Threading.Tasks.Task.Delay(3000).ContinueWith(t => 
            {
                try
                {
                    UpdateTrayStatus();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to restore tray icon: {ex.Message}");
                }
            });

            var title = $"🚨 SÄKERHETSHOT IDENTIFIERAT";
            var message = $"Fil: {fileName}\nHot-nivå: {threat.ThreatLevel}\nÅtgärd: {(e.WasHandledAutomatically ? "Auto-rensad" : "Väntar på handling")}";

            ShowNotification(title, message, ToolTipIcon.Warning, 5000);

            // Visa även mer detaljerad balloon om det är kritiskt
            if (threat.ThreatLevel >= ThreatLevel.High)
            {
                _notifyIcon.ShowBalloonTip(8000,
                    "🚨 KRITISKT SÄKERHETSHOT!",
                    $"{fileName} identifierat som {threat.ThreatLevel} hot.\n{threat.Reason}",
                    ToolTipIcon.Error);
            }
        }

        public void ShowNotification(string title, string message, ToolTipIcon icon, int timeout = 3000)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(timeout, title, message, icon);
                _logger.Information($"Tray notification: {title} - {message}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte visa system notification: {ex.Message}");
            }
        }

        public void ShowThreatSummaryNotification(int threatsFound, int threatsHandled)
        {
            var title = "📊 FilKollen Säkerhetsrapport";
            var message = $"Hot identifierade: {threatsFound}\nHot hanterade: {threatsHandled}\nKlicka för att se detaljer";

            ShowNotification(title, message, ToolTipIcon.Info, 5000);
        }

        private void UpdateTrayStatus()
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var stats = _protectionService.GetProtectionStats();

                    // Uppdatera ikon med korrekt status-indikator
                    _notifyIcon.Icon = stats.IsActive ? _protectedIcon : _unprotectedIcon;

                    // Förbättrad tooltip med mer information
                    var autoStatus = stats.AutoCleanMode ? " (Auto-rensning)" : " (Manuell hantering)";
                    var statusText = stats.IsActive 
                        ? $"FilKollen - SKYDDAT{autoStatus}\nSenaste skanning: {stats.LastScanTime:HH:mm}\nHot hanterade: {stats.TotalThreatsHandled}"
                        : $"FilKollen - OSKYDDAT\nKlicka för att aktivera skydd";

                    _notifyIcon.Text = statusText.Length > 63 ? statusText.Substring(0, 60) + "..." : statusText;

                    // Uppdatera "Visa FilKollen" text i context menu
                    if (_notifyIcon.ContextMenuStrip?.Items.Count > 0)
                    {
                        var showItem = _notifyIcon.ContextMenuStrip.Items[0];
                        var threatsText = stats.TotalThreatsFound > 0 ? $" ({stats.TotalThreatsFound} hot)" : "";
                        showItem.Text = $"📊 Visa FilKollen{threatsText}";
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error updating tray status: {ex.Message}");
            }
        }

        public void SetMainWindowVisibility(bool visible)
        {
            try
            {
                // Uppdatera context menu text
                if (_notifyIcon.ContextMenuStrip?.Items.Count > 0)
                {
                    var showItem = _notifyIcon.ContextMenuStrip.Items[0];
                    var stats = _protectionService.GetProtectionStats();
                    var threatsText = stats.TotalThreatsFound > 0 ? $" ({stats.TotalThreatsFound} hot)" : "";
                    showItem.Text = visible ? $"🙈 Dölj FilKollen{threatsText}" : $"📊 Visa FilKollen{threatsText}";
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error updating main window visibility status: {ex.Message}");
            }
        }

        public void UpdateProtectionStats(int threatsFound, int threatsHandled, bool isActive)
        {
            try
            {
                // Uppdatera tooltip med senaste statistik
                var autoStatus = isActive ? "AKTIVT" : "INAKTIVT";
                var statsText = $"FilKollen - Skydd {autoStatus}\nHot funna: {threatsFound}\nHot hanterade: {threatsHandled}";
                
                _notifyIcon.Text = statsText.Length > 63 ? statsText.Substring(0, 60) + "..." : statsText;
                
                // Uppdatera ikon om nödvändigt
                UpdateTrayStatus();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error updating protection stats: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                // Avregistrera events
                if (_protectionService != null)
                {
                    _protectionService.ProtectionStatusChanged -= OnProtectionStatusChanged;
                    _protectionService.ThreatDetected -= OnThreatDetected;
                }

                // Dispose tray icon och resources
                _notifyIcon?.Dispose();
                _protectedIcon?.Dispose();
                _unprotectedIcon?.Dispose();
                _alertIcon?.Dispose();

                _logger.Information("SystemTrayService disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error disposing SystemTrayService: {ex.Message}");
            }
        }
    }
}