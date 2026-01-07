using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UnchainedLauncher.GUI.Converters {
    public class LineChartPointsConverter : IMultiValueConverter {
        public static LineChartPointsConverter Instance = new LineChartPointsConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 4) return new PointCollection();

            try {
                var history = ToNullableDoubles(values[0], culture);
                var max = System.Convert.ToDouble(values[1], culture);
                var width = System.Convert.ToDouble(values[2], culture);
                var height = System.Convert.ToDouble(values[3], culture);
                var padding = parameter is null ? 0d : System.Convert.ToDouble(parameter, culture);

                if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0d) return new PointCollection();
                if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0d) return new PointCollection();

                var data = history.ToArray();
                if (data.Length == 0) return new PointCollection();

                var usableWidth = Math.Max(0d, width - (2d * padding));
                var usableHeight = Math.Max(0d, height - (2d * padding));
                if (usableWidth <= 0d || usableHeight <= 0d) return new PointCollection();

                var points = new PointCollection(data.Length);
                var denom = Math.Max(1, data.Length - 1);

                for (var i = 0; i < data.Length; i++) {
                    var x = padding + (usableWidth * i / denom);

                    // `null` means "unrecorded". Use NaN Y to avoid drawing a misleading line segment.
                    var sample = data[i];
                    if (!sample.HasValue) {
                        points.Add(new Point(x, double.NaN));
                        continue;
                    }

                    var v = sample.Value;
                    if (double.IsNaN(v) || double.IsInfinity(v) || v < 0d) v = 0d;

                    var ratio = max <= 0d ? 0d : v / max;
                    if (double.IsNaN(ratio) || double.IsInfinity(ratio)) ratio = 0d;
                    ratio = Math.Clamp(ratio, 0d, 1d);

                    var y = padding + (usableHeight * (1d - ratio));
                    points.Add(new Point(x, y));
                }

                return points;
            }
            catch {
                return new PointCollection();
            }
        }

        private static IEnumerable<double?> ToNullableDoubles(object value, CultureInfo culture) {
            if (value is IEnumerable<double?> typedNullable) return typedNullable;
            if (value is IEnumerable<double> typed) return typed.Select(v => (double?)v);

            if (value is IEnumerable enumerable) {
                var list = new List<double?>();
                foreach (var item in enumerable) {
                    if (item is null) {
                        list.Add(null);
                        continue;
                    }

                    try {
                        list.Add(System.Convert.ToDouble(item, culture));
                    }
                    catch {
                        // ignore
                    }
                }

                return list;
            }

            return Enumerable.Empty<double?>();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}