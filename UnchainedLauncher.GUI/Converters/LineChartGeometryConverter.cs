using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UnchainedLauncher.GUI.Converters {
    public class LineChartGeometryConverter : IMultiValueConverter {
        public static LineChartGeometryConverter Instance = new LineChartGeometryConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 4) return Geometry.Empty;

            try {
                var history = ToNullableDoubles(values[0], culture).ToArray();
                var max = System.Convert.ToDouble(values[1], culture);
                var width = System.Convert.ToDouble(values[2], culture);
                var height = System.Convert.ToDouble(values[3], culture);
                var padding = parameter is null ? 0d : System.Convert.ToDouble(parameter, culture);

                if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0d) return Geometry.Empty;
                if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0d) return Geometry.Empty;
                if (history.Length == 0) return Geometry.Empty;

                var usableWidth = Math.Max(0d, width - (2d * padding));
                var usableHeight = Math.Max(0d, height - (2d * padding));
                if (usableWidth <= 0d || usableHeight <= 0d) return Geometry.Empty;

                var denom = Math.Max(1, history.Length - 1);

                // Build a geometry with one figure per contiguous recorded segment.
                // This preserves X positioning across null/unrecorded samples without relying on NaN points
                // (WPF `Polyline` may refuse to render when NaN coordinates are present).
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open()) {
                    var hasOpenFigure = false;
                    Point lastPoint = default;

                    for (var i = 0; i < history.Length; i++) {
                        var x = padding + (usableWidth * i / denom);
                        var sample = history[i];

                        if (!sample.HasValue) {
                            hasOpenFigure = false;
                            continue;
                        }

                        var v = sample.Value;
                        if (double.IsNaN(v) || double.IsInfinity(v) || v < 0d) v = 0d;

                        var ratio = max <= 0d ? 0d : v / max;
                        if (double.IsNaN(ratio) || double.IsInfinity(ratio)) ratio = 0d;
                        ratio = Math.Clamp(ratio, 0d, 1d);

                        var y = padding + (usableHeight * (1d - ratio));
                        var p = new Point(x, y);

                        if (!hasOpenFigure) {
                            ctx.BeginFigure(p, isFilled: false, isClosed: false);
                            hasOpenFigure = true;
                        }
                        else {
                            // Use LineTo for each subsequent point in the segment.
                            // isStroked=true to actually draw.
                            ctx.LineTo(p, isStroked: true, isSmoothJoin: true);
                        }

                        lastPoint = p;
                    }
                }

                geometry.Freeze();
                return geometry;
            }
            catch {
                return Geometry.Empty;
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
                        list.Add(null);
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
