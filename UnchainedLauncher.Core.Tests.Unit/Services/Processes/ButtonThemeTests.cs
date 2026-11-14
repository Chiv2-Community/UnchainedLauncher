using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class ButtonThemeTests(HomeLaunchUi ui) {
    [Fact]
    public void DisabledButtonsUseMutedThemeColorsAndRestoreTheirActiveStyle() => ui.Run(() => {
        var resources = new ResourceDictionary {
            Source = new Uri("/UnchainedLauncher;component/Resources/Themes/Buttons.xaml", UriKind.Relative)
        };
        var previousTheme = ThemeService.Current;
        var renderMode = RenderOptions.ProcessRenderMode;
        Window? window = null;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        try {
            ThemeService.Apply(ThemeVariant.Dark);
            Application.Current.Resources.MergedDictionaries.Add(resources);
            var panel = new StackPanel();
            foreach (var style in new[] { "Button.Base", "Button.Primary", "Button.Secondary", "Button.Success", "Button.Danger", "Button.Light" })
                panel.Children.Add(new Button { Content = "Scan local mods", Style = (Style)resources[style] });
            // Local colors must not defeat the disabled template's neutral appearance.
            panel.Children.Add(new Button { Content = "Custom colors", Background = Brushes.Red, Foreground = Brushes.White });
            window = new Window { Content = panel, Width = 400, Height = 400, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            void Flush() => window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Flush();
            foreach (var button in panel.Children.OfType<Button>()) {
                var surface = Assert.IsType<Border>(button.Template.FindName("border", button));
                var content = Assert.IsType<ContentPresenter>(button.Template.FindName("content", button));
                var activeBackground = ColorOf(surface.Background);
                var activeForeground = ColorOf(TextElement.GetForeground(content));
                button.IsEnabled = false;
                foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light }) {
                    ThemeService.Apply(theme);
                    Flush();
                    Assert.Equal(ColorOf(button.FindResource("Brush.Light")), ColorOf(surface.Background));
                    Assert.Equal(ColorOf(button.FindResource("Brush.Muted")), ColorOf(TextElement.GetForeground(content)));
                    Assert.Equal(ColorOf(button.FindResource("Brush.InputBorder")), ColorOf(surface.BorderBrush));
                    Assert.Equal(1, surface.Opacity);
                    Assert.Equal(Cursors.Arrow, button.Cursor);
                }
                ThemeService.Apply(ThemeVariant.Dark);
                button.IsEnabled = true;
                Flush();
                Assert.Equal(activeBackground, ColorOf(surface.Background));
                Assert.Equal(activeForeground, ColorOf(TextElement.GetForeground(content)));
                Assert.Equal(Cursors.Hand, button.Cursor);
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
