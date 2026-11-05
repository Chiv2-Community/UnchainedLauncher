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
            var cardSurface = Assert.IsType<Border>(card.Content);
            var surfaceGrid = Assert.IsType<Grid>(cardSurface.Child);
            var grid = Assert.Single(surfaceGrid.Children.OfType<Grid>());
            var toggle = Assert.Single(grid.Children.OfType<ToggleButton>());
            var body = Assert.Single(grid.Children.OfType<ContentPresenter>());
            var separator = Assert.Single(grid.Children.OfType<Border>().Where(border => Grid.GetRow(border) == 1));
            var indicator = Assert.Single(Assert.IsType<Grid>(toggle.Content).Children.OfType<System.Windows.Shapes.Path>());
            var surface = Assert.IsType<Border>(toggle.Template.FindName("HeaderSurface", toggle));
            var hoverOutline = Assert.IsType<Border>(card.FindName("HoverOutline"));
            var defaults = new UnchainedLauncher.GUI.Views.Controls.CollapsibleCard();
            Assert.True(defaults.ShowExpansionIndicator);
            Assert.False(defaults.HighlightOnHover);
            Assert.False(defaults.ShowHeaderSeparator);
            var defaultGrid = Assert.Single(Assert.IsType<Grid>(Assert.IsType<Border>(defaults.Content).Child).Children.OfType<Grid>());
            Assert.Equal(Cursors.Hand, Assert.Single(defaultGrid.Children.OfType<ToggleButton>()).Cursor);
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
                Assert.Same(view.FindResource("Brush.PrimaryHover"), hoverOutline.BorderBrush);
                Assert.Equal(Visibility.Visible, hoverOutline.Visibility);
                Assert.Same(normalBackground, surface.Background);
                Assert.Equal(Cursors.Hand, toggle.Cursor);
                card.HighlightOnHover = false;
                FlushBindings();
                Assert.Same(normalBackground, surface.Background);
                Assert.Equal(Visibility.Collapsed, hoverOutline.Visibility);
                card.HighlightOnHover = true;
                vm.IsExpanded = true;
                FlushBindings();
                Assert.Same(normalBackground, surface.Background);
                Assert.Equal(Visibility.Visible, hoverOutline.Visibility);
                var description = Assert.Single(details.Children.OfType<TextBox>());
                Assert.False(UnchainedLauncher.GUI.Views.Controls.InputOptions.GetIsHoverHighlightEnabled(description));
                var descriptionBorder = Assert.IsType<Border>(description.Template.FindName("OuterBorder", description));
                var descriptionBrush = descriptionBorder.BorderBrush;
                hover.Invoke(Mouse.PrimaryDevice, new object?[] { description, 0 });
                FlushBindings();
                window.UpdateLayout();
                hover.Invoke(Mouse.PrimaryDevice, new object?[] { description, 0 });
                FlushBindings();
                Assert.True(description.IsMouseOver);
                Assert.Equal(Visibility.Visible, hoverOutline.Visibility);
                Assert.Equal(grid.ActualWidth, hoverOutline.ActualWidth, 3);
                Assert.Equal(grid.ActualHeight, hoverOutline.ActualHeight, 3);
                Assert.Same(descriptionBrush, descriptionBorder.BorderBrush);
                UnchainedLauncher.GUI.Views.Controls.InputOptions.SetIsHoverHighlightEnabled(description, true);
                FlushBindings();
                hover.Invoke(Mouse.PrimaryDevice, new object?[] { description, 0 });
                Assert.True(description.IsMouseOver);
                Assert.True(description.IsEnabled);
                Assert.True(UnchainedLauncher.GUI.Views.Controls.InputOptions.GetIsHoverHighlightEnabled(description));
                Assert.Same(view.FindResource("Brush.PrimaryHover"), descriptionBorder.BorderBrush);
                UnchainedLauncher.GUI.Views.Controls.InputOptions.SetIsHoverHighlightEnabled(description, false);
                FlushBindings();
                hover.Invoke(Mouse.PrimaryDevice, new object?[] { description, 0 });
                Assert.Same(descriptionBrush, descriptionBorder.BorderBrush);
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