using System;
using System.Globalization;
using System.Windows.Data;

namespace UnchainedLauncher.GUI.Converters {
    public class BytesToMiBConverter : IValueConverter {
        public static BytesToMiBConverter Instance = new BytesToMiBConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is null) return 0d;

            try {
                var bytes = System.Convert.ToDouble(value, culture);
                return bytes / (1024d * 1024d);
            }
            catch {
                return 0d;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
