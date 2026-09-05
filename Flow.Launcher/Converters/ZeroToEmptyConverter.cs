using System;
using System.Globalization;
using System.Windows.Data;

namespace Flow.Launcher.Converters
{
    public class ZeroToEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 0 ? string.Empty : intValue.ToString(CultureInfo.InvariantCulture);

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
                ? port
                : 0;
        }
    }
}
