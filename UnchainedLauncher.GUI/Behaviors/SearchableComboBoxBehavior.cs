using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.GUI.Behaviors {
    public static class SearchableComboBoxBehavior {
        public static readonly DependencyProperty IsSearchBoxProperty = DependencyProperty.RegisterAttached(
            "IsSearchBox", typeof(bool), typeof(SearchableComboBoxBehavior), new PropertyMetadata(false, OnIsSearchBoxChanged));
        public static readonly DependencyProperty TextConverterProperty = DependencyProperty.RegisterAttached(
            "TextConverter", typeof(IValueConverter), typeof(SearchableComboBoxBehavior));
        public static readonly DependencyProperty TextConverterParameterProperty = DependencyProperty.RegisterAttached(
            "TextConverterParameter", typeof(object), typeof(SearchableComboBoxBehavior));
        public static readonly DependencyProperty SortAlphabeticallyProperty = DependencyProperty.RegisterAttached(
            "SortAlphabetically", typeof(bool), typeof(SearchableComboBoxBehavior), new PropertyMetadata(false));

        public static bool GetSortAlphabetically(DependencyObject target) => (bool)target.GetValue(SortAlphabeticallyProperty);
        public static void SetSortAlphabetically(DependencyObject target, bool value) => target.SetValue(SortAlphabeticallyProperty, value);
        public static bool GetIsSearchBox(DependencyObject target) => (bool)target.GetValue(IsSearchBoxProperty);
        public static void SetIsSearchBox(DependencyObject target, bool value) => target.SetValue(IsSearchBoxProperty, value);
        public static IValueConverter? GetTextConverter(DependencyObject target) => (IValueConverter?)target.GetValue(TextConverterProperty);
        public static void SetTextConverter(DependencyObject target, IValueConverter value) => target.SetValue(TextConverterProperty, value);
        public static object? GetTextConverterParameter(DependencyObject target) => target.GetValue(TextConverterParameterProperty);
        public static void SetTextConverterParameter(DependencyObject target, object value) => target.SetValue(TextConverterParameterProperty, value);

        private static void OnIsSearchBoxChanged(DependencyObject target, DependencyPropertyChangedEventArgs e) {
            if (target is TextBox box && (bool)e.NewValue) {
                State? state = null;
                box.Loaded += (_, _) => {
                    if (state == null && box.TemplatedParent is ComboBox combo &&
                        combo.Template.FindName("PART_SearchResults", combo) is ListBox results) {
                        state = new State(combo, box, results);
                    }
                };
                box.Unloaded += (_, _) => {
                    state?.Dispose();
                    state = null;
                };
            }
        }

        private sealed class State : IDisposable {
            private static readonly DependencyPropertyDescriptor OpenProperty =
                DependencyPropertyDescriptor.FromProperty(ComboBox.IsDropDownOpenProperty, typeof(ComboBox));
            private readonly ComboBox combo;
            private readonly TextBox box;
            private readonly ListBox results;
            private bool updating;

            public State(ComboBox combo, TextBox box, ListBox results) {
                this.combo = combo;
                this.box = box;
                this.results = results;
                box.TextChanged += TextChanged;
                box.GotKeyboardFocus += SearchFocused;
                combo.PreviewKeyDown += KeyDown;
                combo.PreviewTextInput += TextInput;
                combo.PreviewMouseWheel += MouseWheel;
                combo.DropDownOpened += Opened;
                OpenProperty.AddValueChanged(combo, OpenChanged);
                results.PreviewMouseLeftButtonUp += ChooseWithMouse;
                ((INotifyCollectionChanged)combo.Items).CollectionChanged += ItemsChanged;
            }

            private string Label(object item) {
                if (GetTextConverter(combo) is { } converter) {
                    return converter.Convert(item, typeof(string), GetTextConverterParameter(combo)!, CultureInfo.CurrentCulture)?.ToString() ?? "";
                }
                var path = TextSearch.GetTextPath(combo);
                if (string.IsNullOrEmpty(path)) path = combo.DisplayMemberPath;
                object? value = item;
                if (!string.IsNullOrEmpty(path)) {
                    foreach (var part in path.Split('.')) {
                        value = value == null ? null : TypeDescriptor.GetProperties(value)[part]?.GetValue(value);
                    }
                }
                return value?.ToString() ?? "";
            }

            private void Refresh() {
                // Keep filtering and sorting separate from bound selection and shared collection views.
                var previous = results.SelectedItem;
                IEnumerable<object> matches = combo.Items.Cast<object>().Where(item => FuzzySearch.Matches(Label(item), box.Text));
                if (GetSortAlphabetically(combo)) matches = matches.OrderBy(Label, StringComparer.OrdinalIgnoreCase);
                results.ItemsSource = matches.ToList();
                results.SelectedItem = results.Items.Contains(previous) ? previous :
                    results.Items.Contains(combo.SelectedItem) ? combo.SelectedItem : null;
                if (results.SelectedItem == null && results.Items.Count > 0) results.SelectedIndex = 0;
                if (results.SelectedItem != null) results.ScrollIntoView(results.SelectedItem);
            }

            private void SearchFocused(object sender, KeyboardFocusChangedEventArgs e) {
                if (!combo.IsDropDownOpen) combo.IsDropDownOpen = true;
            }

            private void TextChanged(object sender, TextChangedEventArgs e) {
                if (updating) return;
                if (!combo.IsDropDownOpen) combo.IsDropDownOpen = true;
                Refresh();
            }

            private void Opened(object? sender, EventArgs e) {
                results.SelectedItem = null;
                Refresh();
                box.Focus();
            }

            private void OpenChanged(object? sender, EventArgs e) {
                if (combo.IsDropDownOpen) return;
                updating = true;
                box.Clear();
                updating = false;
                if (combo.IsKeyboardFocusWithin) combo.Focus();
            }

            private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
                if (combo.IsDropDownOpen) Refresh();
            }

            private void ChooseWithMouse(object sender, MouseButtonEventArgs e) {
                if (e.OriginalSource is DependencyObject source &&
                    ItemsControl.ContainerFromElement(results, source) is ListBoxItem item) {
                    Commit(item.Content);
                    e.Handled = true;
                }
            }

            private void Commit(object item) {
                combo.SetCurrentValue(ComboBox.SelectedItemProperty, item);
                combo.IsDropDownOpen = false;
            }

            private void TextInput(object sender, TextCompositionEventArgs e) {
                if (!combo.IsKeyboardFocused || string.IsNullOrEmpty(e.Text)) return;
                combo.IsDropDownOpen = true;
                box.Text = e.Text;
                box.CaretIndex = box.Text.Length;
                e.Handled = true;
            }

            private void MouseWheel(object sender, MouseWheelEventArgs e) {
                if (combo.IsDropDownOpen) return;
                // Stop WPF's closed-box wheel selection before its bubbling class handler.
                e.Handled = true;
                MouseWheelScrollForwardingBehavior.FindAncestorScrollViewer(combo)?.RaiseEvent(
                    new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = Mouse.MouseWheelEvent });
            }

            private void KeyDown(object sender, KeyEventArgs e) {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.F4 || ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && (key == Key.Up || key == Key.Down))) {
                    combo.IsDropDownOpen = !combo.IsDropDownOpen;
                    e.Handled = true;
                }
                else if (key == Key.Escape && combo.IsDropDownOpen) {
                    combo.IsDropDownOpen = false;
                    e.Handled = true;
                }
                else if (key == Key.Enter && combo.IsDropDownOpen) {
                    if (results.SelectedItem != null) Commit(results.SelectedItem);
                    e.Handled = true;
                }
                else if (key == Key.Tab && combo.IsDropDownOpen) {
                    // Cancel an unchosen result without handling normal focus traversal.
                    combo.IsDropDownOpen = false;
                }
                else if (key is Key.Down or Key.Up or Key.PageDown or Key.PageUp ||
                         (!box.IsKeyboardFocusWithin && key is Key.Home or Key.End or Key.Left or Key.Right)) {
                    // TextBox keeps Home/End/Left/Right for caret and selection editing.
                    var wasOpen = combo.IsDropDownOpen;
                    if (!wasOpen) combo.IsDropDownOpen = true;
                    if (results.Items.Count > 0) {
                        var step = key switch {
                            Key.PageDown => 10,
                            Key.PageUp => -10,
                            Key.Down => 1,
                            Key.Left => combo.FlowDirection == FlowDirection.RightToLeft ? 1 : -1,
                            Key.Right => combo.FlowDirection == FlowDirection.RightToLeft ? -1 : 1,
                            _ => -1
                        };
                        results.SelectedIndex = key == Key.Home ? 0 : key == Key.End ? results.Items.Count - 1 :
                            wasOpen ? Math.Clamp(results.SelectedIndex + step, 0, results.Items.Count - 1) : results.SelectedIndex;
                        results.ScrollIntoView(results.SelectedItem);
                    }
                    e.Handled = true;
                }
            }

            public void Dispose() {
                box.TextChanged -= TextChanged;
                box.GotKeyboardFocus -= SearchFocused;
                combo.PreviewKeyDown -= KeyDown;
                combo.PreviewTextInput -= TextInput;
                combo.PreviewMouseWheel -= MouseWheel;
                combo.DropDownOpened -= Opened;
                OpenProperty.RemoveValueChanged(combo, OpenChanged);
                results.PreviewMouseLeftButtonUp -= ChooseWithMouse;
                ((INotifyCollectionChanged)combo.Items).CollectionChanged -= ItemsChanged;
            }
        }
    }
}
