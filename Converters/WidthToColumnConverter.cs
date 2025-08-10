using System;
using System.Globalization;
using System.Windows.Data;

namespace FilKollen.Converters
{
    public class WidthToColumnConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                // Return true if width is less than 1100px (should use 1 column layout)
                return width < 1100;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}