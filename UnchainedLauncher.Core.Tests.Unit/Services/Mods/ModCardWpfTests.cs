using Moq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Tests.Unit.Services.Processes;
using UnchainedLauncher.GUI.ViewModels;
using ModView = UnchainedLauncher.GUI.Views.Mods.Mod;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

[Collection("Home launch UI")]
public class ModCardWpfTests(HomeLaunchUi ui) {
    [Fact]
    public void SharedCardToggleUpdatesBoundVisibilityWhileCollapsedHeaderRemainsAccessible() => ui.Run(() => {
        var resources = new List<ResourceDictionary>();
        Window? window = null;
        try {
            foreach (var theme in new[] { "Constants", "DarkConstants", "Buttons", "Misc", "Inputs", "Typography" }) {
                var dictionary = new ResourceDictionary {
                    Source = new Uri($"/UnchainedLauncher;component/Resources/Themes/{theme}.xaml", UriKind.Relative)
                };
                resources.Add(dictionary);
                Application.Current.Resources.MergedDictionaries.Add(dictionary);
            }
            var info = new ModInfo("https://github.com/test/Example", "Example", "Example description", null, null, [], []);
            var release = new Release("v1.0.0", "hash", "mod.pak", DateTime.UtcNow, info, new([], [], [], [], []), null);
            var mod = new Mod(info, [release]);
            var manager = new Mock<IModManager>();
            manager.SetupGet(m => m.Mods).Returns([mod]);
            var vm = new ModVM(mod, LanguageExt.Prelude.None, manager.Object);
            // Use the platform's UI font so the regression also runs without Segoe UI under Wine.
            var view = new ModView { DataContext = vm, FontFamily = SystemFonts.MessageFontFamily };
            view.Resources["Font.Default"] = SystemFonts.MessageFontFamily;
            window = new Window { Content = view, Width = 1100, Height = 600, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            FlushBindings();

            var card = Assert.IsType<UnchainedLauncher.GUI.Views.Controls.CollapsibleCard>(view.Content);
            var header = Assert.IsType<Grid>(card.Header);
            var details = Assert.IsType<StackPanel>(card.Body);
            var grid = Assert.IsType<Grid>(Assert.IsType<Border>(card.Content).Child);
            var toggle = Assert.Single(grid.Children.OfType<ToggleButton>());
            var body = Assert.Single(grid.Children.OfType<ContentPresenter>());
            var separator = Assert.Single(grid.Children.OfType<Border>());
            var indicator = Assert.Single(Assert.IsType<Grid>(toggle.Content).Children.OfType<System.Windows.Shapes.Path>());
            var surface = Assert.IsType<Border>(toggle.Template.FindName("HeaderSurface", toggle));
            var defaults = new UnchainedLauncher.GUI.Views.Controls.CollapsibleCard();
            Assert.True(defaults.ShowExpansionIndicator);
            Assert.False(defaults.HighlightWhenCollapsed);
            Assert.False(defaults.ShowHeaderSeparator);
            Assert.Equal(Visibility.Collapsed, indicator.Visibility);
            var enable = Assert.Single(header.Children.OfType<Button>().Where(button => button.Command == vm.EnableOrDisableCommand));
            var version = Assert.Single(header.Children.OfType<ComboBox>());
            var togglePeer = new ToggleButtonAutomationPeer(toggle);
            var provider = Assert.IsAssignableFrom<IToggleProvider>(togglePeer.GetPattern(PatternInterface.Toggle));

            Assert.True(vm.IsExpanded);
            Assert.True(toggle.IsChecked);
            Assert.True(card.IsExpanded);
            Assert.Equal(Visibility.Visible, body.Visibility);
            Assert.True(details.IsVisible);
            Assert.Equal(Visibility.Visible, separator.Visibility);
            Assert.Equal(grid.ActualWidth, separator.ActualWidth, 3);

            provider.Toggle();
            FlushBindings();

            Assert.False(vm.IsExpanded);
            Assert.False(toggle.IsChecked);
            Assert.False(card.IsExpanded);
            Assert.Equal(Visibility.Collapsed, body.Visibility);
            Assert.False(details.IsVisible);
            Assert.Equal(Visibility.Collapsed, separator.Visibility);
            var normalBackground = surface.Background;
            var hover = typeof(MouseDevice).GetMethod("ChangeMouseOver", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            try {
                hover.Invoke(Mouse.PrimaryDevice, new object?[] { toggle, 0 });
                FlushBindings();
                Assert.Same(view.FindResource("Brush.PrimaryTransparent"), surface.Background);
                Assert.Equal(Cursors.Hand, toggle.Cursor);
                card.HighlightWhenCollapsed = false;
                FlushBindings();
                Assert.Same(normalBackground, surface.Background);
                card.HighlightWhenCollapsed = true;
                vm.IsExpanded = true;
                FlushBindings();
                Assert.Same(normalBackground, surface.Background);
                vm.IsExpanded = false;
                FlushBindings();
            }
            finally {
                hover.Invoke(Mouse.PrimaryDevice, new object?[] { null, 0 });
                FlushBindings();
            }
            foreach (var control in new Control[] { enable, version, toggle }) {
                Assert.True(control.IsVisible);
                Assert.True(control.IsEnabled);
                Assert.True(control.Focusable);
                Assert.True(KeyboardNavigation.GetIsTabStop(control));
                var peer = Assert.IsAssignableFrom<FrameworkElementAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(control));
                Assert.True(peer.IsEnabled());
                Assert.False(peer.IsOffscreen());
            }
            Assert.Same(vm.EnableOrDisableCommand, enable.Command);
            Assert.Same(release, Assert.Single(version.Items.Cast<Release>()));

            // A source-side change must update the real view and retain the two-way binding.
            vm.IsExpanded = true;
            FlushBindings();
            Assert.True(toggle.IsChecked);
            Assert.True(card.IsExpanded);
            Assert.Equal(Visibility.Visible, body.Visibility);
            Assert.True(details.IsVisible);
            Assert.Equal(Visibility.Visible, separator.Visibility);
            card.ShowHeaderSeparator = false;
            card.ShowExpansionIndicator = true;
            FlushBindings();
            Assert.Equal(Visibility.Collapsed, separator.Visibility);
            Assert.Equal(Visibility.Visible, indicator.Visibility);
        }
        finally {
            window?.Close();
            foreach (var dictionary in resources)
                Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }
    });

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
