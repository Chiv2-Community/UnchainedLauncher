using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class ProgressBarThemeTests(HomeLaunchUi ui) {
    [Fact]
    public void ProgressTracksRangeAndThemeWithoutNativeChrome() => ui.Run(() => {
        var resources = new ResourceDictionary {
            Source = new Uri("/UnchainedLauncher;component/Resources/Themes/Misc.xaml", UriKind.Relative)
        };
        var previousTheme = ThemeService.Current;
        var renderMode = RenderOptions.ProcessRenderMode;
        Window? window = null;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        try {
            ThemeService.Apply(ThemeVariant.Dark);
            Application.Current.Resources.MergedDictionaries.Add(resources);
            var progress = new ProgressBar { Minimum = 10, Maximum = 110, Value = 10 };
            window = new Window { Content = progress, Width = 300, Height = 100, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            void Flush() => window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Flush();
            var track = Assert.IsType<Grid>(progress.Template.FindName("PART_Track", progress));
            var indicator = Assert.IsType<Border>(progress.Template.FindName("PART_Indicator", progress));
            var surface = Assert.IsType<Border>(progress.Template.FindName("Surface", progress));
            var range = Assert.IsAssignableFrom<IRangeValueProvider>(new ProgressBarAutomationPeer(progress).GetPattern(PatternInterface.RangeValue));
            var darkAccent = ColorOf(indicator.Background);
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light }) {
                ThemeService.Apply(theme);
                Flush();
                Assert.Equal(ColorOf(progress.FindResource("Brush.Primary")), ColorOf(indicator.Background));
                Assert.Equal(ColorOf(progress.FindResource("Brush.InputBackground")), ColorOf(surface.Background));
                if (theme == ThemeVariant.Light) Assert.NotEqual(darkAccent, ColorOf(indicator.Background));
                foreach (var value in new[] { 10d, 60d, 110d }) {
                    progress.Value = value;
                    Flush();
                    Assert.Equal(track.ActualWidth * (value - 10) / 100, indicator.ActualWidth, 3);
                    Assert.Equal(value, range.Value);
                }
            }
            progress.IsIndeterminate = true;
            Flush();
            Assert.Equal(track.ActualWidth, indicator.ActualWidth, 3);
            Assert.True(indicator.HasAnimatedProperties);
            progress.IsIndeterminate = false;
            progress.Value = 60;
            Flush();
            Assert.False(indicator.HasAnimatedProperties);
            Assert.Equal(1, indicator.Opacity);
            Assert.Equal(track.ActualWidth / 2, indicator.ActualWidth, 3);
            progress.IsEnabled = false;
            Flush();
            Assert.Equal(0.5, surface.Opacity);
            foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal }) {
                progress.Orientation = orientation;
                progress.Width = orientation == Orientation.Vertical ? 16 : 160;
                progress.Height = orientation == Orientation.Vertical ? 60 : 12;
                foreach (var value in new[] { 10d, 60d, 110d }) {
                    progress.Value = value;
                    Flush();
                    AssertFill((value - 10) / 100);
                }
                progress.IsIndeterminate = true;
                Flush();
                AssertFill(1);
                Assert.True(indicator.HasAnimatedProperties);
                progress.IsIndeterminate = false;
                progress.Value = 60;
                Flush();
                AssertFill(0.5);
                Assert.False(indicator.HasAnimatedProperties);

                void AssertFill(double fraction) {
                    var trackBounds = track.TransformToAncestor(surface).TransformBounds(new Rect(track.RenderSize));
                    var fillBounds = indicator.TransformToAncestor(surface).TransformBounds(new Rect(indicator.RenderSize));
                    if (orientation == Orientation.Vertical) {
                        Assert.Equal(trackBounds.Height * fraction, fillBounds.Height, 3);
                        Assert.Equal(trackBounds.Bottom, fillBounds.Bottom, 3);
                        Assert.Equal(trackBounds.Width, fillBounds.Width, 3);
                    }
                    else {
                        Assert.Equal(trackBounds.Width * fraction, fillBounds.Width, 3);
                        Assert.Equal(trackBounds.Left, fillBounds.Left, 3);
                        Assert.Equal(trackBounds.Height, fillBounds.Height, 3);
                    }
                }
            }
        }
        finally {
            window?.Close();
            Application.Current.Resources.MergedDictionaries.Remove(resources);
            ThemeService.Apply(previousTheme);
            RenderOptions.ProcessRenderMode = renderMode;
        }
    });

    private static Color ColorOf(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;
}
