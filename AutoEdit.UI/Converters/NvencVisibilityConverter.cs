using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoEdit.UI.Converters
{
    public class NvencVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Konverterar en bool eller string till Visibility för NVENC-inställningar
            // Om true eller "h264_nvenc"/"hevc_nvenc" -> Visible, annars Collapsed
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            
            if (value is string stringValue)
            {
                return stringValue.Contains("nvenc", StringComparison.OrdinalIgnoreCase) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
            
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
