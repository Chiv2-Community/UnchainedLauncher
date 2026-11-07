using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UnchainedLauncher.GUI.Behaviors;
using static UnchainedLauncher.Core.Tests.Unit.Services.Server.MouseWheelTestSupport;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

public class MouseWheelListTemplateTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapAndActorListsForwardRoutedWheelEvents(bool wrapMaps) => OnSta(() => {
        var label = new TextBlock { Text = wrapMaps ? "map" : "actor" };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new CheckBox());
        row.Children.Add(label);
        var list = new ListBox { Items = { row } };
        if (wrapMaps) {
            var panel = new FrameworkElementFactory(typeof(WrapPanel));
            list.ItemsPanel = new ItemsPanelTemplate(panel);
        }
        MouseWheelScrollForwardingBehavior.SetIsEnabled(list, true);
        var outer = CreateViewer(list);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(outer, true);
        var templateViewer = Descendants(list).OfType<ScrollViewer>().Single();
        Assert.Same(list, templateViewer.TemplatedParent);

        foreach (var source in new UIElement[] { label, row, templateViewer }) {
            outer.ScrollToVerticalOffset(0);
            outer.UpdateLayout();
            Assert.True(Wheel(source).Handled);
            outer.UpdateLayout();
            Assert.Equal(60d, outer.VerticalOffset);
            Assert.Equal(0d, templateViewer.VerticalOffset);
        }
    });

    [Theory]
    [InlineData("viewer")]
    [InlineData("combo")]
    [InlineData("list")]
    [InlineData("listView")]
    [InlineData("grid")]
    [InlineData("multiline")]
    public void RealNestedControlsKeepTheirWheelInput(string kind) => OnSta(() => {
        UIElement nested = kind switch {
            "viewer" => new ScrollViewer { Content = new TextBlock { Text = "nested" } },
            "combo" => new ComboBox(),
            "list" => new ListBox(),
            "listView" => new ListView(),
            "grid" => new DataGrid(),
            _ => new TextBox { AcceptsReturn = true }
        };
        var list = new ListBox { Items = { nested } };
        MouseWheelScrollForwardingBehavior.SetIsEnabled(list, true);
        var outer = CreateViewer(list);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(outer, true);
        var source = kind == "viewer" ? (UIElement)((ScrollViewer)nested).Content : nested;

        Assert.False(Wheel(source).Handled);
        outer.UpdateLayout();
        Assert.Equal(0d, outer.VerticalOffset);
    });

    [Fact]
    public void ViewerBehaviorTargetsItsAncestorNotItself() => OnSta(() => {
        var label = new TextBlock { Text = "inner viewer" };
        var inner = CreateViewer(label);
        inner.Height = 150;
        var outer = CreateViewer(inner);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(inner, true);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(outer, true);

        Assert.True(Wheel(label).Handled);
        outer.UpdateLayout();
        Assert.Equal(60d, outer.VerticalOffset);
        Assert.Equal(0d, inner.VerticalOffset);
    });

    [Fact]
    public void RootViewerDoesNotSwallowItsOwnWheelInput() => OnSta(() => {
        var label = new TextBlock { Text = "root viewer" };
        var outer = CreateViewer(label);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(outer, true);
        Assert.False(Wheel(label).Handled);
        Assert.Equal(0d, outer.VerticalOffset);
    });

    [Fact]
    public void DisabledBehaviorAndNonScrollableAncestorsDoNotHandleWheelInput() => OnSta(() => {
        var label = new TextBlock { Text = "map" };
        var list = new ListBox { Items = { label } };
        var outer = CreateViewer(list);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(list, true);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(list, false);
        Assert.False(Wheel(label).Handled);

        MouseWheelScrollForwardingBehavior.SetIsEnabled(list, true);
        ((StackPanel)outer.Content).Children.RemoveAt(1);
        outer.UpdateLayout();
        Assert.Equal(0d, outer.ScrollableHeight);
        Assert.False(Wheel(label).Handled);
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}