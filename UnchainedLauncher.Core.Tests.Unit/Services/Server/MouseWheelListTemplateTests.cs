using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UnchainedLauncher.GUI.Behaviors;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

public class MouseWheelListTemplateTests {
    [Fact]
    public void ListTemplateViewerDoesNotBlockForwardingButNestedControlsDo() {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var item = new TextBlock { Text = "map" };
                var list = new ListBox { Items = { item } };
                list.Measure(new Size(400, 300));
                list.Arrange(new Rect(0, 0, 400, 300));
                list.UpdateLayout();
                var method = typeof(MouseWheelScrollForwardingBehavior).GetMethod(
                    "IsOverNestedScrollableControl", BindingFlags.NonPublic | BindingFlags.Static)!;
                Assert.False((bool)method.Invoke(null, [item, list])!);
                var nested = new ComboBox();
                list.Items.Add(nested);
                Assert.True((bool)method.Invoke(null, [nested, list])!);
                var independent = new ScrollViewer();
                list.Items.Add(independent);
                Assert.True((bool)method.Invoke(null, [independent, list])!);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }
}
