using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace UnchainedLauncher.GUI.Behaviors {
    public enum ChartValueKind {
        MemoryMiB,
        Players
    }

    public static class ChartHoverBehavior {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(ChartHoverBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        public static readonly DependencyProperty HistoryProperty =
            DependencyProperty.RegisterAttached(
                "History",
                typeof(IList),
                typeof(ChartHoverBehavior),
                new PropertyMetadata(null));

        public static void SetHistory(DependencyObject element, IList value) => element.SetValue(HistoryProperty, value);
        public static IList? GetHistory(DependencyObject element) => (IList?)element.GetValue(HistoryProperty);

        public static readonly DependencyProperty SampleStepSecondsProperty =
            DependencyProperty.RegisterAttached(
                "SampleStepSeconds",
                typeof(int),
                typeof(ChartHoverBehavior),
                new PropertyMetadata(10));

        public static void SetSampleStepSeconds(DependencyObject element, int value) => element.SetValue(SampleStepSecondsProperty, value);
        public static int GetSampleStepSeconds(DependencyObject element) => (int)element.GetValue(SampleStepSecondsProperty);

        public static readonly DependencyProperty ValueKindProperty =
            DependencyProperty.RegisterAttached(
                "ValueKind",
                typeof(ChartValueKind),
                typeof(ChartHoverBehavior),
                new PropertyMetadata(ChartValueKind.MemoryMiB));

        public static void SetValueKind(DependencyObject element, ChartValueKind value) => element.SetValue(ValueKindProperty, value);
        public static ChartValueKind GetValueKind(DependencyObject element) => (ChartValueKind)element.GetValue(ValueKindProperty);

        public static readonly DependencyProperty ToolTipTextProperty =
            DependencyProperty.RegisterAttached(
                "ToolTipText",
                typeof(string),
                typeof(ChartHoverBehavior),
                new PropertyMetadata(""));

        public static void SetToolTipText(DependencyObject element, string value) => element.SetValue(ToolTipTextProperty, value);
        public static string GetToolTipText(DependencyObject element) => (string)element.GetValue(ToolTipTextProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not FrameworkElement fe) return;

            if ((bool)e.NewValue) {
                fe.MouseMove += OnMouseMove;
                fe.MouseLeave += OnMouseLeave;
            }
            else {
                fe.MouseMove -= OnMouseMove;
                fe.MouseLeave -= OnMouseLeave;
            }
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e) {
            if (sender is not DependencyObject d) return;
            SetToolTipText(d, "");
        }

        private static void OnMouseMove(object sender, MouseEventArgs e) {
            if (sender is not FrameworkElement fe) return;

            var history = GetHistory(fe);
            if (history == null || history.Count == 0) {
                SetToolTipText(fe, "");
                return;
            }

            var width = fe.ActualWidth;
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0d) {
                SetToolTipText(fe, "");
                return;
            }

            var pos = e.GetPosition(fe);
            var x = Math.Clamp(pos.X, 0d, width);

            var denom = Math.Max(1, history.Count - 1);
            var t = x / width;
            var index = (int)Math.Round(t * denom);
            index = Math.Clamp(index, 0, history.Count - 1);

            var stepSeconds = Math.Max(1, GetSampleStepSeconds(fe));
            var secondsAgoStart = (history.Count - 1 - index) * stepSeconds;
            var startTime = DateTimeOffset.Now - TimeSpan.FromSeconds(secondsAgoStart);
            var endTime = startTime + TimeSpan.FromSeconds(stepSeconds);
            var timeText = $"{startTime:HH:mm:ss} – {endTime:HH:mm:ss}";

            var valueText = FormatValue(fe, history[index]);
            SetToolTipText(fe, string.IsNullOrWhiteSpace(valueText) ? timeText : $"{timeText}\n{valueText}");
        }

        private static string FormatValue(FrameworkElement fe, object? raw) {
            if (raw is null) return "Unrecorded";

            if (!TryConvertToDouble(raw, out var v)) return "Unrecorded";

            var kind = GetValueKind(fe);
            return kind switch {
                ChartValueKind.MemoryMiB => $"{v.ToString("N1", CultureInfo.CurrentCulture)} MiB",
                ChartValueKind.Players => $"{Math.Round(v).ToString("N0", CultureInfo.CurrentCulture)} players",
                _ => v.ToString(CultureInfo.CurrentCulture)
            };
        }

        private static bool TryConvertToDouble(object raw, out double value) {
            try {
                value = System.Convert.ToDouble(raw, CultureInfo.CurrentCulture);
                if (double.IsNaN(value) || double.IsInfinity(value)) {
                    value = 0d;
                }
                return true;
            }
            catch {
                value = 0d;
                return false;
            }
        }
    }
}
