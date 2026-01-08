using System;
using System.Globalization;
using System.Windows.Data;

namespace UnchainedLauncher.GUI.Converters {
    public class NullableIntToStringConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is null) return string.Empty;
            if (value is int i) return i.ToString(culture);
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is null) return null!;

            var s = value.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null!;

            if (int.TryParse(s.Trim(), NumberStyles.Integer, culture, out var parsed)) {
                return parsed;
            }

            // If parsing fails, keep the existing value by returning Binding.DoNothing.
            return Binding.DoNothing;
        }
    }
}
