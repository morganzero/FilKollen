using System;
using System.ComponentModel;
using FilKollen.Models;

namespace FilKollen.ViewModels
{
    public class ScanResultViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public ScanResult ScanResult { get; }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
        
        public string FileName => ScanResult.FileName;
        public string FilePath => ScanResult.FilePath;
        public string FormattedSize => ScanResult.FormattedSize;
        public DateTime LastModified => ScanResult.LastModified;
        public ThreatLevel ThreatLevel => ScanResult.ThreatLevel;
        public string Reason => ScanResult.Reason;

        public ScanResultViewModel(ScanResult scanResult)
        {
            ScanResult = scanResult;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
