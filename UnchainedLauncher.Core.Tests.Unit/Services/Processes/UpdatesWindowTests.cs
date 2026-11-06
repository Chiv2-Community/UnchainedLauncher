using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class UpdatesWindowTests(HomeLaunchUi ui) {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlwaysKeepPreferenceRoundTripsAndOldSettingsDefaultToFalse(bool enabled) => ui.Run(() => {
        var codec = new SettingsCodec(null!, null!, null!, "");
        var settings = codec.Deserialize("{\"installation_type\":0}").Result!;
        Assert.False(settings.AlwaysKeepUnmanagedPaks);
        Assert.True(settings.AutomaticallyDownloadPakDependencies);
        settings.AlwaysKeepUnmanagedPaks = enabled;
        settings.AutomaticallyDownloadPakDependencies = enabled;
        var json = codec.Serialize(settings);
        Assert.Contains("\"always_keep_unmanaged_paks\": " + enabled.ToString().ToLowerInvariant(), json);
        Assert.Equal(enabled, codec.Deserialize(json).Result!.AlwaysKeepUnmanagedPaks);
        Assert.Equal(enabled, codec.Deserialize(json).Result!.AutomaticallyDownloadPakDependencies);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    public void ResizedDialogScrollsRowsKeepsActionsVisibleAndHidesUnknownReleaseLinks(int cardCount) => ui.Run(() => {
        var resources = new List<ResourceDictionary>();
        UpdatesWindow? window = null;
        var renderMode = RenderOptions.ProcessRenderMode;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        try {
            foreach (var theme in new[] { "Constants", "DarkConstants", "Buttons", "Misc", "Inputs", "Typography" }) {
                var dictionary = new ResourceDictionary { Source = new Uri($"/UnchainedLauncher;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
                resources.Add(dictionary);
                Application.Current.Resources.MergedDictionaries.Add(dictionary);
            }
            var unknown = new DependencyUpdate("a-long-unmanaged-pak-name.pak", null, "Unknown", "", "Unrecognized pak");
            var known = new DependencyUpdate("known.pak", null, "1.2.3", "https://example.test/release", "Example mod");
            window = new UpdatesWindow("Unmanaged paks detected", "Unmanaged paks detected. Keep them or remove them?",
                "Keep", "Remove", "Cancel", Enumerable.Range(0, 8).Select(i => new DependencyUpdateViewModel(i == 0 ? unknown : known))) {
                Width = 400,
                Height = 280,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            window.Resources["Font.Default"] = SystemFonts.MessageFontFamily;
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);
            Assert.Equal(SizeToContent.Manual, window.SizeToContent);
            Assert.True(Assert.Single(Descendants<ScrollViewer>(window)).ScrollableHeight > 0);
            var rows = Descendants<TextBlock>(window).Where(text => text.Inlines.OfType<Hyperlink>().Any()).ToArray();
            Assert.Equal(Visibility.Collapsed, rows[0].Visibility);
            Assert.All(rows.Skip(1), row => Assert.Equal(Visibility.Visible, row.Visibility));
            var actions = Descendants<Button>(window).Where(button => new[] { "Keep", "Remove", "Cancel" }.Contains(button.Content as string)).ToArray();
            Assert.Equal(3, actions.Length);
            foreach (var action in actions) {
                Assert.True(action.IsVisible);
                var position = action.TranslatePoint(new Point(), window);
                Assert.InRange(position.X + action.ActualWidth, 0, window.ActualWidth);
                Assert.InRange(position.Y + action.ActualHeight, 0, window.ActualHeight);
            }
            window.Close();
            window = new UpdatesWindow("Unmanaged paks detected", "Unmanaged paks detected. Keep them or remove them?",
                "Keep", "Remove", "Cancel", Enumerable.Range(0, cardCount).Select(_ => new DependencyUpdateViewModel(known))) {
                ShowActivated = false,
                ShowInTaskbar = false
            };
            window.Resources["Font.Default"] = SystemFonts.MessageFontFamily;
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var viewport = Assert.Single(Descendants<ScrollViewer>(window));
            Assert.Equal(640, window.Width);
            var expectedHeight = Math.Min(cardCount, 3.5) * 112 + (cardCount < 4 ? 1 : 0);
            Assert.InRange(viewport.ViewportHeight, expectedHeight - 1, expectedHeight + 1);
            Assert.Equal(cardCount >= 4, viewport.ScrollableHeight > 0);
        }
        finally {
            window?.Close();
            RenderOptions.ProcessRenderMode = renderMode;
            foreach (var resource in resources) Application.Current.Resources.MergedDictionaries.Remove(resource);
        }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}