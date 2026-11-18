using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class ProgressVisualTests {
    [Fact]
    public void FillTracksOrientationRangeIndeterminateAndTheme() {
        Exception? failure = null;
        var thread = new Thread(() => {
            try { VerifyFill(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyFill() {
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var element = new XElement(ProgressPresentationTests.Load("Misc").Descendants(wpf + "Style")
            .Single(e => (string?)e.Attribute("TargetType") == "ProgressBar"));
        element.Add(new XAttribute(XNamespace.Xmlns + "x", x));
        var progress = new ProgressBar {
            Minimum = 10, Maximum = 110,
            Style = (Style)XamlReader.Parse(element.ToString())
        };
        var window = new Window { Content = progress, Width = 300, Height = 150, ShowInTaskbar = false, ShowActivated = false };
        try {
            window.Show();
            foreach (var accent in new[] { Brushes.Blue, Brushes.Orange }) {
                progress.Resources["Brush.Primary"] = accent;
                progress.Resources["Brush.InputBackground"] = Brushes.Gray;
                progress.Resources["Brush.InputBorder"] = Brushes.Black;
                foreach (var orientation in new[] { Orientation.Horizontal, Orientation.Vertical }) {
                    progress.Orientation = orientation;
                    progress.Width = orientation == Orientation.Vertical ? 16 : 160;
                    progress.Height = orientation == Orientation.Vertical ? 60 : 12;
                    foreach (var value in new[] { 10d, 60d, 110d }) {
                        progress.Value = value;
                        Verify((value - 10) / 100);
                    }
                    progress.IsIndeterminate = true;
                    Verify(1);
                    var indicator = (Border)progress.Template.FindName("PART_Indicator", progress);
                    Assert.True(indicator.HasAnimatedProperties);
                    progress.IsIndeterminate = false;
                    progress.Value = 60;
                    Verify(0.5);
                    Assert.False(indicator.HasAnimatedProperties);

                    void Verify(double fraction) {
                        window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                        var track = (Grid)progress.Template.FindName("PART_Track", progress);
                        var fill = (Border)progress.Template.FindName("PART_Indicator", progress);
                        var surface = (Border)progress.Template.FindName("Surface", progress);
                        Assert.Same(accent, fill.Background);
                        var trackBounds = track.TransformToAncestor(surface).TransformBounds(new Rect(track.RenderSize));
                        var fillBounds = fill.TransformToAncestor(surface).TransformBounds(new Rect(fill.RenderSize));
                        if (orientation == Orientation.Vertical) {
                            Assert.Equal(trackBounds.Height * fraction, fillBounds.Height, 3);
                            Assert.Equal(trackBounds.Bottom, fillBounds.Bottom, 3);
                        }
                        else {
                            Assert.Equal(trackBounds.Width * fraction, fillBounds.Width, 3);
                            Assert.Equal(trackBounds.Left, fillBounds.Left, 3);
                        }
                    }
                }
            }
        }
        finally { window.Close(); }
    }
}
