using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UnchainedLauncher.GUI.Converters {
    public class RoundedRectClipConverter : IMultiValueConverter {
        public static RoundedRectClipConverter Instance = new RoundedRectClipConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 3) return Geometry.Empty;

            try {
                var width = System.Convert.ToDouble(values[0], culture);
                var height = System.Convert.ToDouble(values[1], culture);
                var radius = System.Convert.ToDouble(values[2], culture);

                if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0d) return Geometry.Empty;
                if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0d) return Geometry.Empty;
                if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0d) radius = 0d;

                // Clip to rounded rect so dropdown item backgrounds can't draw into the corners.
                var geometry = new RectangleGeometry(new Rect(0d, 0d, width, height), radius, radius);
                geometry.Freeze();
                return geometry;
            }
            catch {
                return Geometry.Empty;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
