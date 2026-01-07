using System;
using System.Globalization;
using System.Windows.Data;

namespace UnchainedLauncher.GUI.Converters {
    public class HistogramBarHeightConverter : IMultiValueConverter {
        public static HistogramBarHeightConverter Instance = new HistogramBarHeightConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 2) return 0d;

            try {
                var v = System.Convert.ToDouble(values[0], culture);
                var max = System.Convert.ToDouble(values[1], culture);
                var height = parameter is null ? 0d : System.Convert.ToDouble(parameter, culture);

                if (height <= 0d) return 0d;
                if (v <= 0d) return 0d;
                if (max <= 0d) return 0d;

                var scaled = height * (v / max);
                if (double.IsNaN(scaled) || double.IsInfinity(scaled)) return 0d;
                return Math.Max(0d, scaled);
            }
            catch {
                return 0d;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}