using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using FilKollen.Models;
using FilKollen.Services;
using Serilog;
using Application = System.Windows.Application;
using FontStyle = System.Drawing.FontStyle;

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
        public event EventHandler? ClearThreatsRequested;
        public event EventHandler? ShowSettingsRequested;

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
                // Skapa enkla ikoner programmatiskt
                _protectedIcon = CreateIcon(Color.Green, "✓");
                _unprotectedIcon = CreateIcon(Color.Red, "!");
                _alertIcon = CreateIcon(Color.Orange, "⚠");
            }
            catch
            {
                // Fallback till systemikoner
                _protectedIcon = SystemIcons.Shield;
                _unprotectedIcon = SystemIcons.Error;
                _alertIcon = SystemIcons.Warning;
            }
        }

        private Icon CreateIcon(Color color, string text)
        {
            var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, 0, 0, 16, 16);
                }
                using (var font = new Font(new FontFamily("Arial"), 8, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.White))
                {
                    var size = g.MeasureString(text, font);
                    g.DrawString(text, font, brush,
                        (16 - size.Width) / 2, (16 - size.Height) / 2);
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

            // Förbättrad context menu enligt din feedback
            var contextMenu = new ContextMenuStrip();

            // Visa/Dölj FilKollen
            contextMenu.Items.Add("Visa FilKollen", null, (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty));
            contextMenu.Items.Add(new ToolStripSeparator());

            // Snabbskanna datorn (direkt åtgärd istället för toggle)
            contextMenu.Items.Add("🔍 Snabbskanna datorn", null, (s, e) => QuickScanRequested?.Invoke(this, EventArgs.Empty));

            // Visa säkerhetsstatus
            var statusItem = new ToolStripMenuItem("📊 Visa säkerhetsstatus")
            {
                Enabled = false // Bara för att visa status
            };
            contextMenu.Items.Add(statusItem);

            // Rensa hot (endast synlig om hot finns)
            var clearThreatsItem = new ToolStripMenuItem("🧹 Rensa hot");
            clearThreatsItem.Click += (s, e) => ClearThreatsRequested?.Invoke(this, EventArgs.Empty);
            clearThreatsItem.Visible = false; // Dold tills hot finns
            contextMenu.Items.Add(clearThreatsItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            // Inställningar
            contextMenu.Items.Add("⚙️ Inställningar", null, (s, e) => ShowSettingsRequested?.Invoke(this, EventArgs.Empty));

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("❌ Avsluta", null, (s, e) => ExitApplicationRequested?.Invoke(this, EventArgs.Empty));

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double-click för att visa huvudfönster
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);

            UpdateTrayStatus();
        }

        private void OnProtectionStatusChanged(object? sender, ProtectionStatusChangedEventArgs e)
        {
            UpdateTrayStatus();

            var status = e.IsActive ? "AKTIVERAT" : "INAKTIVERAT";
            var icon = e.IsActive ? ToolTipIcon.Info : ToolTipIcon.Warning;

            ShowNotification("FilKollen Auto-skydd",
                $"Auto-skydd {status}", icon);
        }

        private void OnThreatDetected(object? sender, ThreatDetectedEventArgs e)
        {
            var threat = e.Threat;
            var fileName = System.IO.Path.GetFileName(threat.FilePath);

            // Temporärt visa alert-ikon
            _notifyIcon.Icon = _alertIcon;
            System.Threading.Tasks.Task.Delay(3000).ContinueWith(t => UpdateTrayStatus());

            var title = $"🚨 SÄKERHETSHOT IDENTIFIERAT";
            var message = $"Fil: {fileName}\nHot-nivå: {threat.ThreatLevel}\nÅtgärd: {(e.WasHandledAutomatically ? "Auto-rensad" : "Väntar på handling")}";

            ShowNotification(title, message, ToolTipIcon.Warning, 5000);

            // Uppdatera context menu för att visa "Rensa hot" alternativet
            if (_notifyIcon.ContextMenuStrip?.Items.Count > 4)
            {
                _notifyIcon.ContextMenuStrip.Items[4].Visible = true; // "Rensa hot" item
            }

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

        public void UpdateThreatsStatus(bool hasThreats)
        {
            try
            {
                if (_notifyIcon.ContextMenuStrip?.Items.Count > 4)
                {
                    // Uppdatera "Rensa hot" synlighet
                    _notifyIcon.ContextMenuStrip.Items[4].Visible = hasThreats;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Kunde inte uppdatera tray threats status: {ex.Message}");
            }
        }

        private void UpdateTrayStatus()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var stats = _protectionService.GetProtectionStats();

                _notifyIcon.Icon = stats.IsActive ? _protectedIcon : _unprotectedIcon;

                var autoStatus = stats.AutoCleanMode ? " (Auto-rensning)" : " (Manuell hantering)";
                var statusText = stats.IsActive ?
                    $"FilKollen - SKYDDAT{autoStatus}" :
                    "FilKollen - OSKYDDAT";

                _notifyIcon.Text = statusText;

                // Uppdatera "Visa säkerhetsstatus" text i context menu
                if (_notifyIcon.ContextMenuStrip?.Items.Count > 3)
                {
                    var statusItem = _notifyIcon.ContextMenuStrip.Items[3] as ToolStripMenuItem;
                    if (statusItem != null)
                    {
                        statusItem.Text = stats.IsActive ?
                            "📊 Skydd aktivt" :
                            "📊 Skydd avstängt";
                    }
                }
            });
        }

        public void SetMainWindowVisibility(bool visible)
        {
            // Uppdatera context menu text
            if (_notifyIcon.ContextMenuStrip?.Items.Count > 0)
            {
                _notifyIcon.ContextMenuStrip.Items[0].Text = visible ? "Dölj FilKollen" : "Visa FilKollen";
            }
        }

        public void Dispose()
        {
            _protectionService.ProtectionStatusChanged -= OnProtectionStatusChanged;
            _protectionService.ThreatDetected -= OnThreatDetected;

            _notifyIcon?.Dispose();
            _protectedIcon?.Dispose();
            _unprotectedIcon?.Dispose();
            _alertIcon?.Dispose();
        }
    }
}