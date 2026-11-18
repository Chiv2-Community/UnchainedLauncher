using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using UnchainedLauncher.GUI.Behaviors;
using UnchainedLauncher.GUI.Converters;
using UnchainedLauncher.GUI.ViewModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class SearchableComboBoxTests {
    [Fact]
    public void FilteringPreservesBindingUntilEnterOrMouseCommit() => RunSta(() => {
        var items = new ObservableCollection<string> { "Steam", "Epic Games Store", "Xbox Game Pass" };
        using var host = new Host(items);
        var model = new SelectionModel { Selection = "Steam" };
        host.Combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SelectionModel.Selection)) {
            Source = model, Mode = BindingMode.TwoWay
        });
        var other = new ComboBox { ItemsSource = items, SelectedItem = "Steam" };
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "egs";
        Assert.Equal("Epic Games Store", Assert.Single(host.Results.Items.Cast<string>()));
        Assert.Equal("Steam", model.Selection);
        Assert.Equal(3, other.Items.Count);
        Assert.Equal("Steam", other.SelectedItem);
        Press(host.Box, Key.Enter);
        Assert.Equal("Epic Games Store", model.Selection);
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.Empty(host.Box.Text);
        Assert.True(BindingOperations.IsDataBound(host.Combo, ComboBox.SelectedItemProperty));
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "no matches";
        Press(host.Box, Key.Enter);
        Assert.Equal("Epic Games Store", model.Selection);
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "xgp";
        host.Window.UpdateLayout();
        var item = (ListBoxItem)host.Results.ItemContainerGenerator.ContainerFromIndex(0);
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
            RoutedEvent = Mouse.PreviewMouseUpEvent
        });
        Assert.Equal("Xbox Game Pass", model.Selection);
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.True(BindingOperations.IsDataBound(host.Combo, ComboBox.SelectedItemProperty));
    });

    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.Tab)]
    public void CancelPreservesSelectionAndTabTraversal(Key key) => RunSta(() => {
        using var host = new Host(new[] { "Steam", "Epic Games Store" });
        host.Combo.SelectedIndex = 0;
        host.Window.Activate();
        host.Combo.Focus();
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "egs";
        var args = Press(host.Box, key);
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.Equal("Steam", host.Combo.SelectedItem);
        Assert.Empty(host.Box.Text);
        if (key == Key.Tab) {
            Assert.False(args.Handled);
            foreach (var direction in new[] { FocusNavigationDirection.Next, FocusNavigationDirection.Previous }) {
                host.Combo.Focus();
                Assert.True(host.Combo.MoveFocus(new TraversalRequest(direction)));
                Assert.Same(direction == FocusNavigationDirection.Next ? host.After : host.Before, Keyboard.FocusedElement);
            }
        }
    });

    [Fact]
    public void VisibleLabelsSortWithoutChangingSourceAndRefreshLiveItems() => RunSta(() => {
        var zulu = new Choice("Zulu map");
        var beta = new Choice("Beta map");
        var alpha = new Choice("alpha map");
        var items = new ObservableCollection<Choice> { zulu, beta, alpha };
        using var host = new Host(items);
        host.Combo.DisplayMemberPath = nameof(Choice.Label);
        SearchableComboBoxBehavior.SetSortAlphabetically(host.Combo, true);
        host.Combo.SelectedItem = zulu;
        host.Combo.IsDropDownOpen = true;
        Assert.Equal(new[] { alpha, beta, zulu }, host.Results.Items.Cast<Choice>());
        host.Box.Text = "map";
        Assert.Equal(3, host.Results.Items.Count);
        var aardvark = new Choice("Aardvark map");
        items.Add(aardvark);
        Assert.Equal(new[] { aardvark, alpha, beta, zulu }, host.Results.Items.Cast<Choice>());
        Assert.Equal(new[] { zulu, beta, alpha, aardvark }, items);
        host.Box.Text = "beta";
        Assert.Same(beta, Assert.Single(host.Results.Items.Cast<Choice>()));
        Press(host.Box, Key.Escape);
        Assert.Same(zulu, host.Combo.SelectedItem);
        host.Combo.IsDropDownOpen = true;
        Assert.Equal(4, host.Results.Items.Count);
    });

    [Fact]
    public void PluginSortSearchUsesConvertedLabelAndParameter() => RunSta(() => {
        using var host = new Host(Enum.GetValues<ModSortMode>());
        SearchableComboBoxBehavior.SetTextConverter(host.Combo, new ModSortModeToStringConverter());
        SearchableComboBoxBehavior.SetTextConverterParameter(host.Combo, "Plugins");
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "plugins";
        Assert.Equal(ModSortMode.NewestModsFirst, Assert.Single(host.Results.Items.Cast<ModSortMode>()));
        host.Box.Text = "mods";
        Assert.Empty(host.Results.Items);
    });

    [Fact]
    public void ResultSelectionTracksThemeEvenWithoutKeyboardFocus() => RunSta(() => {
        using var host = new Host(new[] { "Selected option" });
        host.Combo.IsDropDownOpen = true;
        host.Window.UpdateLayout();
        var item = (ListBoxItem)host.Results.ItemContainerGenerator.ContainerFromIndex(0);
        item.ApplyTemplate();
        var border = (Border)item.Template.FindName("ResultBorder", item);
        foreach (var theme in new[] { "DarkConstants", "LightConstants" }) {
            var constants = (ResourceDictionary)XamlReader.Parse(ProgressPresentationTests.Load(theme).ToString());
            host.Combo.Resources.MergedDictionaries.Clear();
            host.Combo.Resources.MergedDictionaries.Add(constants);
            Assert.Same(constants["Brush.PrimaryTransparent"], border.Background);
            Assert.Same(constants["Brush.Primary"], border.BorderBrush);
            Assert.Same(constants["Brush.TextPrimary"], item.Foreground);
        }
    });

    private sealed class Host : IDisposable {
        public ComboBox Combo { get; }
        public TextBox Box => (TextBox)Combo.Template.FindName("PART_SearchBox", Combo);
        public ListBox Results => (ListBox)Combo.Template.FindName("PART_SearchResults", Combo);
        public Window Window { get; }
        public Button Before { get; } = new() { Content = "Before" };
        public Button After { get; } = new() { Content = "After" };

        public Host(IEnumerable items) {
            var document = ProgressPresentationTests.Load("Inputs");
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            document.Root!.Elements(wpf + "Style").Single(e => (string?)e.Attribute(x + "Key") == "FormLabel").Remove();
            var assembly = typeof(SearchableComboBoxBehavior).Assembly.GetName().Name;
            var markup = document.ToString();
            foreach (var attribute in document.Root.Attributes().Where(a => a.IsNamespaceDeclaration &&
                         a.Value.StartsWith("clr-namespace:")).ToArray())
                markup = markup.Replace(attribute.Value, attribute.Value + ";assembly=" + assembly);
            var resources = (ResourceDictionary)XamlReader.Parse(markup);
            Combo = new ComboBox { ItemsSource = items, Resources = resources, Style = (Style)resources[typeof(ComboBox)], Width = 250 };
            Window = new Window {
                Content = new StackPanel { Children = { Before, Combo, After } },
                Width = 300, Height = 180, Left = -10000, ShowInTaskbar = false, ShowActivated = false
            };
            Window.Show();
            Window.UpdateLayout();
        }

        public void Dispose() => Window.Close();
    }

    private static KeyEventArgs Press(UIElement target, Key key) {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new StubPresentationSource(), 0, key) {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(args);
        return args;
    }

    private sealed class StubPresentationSource : PresentationSource {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    public sealed class SelectionModel {
        public string Selection { get; set; } = "";
    }

    private sealed record Choice(string Label);

    private static void RunSta(Action action) {
        Exception? failure = null;
        var thread = new Thread(() => {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
