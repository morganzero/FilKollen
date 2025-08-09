// ViewModels/ScanResultViewModel.cs
#nullable disable
using System.ComponentModel;
using FilKollen.Models;

namespace FilKollen.ViewModels
{
    public class ScanResultViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public bool IsSelected 
        { 
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ThreatLevel { get; set; } = "Medium";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FormattedSize { get; set; } = "0 B";
        public string Reason { get; set; } = "";
        public string FileHash { get; set; } = "";
        public bool IsQuarantined { get; set; }
        
        // För UI-styling baserat på threat level
        public string ThreatLevelColor => ThreatLevel switch
        {
            "Low" => "#4CAF50",      // Green
            "Medium" => "#FF9800",   // Orange
            "High" => "#F44336",     // Red
            "Critical" => "#9C27B0", // Purple
            _ => "#9E9E9E"           // Gray
        };
        
        public string ThreatLevelIcon => ThreatLevel switch
        {
            "Low" => "🟢",
            "Medium" => "🟠", 
            "High" => "🔴",
            "Critical" => "🟣",
            _ => "⚪"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        // TILLAGD: Factory method för att skapa från ScanResult
        public static ScanResultViewModel FromScanResult(ScanResult scanResult)
        {
            return new ScanResultViewModel
            {
                FileName = scanResult.FileName,
                FilePath = scanResult.FilePath,
                ThreatLevel = scanResult.ThreatLevel.ToString(),
                Reason = scanResult.Reason,
                FormattedSize = scanResult.FormattedSize,
                FileHash = scanResult.FileHash,
                IsQuarantined = scanResult.IsQuarantined,
                IsSelected = false
            };
        }
    }
}