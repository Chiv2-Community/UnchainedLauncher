using System;
using System.Globalization;
using System.Windows.Data;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Converters {
    public class UptimeSegmentTooltipConverter : IMultiValueConverter {
        public static UptimeSegmentTooltipConverter Instance = new UptimeSegmentTooltipConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            try {
                if (values.Length < 3) return "";

                // values: [0]=state, [1]=index, [2]=count
                var stateObj = values[0];

                if (!TryToInt(values[1], culture, out var index)) return "";
                if (!TryToInt(values[2], culture, out var count)) return "";
                if (count <= 0 || index < 0 || index >= count) return "";

                var stepSeconds = 10;
                if (parameter != null && TryToInt(parameter, culture, out var p) && p > 0) {
                    stepSeconds = p;
                }

                var now = DateTimeOffset.Now;
                var secondsFromNow = (count - 1 - index) * stepSeconds;
                var end = now - TimeSpan.FromSeconds(secondsFromNow);
                var start = end - TimeSpan.FromSeconds(stepSeconds);

                var stateText = StateToText(stateObj);
                return $"{start:HH:mm:ss} – {end:HH:mm:ss}  ({stateText})";
            }
            catch {
                return "";
            }
        }

        private static string StateToText(object stateObj) {
            if (stateObj is null) return "Unrecorded";
            if (stateObj is UptimeState s) return s.ToString();

            if (stateObj is string str) {
                if (string.IsNullOrWhiteSpace(str)) return "Unrecorded";
                return str;
            }

            return stateObj.ToString() ?? "";
        }

        private static bool TryToInt(object value, CultureInfo culture, out int result) {
            try {
                if (value is int i) {
                    result = i;
                    return true;
                }

                result = System.Convert.ToInt32(value, culture);
                return true;
            }
            catch {
                result = 0;
                return false;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
