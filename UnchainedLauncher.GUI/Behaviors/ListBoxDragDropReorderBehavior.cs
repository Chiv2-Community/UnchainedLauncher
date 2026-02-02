using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace UnchainedLauncher.GUI.Behaviors {
    /// <summary>
    /// Attached behavior that enables drag-and-drop reordering of items in a ListBox.
    /// Works with any ItemsSource that implements IList.
    /// </summary>
    public static class ListBoxDragDropReorderBehavior {
        private static readonly string DragDataFormat = "ListBoxDragDropReorder";
        private static Point _dragStartPoint;
        private static bool _isDragging;
        private static DragAdorner? _dragAdorner;
        private static AdornerLayer? _adornerLayer;

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(ListBoxDragDropReorderBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not ListBox listBox) return;

            if ((bool)e.NewValue) {
                listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
                listBox.PreviewMouseMove += OnPreviewMouseMove;
                listBox.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
                listBox.Drop += OnDrop;
                listBox.DragOver += OnDragOver;
                listBox.DragLeave += OnDragLeave;
                listBox.AllowDrop = true;
            }
            else {
                listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
                listBox.PreviewMouseMove -= OnPreviewMouseMove;
                listBox.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
                listBox.Drop -= OnDrop;
                listBox.DragOver -= OnDragOver;
                listBox.DragLeave -= OnDragLeave;
            }
        }

        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (sender is not ListBox listBox) return;

            _dragStartPoint = e.GetPosition(listBox);

            // Check if we clicked on a button (don't start drag from buttons)
            if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null) {
                return;
            }

            _isDragging = false;
        }

        private static void OnPreviewMouseMove(object sender, MouseEventArgs e) {
            if (sender is not ListBox listBox) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (_isDragging) return;

            // Check if we clicked on a button
            if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null) {
                return;
            }

            var currentPosition = e.GetPosition(listBox);
            var diff = _dragStartPoint - currentPosition;

            // Check if the mouse has moved enough to start a drag
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance) {

                var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (listBoxItem == null) return;

                var item = listBox.ItemContainerGenerator.ItemFromContainer(listBoxItem);
                if (item == null) return;

                _isDragging = true;

                // Create adorner for visual feedback
                CreateDragAdorner(listBox, listBoxItem);

                var data = new DataObject(DragDataFormat, new DragData(listBox, item));
                DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);

                RemoveDragAdorner();
                _isDragging = false;
            }
        }

        private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            _isDragging = false;
            RemoveDragAdorner();
        }

        private static void OnDragOver(object sender, DragEventArgs e) {
            if (sender is not ListBox listBox) return;

            if (!e.Data.GetDataPresent(DragDataFormat)) {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;

            // Update adorner position
            if (_dragAdorner != null && _adornerLayer != null) {
                var position = e.GetPosition(listBox);
                _dragAdorner.UpdatePosition(position.X, position.Y);
            }

            e.Handled = true;
        }

        private static void OnDragLeave(object sender, DragEventArgs e) {
            // Keep adorner visible during drag
        }

        private static void OnDrop(object sender, DragEventArgs e) {
            if (sender is not ListBox listBox) return;

            if (!e.Data.GetDataPresent(DragDataFormat)) return;

            var dragData = e.Data.GetData(DragDataFormat) as DragData;
            if (dragData == null) return;

            // Ensure we're dropping on the same listbox
            if (!ReferenceEquals(dragData.SourceListBox, listBox)) return;

            var itemsSource = listBox.ItemsSource as IList;
            if (itemsSource == null) return;

            var draggedItem = dragData.Item;
            var oldIndex = itemsSource.IndexOf(draggedItem);
            if (oldIndex < 0) return;

            // Find the target index based on drop position
            var dropPosition = e.GetPosition(listBox);
            var newIndex = GetInsertionIndex(listBox, dropPosition);

            if (newIndex < 0) newIndex = itemsSource.Count;
            if (newIndex > itemsSource.Count) newIndex = itemsSource.Count;

            // Adjust index if moving forward (since we're removing first)
            if (oldIndex == newIndex || oldIndex + 1 == newIndex) {
                // No movement needed
                e.Handled = true;
                return;
            }

            // Remove and insert
            itemsSource.RemoveAt(oldIndex);
            if (newIndex > oldIndex) newIndex--;
            itemsSource.Insert(newIndex, draggedItem);

            // Select the moved item
            listBox.SelectedItem = draggedItem;

            e.Handled = true;
        }

        private static int GetInsertionIndex(ListBox listBox, Point dropPosition) {
            // Get the items panel (could be WrapPanel, StackPanel, etc.)
            var itemsPanel = FindVisualChild<Panel>(listBox);
            if (itemsPanel == null) return -1;

            var items = listBox.Items;
            if (items.Count == 0) return 0;

            // Find the item closest to the drop position
            var bestIndex = items.Count;
            var bestDistance = double.MaxValue;

            for (var i = 0; i < items.Count; i++) {
                var container = listBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                var itemPosition = container.TransformToAncestor(listBox).Transform(new Point(0, 0));
                var itemCenter = new Point(
                    itemPosition.X + container.ActualWidth / 2,
                    itemPosition.Y + container.ActualHeight / 2);

                // For horizontal layouts (WrapPanel), prioritize X position
                // Check if drop is to the left of this item's center
                var isBeforeItem = dropPosition.X < itemCenter.X ||
                                   (Math.Abs(dropPosition.X - itemCenter.X) < container.ActualWidth / 2 &&
                                    dropPosition.Y < itemCenter.Y);

                // Simple distance-based approach
                var distanceX = Math.Abs(dropPosition.X - itemCenter.X);
                var distanceY = Math.Abs(dropPosition.Y - itemCenter.Y);

                // Weight Y more heavily to handle row changes
                var distance = distanceX + distanceY * 2;

                // Check if this position is before the current item
                if (dropPosition.Y < itemPosition.Y ||
                    (dropPosition.Y < itemPosition.Y + container.ActualHeight &&
                     dropPosition.X < itemPosition.X + container.ActualWidth / 2)) {
                    if (i < bestIndex) {
                        bestIndex = i;
                    }
                }
            }

            // Fallback: find nearest item and decide before/after
            var nearestIndex = -1;
            var nearestDist = double.MaxValue;

            for (var i = 0; i < items.Count; i++) {
                var container = listBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                var itemBounds = GetBoundsRelativeToAncestor(container, listBox);
                var center = new Point(itemBounds.X + itemBounds.Width / 2, itemBounds.Y + itemBounds.Height / 2);

                var dist = Math.Sqrt(Math.Pow(dropPosition.X - center.X, 2) + Math.Pow(dropPosition.Y - center.Y, 2));
                if (dist < nearestDist) {
                    nearestDist = dist;
                    nearestIndex = i;
                }
            }

            if (nearestIndex >= 0) {
                var container = listBox.ItemContainerGenerator.ContainerFromIndex(nearestIndex) as FrameworkElement;
                if (container != null) {
                    var itemBounds = GetBoundsRelativeToAncestor(container, listBox);
                    var center = new Point(itemBounds.X + itemBounds.Width / 2, itemBounds.Y + itemBounds.Height / 2);

                    // If drop is after the center, insert after this item
                    if (dropPosition.X > center.X ||
                        (Math.Abs(dropPosition.X - center.X) < itemBounds.Width / 4 && dropPosition.Y > center.Y)) {
                        return nearestIndex + 1;
                    }
                    return nearestIndex;
                }
            }

            return bestIndex;
        }

        private static Rect GetBoundsRelativeToAncestor(FrameworkElement element, Visual ancestor) {
            var position = element.TransformToAncestor(ancestor).Transform(new Point(0, 0));
            return new Rect(position, new Size(element.ActualWidth, element.ActualHeight));
        }

        private static void CreateDragAdorner(ListBox listBox, ListBoxItem draggedItem) {
            _adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
            if (_adornerLayer == null) return;

            _dragAdorner = new DragAdorner(listBox, draggedItem);
            _adornerLayer.Add(_dragAdorner);
        }

        private static void RemoveDragAdorner() {
            if (_dragAdorner != null && _adornerLayer != null) {
                _adornerLayer.Remove(_dragAdorner);
                _dragAdorner = null;
                _adornerLayer = null;
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject {
            while (current != null) {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;

                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private sealed class DragData(ListBox sourceListBox, object item) {
            public ListBox SourceListBox { get; } = sourceListBox;
            public object Item { get; } = item;
        }

        private sealed class DragAdorner : Adorner {
            private readonly VisualBrush _visualBrush;
            private readonly Size _size;
            private double _left;
            private double _top;

            public DragAdorner(UIElement adornedElement, FrameworkElement draggedElement) : base(adornedElement) {
                _size = new Size(draggedElement.ActualWidth, draggedElement.ActualHeight);
                _visualBrush = new VisualBrush(draggedElement) {
                    Opacity = 0.7,
                    Stretch = Stretch.None
                };
                IsHitTestVisible = false;
            }

            public void UpdatePosition(double left, double top) {
                _left = left - _size.Width / 2;
                _top = top - _size.Height / 2;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext drawingContext) {
                var rect = new Rect(_left, _top, _size.Width, _size.Height);
                drawingContext.DrawRectangle(_visualBrush, null, rect);
            }
        }
    }
}
