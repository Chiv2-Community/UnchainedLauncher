using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace UnchainedLauncher.GUI.Behaviors {
    public static class MouseWheelScrollForwardingBehavior {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(MouseWheelScrollForwardingBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not UIElement el) return;

            if ((bool)e.NewValue) {
                el.PreviewMouseWheel += OnPreviewMouseWheel;
            }
            else {
                el.PreviewMouseWheel -= OnPreviewMouseWheel;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (sender is not UIElement element) return;

            // Only forward scroll events when the mouse is directly over this element.
            // This prevents double scrolling when the mouse is elsewhere on the page.
            if (!element.IsMouseOver) return;

            // Don't forward if we're over a nested scrollable control (e.g., ComboBox, nested ScrollViewer).
            // But if 'element' itself is a scrollable control (like a ListBox), we still want to forward 
            // from its contents to the parent ScrollViewer.
            if (IsOverNestedScrollableControl(e.OriginalSource as DependencyObject, element)) return;

            // Forward the wheel to the nearest ancestor ScrollViewer.
            var scrollViewer = FindAncestorScrollViewer(element);
            if (scrollViewer == null) return;

            if (scrollViewer.ScrollableHeight <= 0d) return;

            e.Handled = true;

            // Mouse wheel delta is typically 120 per notch; use a simple pixel/DIU mapping.
            var newOffset = scrollViewer.VerticalOffset - (e.Delta / 2d);
            if (newOffset < 0d) newOffset = 0d;
            if (newOffset > scrollViewer.ScrollableHeight) newOffset = scrollViewer.ScrollableHeight;
            scrollViewer.ScrollToVerticalOffset(newOffset);
        }

        private static bool IsOverNestedScrollableControl(DependencyObject? d, UIElement behaviorElement) {
            while (d != null && !ReferenceEquals(d, behaviorElement)) {
                if (d is ScrollViewer or ComboBox or ListBox or ListView or DataGrid or TextBox { AcceptsReturn: true })
                    return true;

                d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject d) {
            // Start searching from the parent, otherwise we'll just find ourselves if 'd' is a ScrollViewer.
            DependencyObject? current = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            while (current != null) {
                if (current is ScrollViewer sv) return sv;

                current = current switch {
                    Visual or Visual3D => VisualTreeHelper.GetParent(current),
                    _ => LogicalTreeHelper.GetParent(current)
                };
            }

            return null;
        }
    }
}