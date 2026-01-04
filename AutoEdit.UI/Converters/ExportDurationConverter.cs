using System;
using System.Globalization;
using System.Windows.Data;

namespace AutoEdit.UI.Converters
{
    public class ExportDurationConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is double outPoint && values[1] is double inPoint)
            {
                double duration = outPoint - inPoint;
                return $"{duration:F1}";
            }
            return "0.0";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
