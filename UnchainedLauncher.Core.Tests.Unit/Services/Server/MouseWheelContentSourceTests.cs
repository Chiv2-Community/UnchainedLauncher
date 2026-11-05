using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using UnchainedLauncher.GUI.Behaviors;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

public class MouseWheelContentSourceTests {
    [Fact]
    public void InlineSourcesCanBeWalkedWithoutTreatingThemAsVisuals() {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var run = new Run("actor");
                var link = new Hyperlink(run);
                var text = new TextBlock(link);
                var container = new Border { Child = text };
                var method = typeof(MouseWheelScrollForwardingBehavior).GetMethod(
                    "IsOverNestedScrollableControl", BindingFlags.NonPublic | BindingFlags.Static)!;
                foreach (var source in new DependencyObject[] { run, link, text })
                    Assert.False((bool)method.Invoke(null, [source, container])!);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }
}
