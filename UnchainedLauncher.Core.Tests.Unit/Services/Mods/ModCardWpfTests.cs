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
    public void DetailsToggleUpdatesBoundVisibilityAndLabelWhileCollapsedHeaderRemainsAccessible() => ui.Run(() => {
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

            var grid = Assert.IsType<Grid>(Assert.IsType<Border>(view.Content).Child);
            var header = Assert.Single(grid.Children.OfType<Grid>());
            var details = Assert.Single(grid.Children.OfType<StackPanel>());
            var toggle = Assert.Single(header.Children.OfType<ToggleButton>());
            var enable = Assert.Single(header.Children.OfType<Button>().Where(button => button.Command == vm.EnableOrDisableCommand));
            var version = Assert.Single(header.Children.OfType<ComboBox>());
            var togglePeer = new ToggleButtonAutomationPeer(toggle);
            var provider = Assert.IsAssignableFrom<IToggleProvider>(togglePeer.GetPattern(PatternInterface.Toggle));

            Assert.True(vm.IsExpanded);
            Assert.True(toggle.IsChecked);
            Assert.Equal("Hide details", toggle.Content);
            Assert.Equal(Visibility.Visible, details.Visibility);
            Assert.True(details.IsVisible);
            Assert.Equal("Details for Example", togglePeer.GetName());

            provider.Toggle();
            FlushBindings();

            Assert.False(vm.IsExpanded);
            Assert.False(toggle.IsChecked);
            Assert.Equal("Show details", toggle.Content);
            Assert.Equal(Visibility.Collapsed, details.Visibility);
            Assert.False(details.IsVisible);
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
            Assert.Equal("Hide details", toggle.Content);
            Assert.Equal(Visibility.Visible, details.Visibility);
            Assert.True(details.IsVisible);
        }
        finally {
            window?.Close();
            foreach (var dictionary in resources)
                Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }
    });

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}