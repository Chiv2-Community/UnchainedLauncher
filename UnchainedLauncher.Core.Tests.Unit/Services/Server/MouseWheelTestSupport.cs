using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

internal static class MouseWheelTestSupport {
    internal static ScrollViewer CreateViewer(UIElement content) {
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

    internal static MouseWheelEventArgs Wheel(IInputElement source) {
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

    internal static void OnSta(Action action) {
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