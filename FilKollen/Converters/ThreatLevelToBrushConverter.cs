using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FilKollen.Models;

namespace FilKollen.Converters
{
    public class ThreatLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ThreatLevel threatLevel)
            {
                return threatLevel switch
                {
                    ThreatLevel.Low => new SolidColorBrush(Color.FromRgb(76, 175, 80)),     // Green
                    ThreatLevel.Medium => new SolidColorBrush(Color.FromRgb(255, 152, 0)),  // Orange  
                    ThreatLevel.High => new SolidColorBrush(Color.FromRgb(244, 67, 54)),    // Red
                    ThreatLevel.Critical => new SolidColorBrush(Color.FromRgb(156, 39, 176)), // Purple
                    _ => new SolidColorBrush(Color.FromRgb(158, 158, 158))                   // Gray
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}