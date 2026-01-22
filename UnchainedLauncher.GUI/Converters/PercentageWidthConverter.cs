using System;
using System.Globalization;
using System.Windows.Data;

namespace UnchainedLauncher.GUI.Converters {
    /// <summary>
    /// Converts a percentage (0-100) and total width to the calculated width
    /// </summary>
    public class PercentageWidthConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length != 2) return 0.0;
            if (values[0] is not double percentage) return 0.0;
            if (values[1] is not double totalWidth) return 0.0;

            // Calculate width based on percentage (0-100)
            return (percentage / 100.0) * totalWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
