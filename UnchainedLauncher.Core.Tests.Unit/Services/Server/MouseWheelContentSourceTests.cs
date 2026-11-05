using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using UnchainedLauncher.GUI.Behaviors;

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
    });

    private static ScrollViewer CreateViewer(UIElement content) {
        var viewer = new ScrollViewer {
            Content = new StackPanel { Children = { content, new Border { Height = 1000 } } },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        viewer.Measure(new Size(400, 300));
        viewer.Arrange(new Rect(0, 0, 400, 300));
        viewer.UpdateLayout();
        Assert.True(viewer.ScrollableHeight > 60);
        return viewer;
    }

    private static MouseWheelEventArgs Wheel(IInputElement source) {
        // Set WPF's hover state without moving the desktop cursor; routing remains real.
        var hover = typeof(MouseDevice).GetMethod("ChangeMouseOver", BindingFlags.NonPublic | BindingFlags.Instance)!;
        hover.Invoke(Mouse.PrimaryDevice, [source, 0]);
        try {
            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) {
                RoutedEvent = Mouse.PreviewMouseWheelEvent
            };
            if (source is ContentElement content) content.RaiseEvent(args);
            else ((UIElement)source).RaiseEvent(args);
            return args;
        }
        finally {
            hover.Invoke(Mouse.PrimaryDevice, [null, 0]);
        }
    }

    private static void OnSta(Action action) {
        Exception? failure = null;
        var thread = new Thread(() => {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}