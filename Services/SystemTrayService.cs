using System;
using System.Drawing;
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
                // Skapa enkla ikoner programmatiskt (kan ersättas med riktiga .ico filer)
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
                using (var font = new Font("Arial", 8, FontStyle.Bold))
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

            // Context menu
            var contextMenu = new ContextMenuStrip();
            
            contextMenu.Items.Add("Visa FilKollen", null, (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty));
            contextMenu.Items.Add(new ToolStripSeparator());
            
            var protectionItem = new ToolStripMenuItem("Real-time Skydd")
            {
                CheckOnClick = true,
                Checked = _protectionService.IsProtectionActive
            };
            protectionItem.Click += async (s, e) => 
            {
                if (protectionItem.Checked)
                {
                    await _protectionService.StartProtectionAsync();
                }
                else
                {
                    await _protectionService.StopProtectionAsync();
                }
            };
            contextMenu.Items.Add(protectionItem);
            
            var autoCleanItem = new ToolStripMenuItem("Automatisk Rensning")
            {
                CheckOnClick = true,
                Checked = _protectionService.AutoCleanMode
            };
            autoCleanItem.Click += (s, e) => 
            {
                _protectionService.AutoCleanMode = autoCleanItem.Checked;
                ShowNotification("Läge ändrat", 
                    $"Automatisk rensning: {(autoCleanItem.Checked ? "Aktiverad" : "Inaktiverad")}", 
                    ToolTipIcon.Info);
                    
                _logViewer.AddLogEntry(LogLevel.Information, "Settings", 
                    $"🔧 Automatisk rensning {(autoCleanItem.Checked ? "AKTIVERAD" : "INAKTIVERAD")}");
            };
            contextMenu.Items.Add(autoCleanItem);
            
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Avsluta", null, (s, e) => ExitApplicationRequested?.Invoke(this, EventArgs.Empty));
            
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
            
            ShowNotification("FilKollen Säkerhetsstatus", 
                $"Real-time skydd {status}", icon);
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
                
                // Uppdatera context menu
                if (_notifyIcon.ContextMenuStrip?.Items.Count > 2)
                {
                    if (_notifyIcon.ContextMenuStrip.Items[2] is ToolStripMenuItem protectionItem)
                    {
                        protectionItem.Checked = stats.IsActive;
                    }
                    if (_notifyIcon.ContextMenuStrip.Items[3] is ToolStripMenuItem autoCleanItem)
                    {
                        autoCleanItem.Checked = stats.AutoCleanMode;
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