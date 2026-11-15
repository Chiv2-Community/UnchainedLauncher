using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using UnchainedLauncher.Core.Tests.Unit.Services.Processes;
using UnchainedLauncher.GUI.Behaviors;
using UnchainedLauncher.GUI.Converters;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server {
    [Collection("Home launch UI")]
    public class SearchableComboBoxTests(HomeLaunchUi ui) {
        [Fact]
        public void MapResultsSortByVisibleLabelWithoutReorderingSource() => ui.Run(() => {
            MapDto Map(string folder, string label) => new($"TBL/Content/Maps/{folder}/{label}.umap",
                "hash", null, null, null, "Unrelated display metadata", null, null, null, "FFA", null);
            var zulu = Map("A", "TDM_zulu");
            var beta = Map("B", "FFA_beta");
            var alpha = Map("Z", "ffa_Alpha");
            var maps = new ObservableCollection<MapDto> { zulu, beta, alpha };
            var combo = CreateCombo(maps);
            try {
                SearchableComboBoxBehavior.SetTextConverter(combo, MapToTravelStringConverter.Instance);
                SearchableComboBoxBehavior.SetSortAlphabetically(combo, true);
                combo.SelectedItem = zulu;
                var box = (TextBox)combo.Template.FindName("PART_SearchBox", combo);
                var results = (ListBox)combo.Template.FindName("PART_SearchResults", combo);
                combo.IsDropDownOpen = true;
                Assert.Equal(new[] { alpha, beta, zulu }, results.Items.Cast<MapDto>());
                box.Text = "ffa";
                Assert.Equal(new[] { alpha, beta }, results.Items.Cast<MapDto>());
                var aardvark = Map("Y", "FFA_aardvark");
                maps.Add(aardvark);
                Assert.Equal(new[] { aardvark, alpha, beta }, results.Items.Cast<MapDto>());
                box.Clear();
                Assert.Equal(new[] { aardvark, alpha, beta, zulu }, results.Items.Cast<MapDto>());
                combo.IsDropDownOpen = false;
                combo.IsDropDownOpen = true;
                Assert.Equal(new[] { aardvark, alpha, beta, zulu }, results.Items.Cast<MapDto>());
                Assert.Same(zulu, combo.SelectedItem);
                Assert.Equal(new[] { zulu, beta, alpha, aardvark }, maps);
            }
            finally { Window.GetWindow(combo).Close(); }
        });

        [Fact]
        public void InactiveSelectedResultsUseThemeBrushesAndFollowThemeChanges() => ui.Run(() => {
            var resources = new ResourceDictionary {
                Source = new Uri("/UnchainedLauncher;component/Resources/Themes/Inputs.xaml", UriKind.Relative)
            };
            var item = new ListBoxItem {
                Content = "Selected option",
                Resources = resources,
                Style = (Style)resources["SearchResultItem"],
                IsSelected = true
            };
            item.ApplyTemplate();
            var border = (Border)item.Template.FindName("ResultBorder", item);
            Assert.False(System.Windows.Controls.Primitives.Selector.GetIsSelectionActive(item));

            foreach (var theme in new[] { "DarkConstants.xaml", "LightConstants.xaml" }) {
                resources.MergedDictionaries.Clear();
                resources.MergedDictionaries.Add(new ResourceDictionary {
                    Source = new Uri($"/UnchainedLauncher;component/Resources/Themes/{theme}", UriKind.Relative)
                });
                Assert.Same(resources["Brush.PrimaryTransparent"], border.Background);
                Assert.Same(resources["Brush.Primary"], border.BorderBrush);
                Assert.Same(resources["Brush.TextPrimary"], item.Foreground);
            }
        });

        [Fact]
        public void FilteringPreservesSelectionBindingAndOtherSelectors() => ui.Run(() => {
            var items = new ObservableCollection<string> { "Steam", "Epic Games Store", "Xbox Game Pass" };
            var model = new SelectionModel { Selection = "Steam" };
            var combo = CreateCombo(items);
            combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SelectionModel.Selection)) {
                Source = model,
                Mode = BindingMode.TwoWay
            });
            var other = new ComboBox { ItemsSource = items, SelectedItem = "Steam" };
            var box = (TextBox)combo.Template.FindName("PART_SearchBox", combo);
            var results = (ListBox)combo.Template.FindName("PART_SearchResults", combo);

            combo.IsDropDownOpen = true;
            Assert.Equal(items, results.Items.Cast<string>());
            box.Text = "egs";
            Assert.Equal("Epic Games Store", Assert.Single(results.Items.Cast<string>()));
            Assert.Equal("Steam", model.Selection);
            Assert.Equal("Steam", combo.SelectedItem);
            Assert.Equal(3, other.Items.Count);
            Assert.Equal("Steam", other.SelectedItem);

            Press(combo, Key.Enter);
            Assert.Equal("Epic Games Store", model.Selection);
            Assert.False(combo.IsDropDownOpen);
            Assert.Empty(box.Text);
            Assert.True(BindingOperations.IsDataBound(combo, ComboBox.SelectedItemProperty));

            combo.IsDropDownOpen = true;
            Assert.Equal(items, results.Items.Cast<string>());
            box.Text = "no matches";
            Assert.Empty(results.Items);
            Press(combo, Key.Enter);
            Assert.Equal("Epic Games Store", model.Selection);
            Press(combo, Key.Escape);
            Assert.False(combo.IsDropDownOpen);
            Assert.Empty(box.Text);
            Window.GetWindow(combo).Close();
        });

        [Fact]
        public void LiveSourcesAndKeyboardNavigationUseVisibleLabels() => ui.Run(() => {
            var items = new ObservableCollection<Choice> { new("First choice"), new("Second choice") };
            var combo = CreateCombo(items);
            combo.DisplayMemberPath = nameof(Choice.Label);
            combo.SelectedItem = items[0];
            var box = (TextBox)combo.Template.FindName("PART_SearchBox", combo);
            var results = (ListBox)combo.Template.FindName("PART_SearchResults", combo);
            combo.IsDropDownOpen = true;
            box.Text = "sec";
            Assert.Same(items[1], Assert.Single(results.Items.Cast<Choice>()));
            items.Add(new Choice("Special choice"));
            Assert.Equal(2, results.Items.Count);
            Press(combo, Key.Down);
            Press(combo, Key.Enter);
            Assert.Same(items[2], combo.SelectedItem);

            combo.IsDropDownOpen = true;
            box.Text = "sec";
            box.Clear();
            Assert.Equal(3, results.Items.Count);
            combo.IsDropDownOpen = false;
            Window.GetWindow(combo).Close();
        });

        [Theory]
        [InlineData(FocusNavigationDirection.Next, false)]
        [InlineData(FocusNavigationDirection.Previous, false)]
        [InlineData(FocusNavigationDirection.Next, true)]
        [InlineData(FocusNavigationDirection.Previous, true)]
        public void TabTraversalLeavesComboWithoutCommittingSearch(FocusNavigationDirection direction, bool searchOpen) => ui.Run(() => {
            var items = new[] { "Steam", "Epic Games Store", "Xbox Game Pass" };
            var model = new SelectionModel { Selection = "Steam" };
            var combo = CreateCombo(items);
            var window = Window.GetWindow(combo);
            try {
                combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SelectionModel.Selection)) {
                    Source = model,
                    Mode = BindingMode.TwoWay
                });
                var before = new Button { Content = "Before" };
                var after = new Button { Content = "After" };
                window.Content = null;
                window.Content = new StackPanel { Children = { before, combo, after } };
                window.Activate();
                window.UpdateLayout();
                Assert.True(combo.Focus());
                var box = (TextBox)combo.Template.FindName("PART_SearchBox", combo);
                if (searchOpen) {
                    combo.IsDropDownOpen = true;
                    Assert.True(box.IsKeyboardFocused);
                    box.Text = "egs";
                    // Route the preview, then run the unhandled Tab's normal WPF focus traversal.
                    Assert.False(Press(box, Key.Tab).Handled);
                }
                Assert.False(combo.IsDropDownOpen);
                Assert.True(combo.IsKeyboardFocused);
                Assert.True(combo.MoveFocus(new TraversalRequest(direction)));
                Assert.Same(direction == FocusNavigationDirection.Next ? after : before, Keyboard.FocusedElement);
                Assert.False(combo.IsDropDownOpen);
                Assert.Empty(box.Text);
                Assert.Equal("Steam", model.Selection);
                Assert.Equal("Steam", combo.SelectedItem);
                Assert.True(BindingOperations.IsDataBound(combo, ComboBox.SelectedItemProperty));
            }
            finally { window.Close(); }
        });

        private static ComboBox CreateCombo(System.Collections.IEnumerable items) {
            var resources = new ResourceDictionary {
                Source = new Uri("/UnchainedLauncher;component/Resources/Themes/Inputs.xaml", UriKind.Relative)
            };
            var combo = new ComboBox { ItemsSource = items, Resources = resources, Style = (Style)resources[typeof(ComboBox)], Width = 250 };
            var window = new Window { Content = combo, Width = 300, Height = 100, Left = -10000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            return combo;
        }

        private static KeyEventArgs Press(UIElement target, Key key) {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, new StubPresentationSource(), 0, key) {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            target.RaiseEvent(args);
            return args;
        }

        private sealed class StubPresentationSource : PresentationSource {
            public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
            public override bool IsDisposed => false;
            protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
        }

        public sealed class SelectionModel {
            public string Selection { get; set; } = "";
        }

        private sealed record Choice(string Label);
    }
}
