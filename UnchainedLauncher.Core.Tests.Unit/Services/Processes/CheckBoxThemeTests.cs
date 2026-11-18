using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class CheckBoxThemeTests(HomeLaunchUi ui) {
    [Fact]
    public void CheckboxStatesStayVisibleAndUpdateWithTheTheme() => ui.Run(() => {
        var resources = new List<ResourceDictionary>();
        Window? window = null;
        var previousTheme = ThemeService.Current;
        var renderMode = RenderOptions.ProcessRenderMode;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        try {
            ThemeService.Apply(ThemeVariant.Dark);
            foreach (var theme in new[] { "Constants", "Buttons", "Misc", "Inputs", "Typography" }) {
                var dictionary = new ResourceDictionary { Source = new Uri($"/UnchainedLauncher;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
                resources.Add(dictionary);
                Application.Current.Resources.MergedDictionaries.Add(dictionary);
            }
            var check = new CheckBox { Content = "_Option", IsThreeState = true, IsChecked = false, FontFamily = SystemFonts.MessageFontFamily };
            window = new Window { Content = check, Width = 300, Height = 100, ShowActivated = false, ShowInTaskbar = false };
            window.Resources["Font.Default"] = SystemFonts.MessageFontFamily;
            window.Show();
            void Flush() => window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Flush();
            var surface = Assert.IsType<Border>(check.Template.FindName("CheckSurface", check));
            var mark = Assert.IsType<System.Windows.Shapes.Path>(check.Template.FindName("CheckMark", check));
            var mixed = Assert.IsType<Rectangle>(check.Template.FindName("MixedMark", check));
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle));
            var darkPrimary = ColorOf(check.FindResource("Brush.Primary"));
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light }) {
                ThemeService.Apply(theme);
                Flush();
                if (theme == ThemeVariant.Light) Assert.NotEqual(darkPrimary, ColorOf(check.FindResource("Brush.Primary")));
                Assert.False(check.IsChecked);
                Assert.Equal(ColorOf(check.FindResource("Brush.TextPrimary")), ColorOf(check.Foreground));
                Assert.True(Contrast(ColorOf(surface.BorderBrush), ColorOf(surface.Background)) >= 3);
                Assert.Equal(Visibility.Collapsed, mark.Visibility);
                Assert.Equal(Visibility.Collapsed, mixed.Visibility);
                toggle.Toggle();
                Flush();
                Assert.True(check.IsChecked);
                Assert.Equal(Visibility.Visible, mark.Visibility);
                Assert.Equal(ColorOf(check.FindResource("Brush.Primary")), ColorOf(surface.Background));
                Assert.True(Contrast(ColorOf(mark.Stroke), ColorOf(surface.Background)) >= 3);
                toggle.Toggle();
                Flush();
                Assert.Null(check.IsChecked);
                Assert.Equal(Visibility.Collapsed, mark.Visibility);
                Assert.Equal(Visibility.Visible, mixed.Visibility);
                check.IsEnabled = false;
                Flush();
                Assert.Equal(0.6, check.Opacity);
                check.IsEnabled = true;
                toggle.Toggle();
                Flush();
                Assert.False(check.IsChecked);
            }
        }
        finally {
            window?.Close();
            foreach (var dictionary in resources) Application.Current.Resources.MergedDictionaries.Remove(dictionary);
            ThemeService.Apply(previousTheme);
            RenderOptions.ProcessRenderMode = renderMode;
        }
    });

    private static Color ColorOf(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static double Contrast(Color first, Color second) {
        static double Channel(byte value) => value / 255.0 <= 0.04045 ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}