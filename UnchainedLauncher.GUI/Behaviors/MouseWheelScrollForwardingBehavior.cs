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
            if (sender is not DependencyObject d) return;

            // Forward the wheel to the nearest ancestor ScrollViewer.
            // This fixes cases where the event gets handled inside complex controls (e.g., TabControl content)
            // and the outer ScrollViewer never receives it.
            var scrollViewer = FindAncestorScrollViewer(d);
            if (scrollViewer == null) return;

            if (scrollViewer.ScrollableHeight <= 0d) return;

            // Don't interfere with scroll viewers that are themselves handling the wheel.
            if (ReferenceEquals(scrollViewer, d) || IsDescendantOfScrollViewerContentPresenter(d, scrollViewer)) {
                // Still allow forwarding if the event is already marked handled.
            }

            e.Handled = true;

            // Mouse wheel delta is typically 120 per notch; use a simple pixel/DIU mapping.
            var newOffset = scrollViewer.VerticalOffset - e.Delta;
            if (newOffset < 0d) newOffset = 0d;
            if (newOffset > scrollViewer.ScrollableHeight) newOffset = scrollViewer.ScrollableHeight;
            scrollViewer.ScrollToVerticalOffset(newOffset);
        }

        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject d) {
            DependencyObject? current = d;
            while (current != null) {
                if (current is ScrollViewer sv) return sv;

                current = current switch {
                    Visual or Visual3D => VisualTreeHelper.GetParent(current),
                    _ => LogicalTreeHelper.GetParent(current)
                };
            }

            return null;
        }

        private static bool IsDescendantOfScrollViewerContentPresenter(DependencyObject d, ScrollViewer sv) {
            // If the event comes from within the ScrollViewer's own content presenter,
            // the ScrollViewer should normally be able to react to it. We keep this helper
            // to avoid over-assumptions about templates.
            var presenter = sv.Template?.FindName("PART_ScrollContentPresenter", sv) as DependencyObject;
            if (presenter == null) return false;

            DependencyObject? current = d;
            while (current != null) {
                if (ReferenceEquals(current, presenter)) return true;
                current = current switch {
                    Visual or Visual3D => VisualTreeHelper.GetParent(current),
                    _ => LogicalTreeHelper.GetParent(current)
                };
            }

            return false;
        }
    }
}
