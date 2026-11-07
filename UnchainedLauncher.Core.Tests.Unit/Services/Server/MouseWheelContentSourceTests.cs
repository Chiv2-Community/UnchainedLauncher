using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using UnchainedLauncher.GUI.Behaviors;
using static UnchainedLauncher.Core.Tests.Unit.Services.Server.MouseWheelTestSupport;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

public class MouseWheelContentSourceTests {
    [Fact]
    public void InlineSourcesForwardRoutedWheelEventsToTheOuterViewer() => OnSta(() => {
        var run = new Run("actor");
        var link = new Hyperlink(run);
        var text = new TextBlock(link);
        var container = new Border { Child = text };
        var viewer = CreateViewer(container);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(container, true);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(viewer, true);

        foreach (var source in new IInputElement[] { run, link, text }) {
            viewer.ScrollToVerticalOffset(0);
            viewer.UpdateLayout();
            var args = Wheel(source);
            viewer.UpdateLayout();
            Assert.Same(source, args.OriginalSource);
            Assert.True(args.Handled);
            Assert.Equal(60d, viewer.VerticalOffset);
        }
    });

    [Fact]
    public void InlineTraversalStillRecognizesIndependentNestedViewers() => OnSta(() => {
        var run = new Run("nested actor");
        var nested = new ScrollViewer { Content = new TextBlock(new Span(run)) };
        var container = new Border { Child = nested };
        var viewer = CreateViewer(container);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(container, true);
        MouseWheelScrollForwardingBehavior.SetIsEnabled(viewer, true);

        Assert.False(Wheel(run).Handled);
        viewer.UpdateLayout();
        Assert.Equal(0d, viewer.VerticalOffset);
    });

    [Fact]
    public void DetachedContentAndNonvisualObjectsHaveSafeParentTraversal() => OnSta(() => {
        var method = typeof(MouseWheelScrollForwardingBehavior).GetMethod(
            "GetParent", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Null(method.Invoke(null, [new Run("detached")]));
        Assert.Null(method.Invoke(null, [new DependencyObject()]));
        var content = new FrameworkContentElement();
        var host = new ContentControl { Content = content };
        Assert.Null(ContentOperations.GetParent(content));
        Assert.Same(host, method.Invoke(null, [content]));
    });
}