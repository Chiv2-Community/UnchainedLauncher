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
                listBox.AllowDrop = true;
            }
            else {
                listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
                listBox.PreviewMouseMove -= OnPreviewMouseMove;
                listBox.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
                listBox.Drop -= OnDrop;
                listBox.DragOver -= OnDragOver;
            }
        }

        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (sender is not ListBox listBox) return;
            if (IsClickOnButton(e.OriginalSource)) return;

            _dragStartPoint = e.GetPosition(listBox);
            _isDragging = false;
        }

        private static void OnPreviewMouseMove(object sender, MouseEventArgs e) {
            if (sender is not ListBox listBox) return;
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
            if (IsClickOnButton(e.OriginalSource)) return;

            var currentPosition = e.GetPosition(listBox);
            var diff = _dragStartPoint - currentPosition;

            if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance) return;

            var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (listBoxItem == null) return;

            var item = listBox.ItemContainerGenerator.ItemFromContainer(listBoxItem);
            if (item == null) return;

            _isDragging = true;
            CreateDragAdorner(listBox, listBoxItem);

            var data = new DataObject(DragDataFormat, new DragData(listBox, item));
            DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);

            RemoveDragAdorner();
            _isDragging = false;
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
            var items = listBox.Items;
            if (items.Count == 0) return 0;

            var nearestIndex = -1;
            var nearestDist = double.MaxValue;

            for (var i = 0; i < items.Count; i++) {
                if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;

                var bounds = GetBoundsRelativeToAncestor(container, listBox);
                var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

                var dist = Math.Sqrt(Math.Pow(dropPosition.X - center.X, 2) + Math.Pow(dropPosition.Y - center.Y, 2));
                if (dist < nearestDist) {
                    nearestDist = dist;
                    nearestIndex = i;
                }
            }

            if (nearestIndex < 0) return items.Count;

            var nearestContainer = listBox.ItemContainerGenerator.ContainerFromIndex(nearestIndex) as FrameworkElement;
            if (nearestContainer == null) return items.Count;

            var nearestBounds = GetBoundsRelativeToAncestor(nearestContainer, listBox);
            var nearestCenter = new Point(nearestBounds.X + nearestBounds.Width / 2, nearestBounds.Y + nearestBounds.Height / 2);

            // If drop is after the center, insert after this item
            return dropPosition.X > nearestCenter.X ||
                   (Math.Abs(dropPosition.X - nearestCenter.X) < nearestBounds.Width / 4 && dropPosition.Y > nearestCenter.Y)
                ? nearestIndex + 1
                : nearestIndex;
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

        private static bool IsClickOnButton(object source) =>
            source is DependencyObject dependencyObject && FindAncestor<Button>(dependencyObject) != null;

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject {
            while (current != null) {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
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