using LanguageExt;
using Moq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class PakDependencyPreferenceTests(HomeLaunchUi ui) {
    private static Release Release(string name, params Dependency[] dependencies) => new("v1.0.0",
        Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(name))), name + ".pak", DateTime.UtcNow,
        new($"https://github.com/Org/{name}", name, "", null, null, [], [], dependencies.ToList()),
        new AssetCollections([], [], [new($"TBL/Content/Maps/{name}.umap", "hash", null, null, null, name, null, null, null, "FFA", null)], [], []), "");

    private static ModManager Manager(IPakDir paks, params Release[] releases) => new(Mock.Of<IModRegistry>(MockBehavior.Strict), paks, [],
        releases.Select(release => new Mod(release.Info, [release])));

    [Fact]
    public void RealCardBindingsKeepAutomaticSelectionImplicitAndRenderGold() => ui.Run(() => {
        var resources = new List<ResourceDictionary>();
        Window? window = null;
        var renderMode = RenderOptions.ProcessRenderMode;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        try {
            foreach (var theme in new[] { "Constants", "DarkConstants", "Buttons", "Misc", "Inputs", "Typography" }) {
                var dictionary = new ResourceDictionary { Source = new Uri($"/UnchainedLauncher;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
                resources.Add(dictionary);
                Application.Current.Resources.MergedDictionaries.Add(dictionary);
            }
            var converters = new ResourceDictionary { ["ModSortModeToStringConverter"] = new UnchainedLauncher.GUI.Converters.ModSortModeToStringConverter() };
            resources.Add(converters);
            Application.Current.Resources.MergedDictionaries.Add(converters);
            var library = Release("Library");
            var parent = Release("Parent", new Dependency(library.Info.RepoUrl, library.Tag));
            var manager = Manager(Mock.Of<IPakDir>(), parent, library);
            var settings = new SettingsCodec(null!, null!, null!, "").Deserialize("{\"installation_type\":0}").Result!;
            var list = new ModListVM(manager, Mock.Of<IUserDialogueSpawner>(), settings);
            var card = list.DisplayMods.Single(mod => mod.Mod.LatestReleaseInfo.Name == "Library");
            var view = new UnchainedLauncher.GUI.Views.Mods.ModList { DataContext = list, FontFamily = SystemFonts.MessageFontFamily };
            view.Resources["Font.Default"] = SystemFonts.MessageFontFamily;
            window = new Window { Content = view, Width = 850, Height = 500, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            void Flush() => window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Flush();
            UnchainedLauncher.GUI.Views.Mods.Mod LibraryView() => Descendants<UnchainedLauncher.GUI.Views.Mods.Mod>(view).Single(control => ReferenceEquals(control.DataContext, card));
            var originalCard = LibraryView();
            var cardStyle = originalCard.FindResource("Card");
            var modCardStyle = originalCard.FindResource("ModCard");
            void AssertIsolatedShadows() {
                var borders = Descendants<Border>(originalCard).Where(border => ReferenceEquals(border.Style, cardStyle)
                    || ReferenceEquals(border.Style, modCardStyle)).ToList();
                Assert.NotEmpty(borders);
                foreach (var border in borders) {
                    var layers = Assert.IsType<Grid>(VisualTreeHelper.GetParent(border));
                    Assert.Equal(2, layers.Children.Count);
                    var shadow = Assert.IsType<Border>(layers.Children[0]);
                    var foreground = Assert.IsType<Border>(layers.Children[1]);
                    Assert.Null(shadow.Child);
                    Assert.False(shadow.IsHitTestVisible);
                    Assert.IsType<DropShadowEffect>(shadow.Effect);
                    Assert.Null(foreground.Effect);
                    Assert.Null(foreground.Background);
                    Assert.Null(foreground.BorderBrush);
                    Assert.NotNull(foreground.Child);
                    Assert.Equal(shadow.Margin, foreground.Margin);
                    Assert.Equal(shadow.Padding, foreground.Padding);
                    Assert.Equal(shadow.BorderThickness, foreground.BorderThickness);
                    if (foreground.IsVisible && foreground.ActualWidth > 0) {
                        Assert.Equal(shadow.RenderSize, foreground.RenderSize);
                        Assert.Equal(shadow.TranslatePoint(new Point(), layers), foreground.TranslatePoint(new Point(), layers));
                        var contentOrigin = foreground.Child.TranslatePoint(new Point(), foreground);
                        Assert.Equal(foreground.Padding.Left + foreground.BorderThickness.Left, contentOrigin.X, 1);
                        Assert.Equal(foreground.Padding.Top + foreground.BorderThickness.Top, contentOrigin.Y, 1);
                    }
                }
            }
            AssertIsolatedShadows();
            var contents = Descendants<UnchainedLauncher.GUI.Views.Controls.CollapsibleCard>(originalCard).Single(control => Equals(control.Header, "Contents"));
            var header = Descendants<ToggleButton>(contents).First();
            Assert.False(contents.IsExpanded);
            header.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Flush();
            Assert.True(contents.IsExpanded);
            var maps = Descendants<UnchainedLauncher.GUI.Views.Controls.CollapsibleCard>(contents).Single(control => Equals(control.Header, "Maps"));
            Descendants<ToggleButton>(maps).First().SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Flush();
            Assert.True(maps.IsExpanded);
            Assert.Contains(Descendants<TextBlock>(maps), text => text.Text == library.Manifest.Maps.Single().Path);
            AssertIsolatedShadows();
            header.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            Flush();
            Assert.False(contents.IsExpanded);
            var automatic = Descendants<CheckBox>(view).Single(control => Equals(control.Content, "Automatically download pak dependencies"));
            var keep = Descendants<CheckBox>(view).Single(control => Equals(control.Content, "Always keep unmanaged paks"));
            var divider = Assert.IsType<Border>(view.FindName("OptionsDivider"));
            Assert.Equal(1, divider.ActualHeight);
            Assert.Equal(view.ActualWidth - 20, divider.ActualWidth, 1);
            Assert.True(divider.TranslatePoint(new Point(), view).Y + divider.ActualHeight < automatic.TranslatePoint(new Point(), view).Y);
            Assert.Equal(automatic.TranslatePoint(new Point(), view).Y, keep.TranslatePoint(new Point(), view).Y, 1);
            Assert.True(keep.TranslatePoint(new Point(), view).X >= automatic.TranslatePoint(new Point(), view).X + automatic.ActualWidth);
            var resets = 0;
            list.DisplayMods.CollectionChanged += (_, args) => {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
            };
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(parent));
            Flush();
            Assert.True(card.IsDependency);
            Assert.Single(manager.EnabledModReleaseCoordinates);
            Assert.Equal(library, Assert.Single(Descendants<ComboBox>(LibraryView())).SelectedItem);
            var button = Descendants<Button>(LibraryView()).Single(control => Equals(control.Content, "Dependency"));
            Assert.Equal(Color.FromRgb(0xD0, 0x91, 0x28), Assert.IsType<SolidColorBrush>(button.Background).Color);
            Assert.Contains(Descendants<Border>(view), border => border.Background is SolidColorBrush brush
                && brush.Color == Color.FromRgb(0xD0, 0x91, 0x28) && brush.Opacity == 0.05);
            Assert.Single(Descendants<Border>(originalCard).Where(border => border.Background is SolidColorBrush brush
                && brush.Color == Color.FromRgb(0xD0, 0x91, 0x28) && brush.Opacity == 0.05));
            AssertIsolatedShadows();
            window.Activate();
            Keyboard.Focus(button);
            Flush();
            Assert.True(originalCard.IsKeyboardFocusWithin);
            var modLayers = Assert.IsType<Grid>(originalCard.Content);
            Assert.Equal(8, Assert.IsType<DropShadowEffect>(Assert.IsType<Border>(modLayers.Children[0]).Effect).BlurRadius);
            AssertIsolatedShadows();
            button.Command.Execute(null);
            Flush();
            Assert.Equal("Disable", button.Content);
            Assert.Single(Descendants<Border>(originalCard).Where(border => border.Background is SolidColorBrush brush
                && brush.Color == (Color)originalCard.FindResource("Color.Success") && brush.Opacity == 0.05));
            AssertIsolatedShadows();
            button.Command.Execute(null);
            Flush();
            Assert.Equal("Dependency", button.Content);
            automatic.SetCurrentValue(CheckBox.IsCheckedProperty, false);
            Flush();
            Assert.False(settings.AutomaticallyDownloadPakDependencies);
            Assert.Equal("Enable", button.Content);
            Assert.Null(Assert.Single(Descendants<ComboBox>(LibraryView())).SelectedItem);
            automatic.SetCurrentValue(CheckBox.IsCheckedProperty, true);
            Flush();
            Assert.Equal("Dependency", button.Content);
            Assert.Same(originalCard, LibraryView());
            Assert.Equal(0, resets);
            Assert.Single(manager.EnabledModReleaseCoordinates);
        }
        finally {
            window?.Close();
            foreach (var dictionary in resources) Application.Current.Resources.MergedDictionaries.Remove(dictionary);
            RenderOptions.ProcessRenderMode = renderMode;
        }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    [Fact]
    public void DependencyCardsPromoteDemoteAndFollowPreferenceWithoutChangingExplicitRoots() => ui.Run(() => {
        var leaf = Release("Leaf");
        var library = Release("Library", new Dependency(leaf.Info.RepoUrl, leaf.Tag));
        var parent = Release("Parent", new Dependency(library.Info.RepoUrl, library.Tag));
        var disabled = Release("A Disabled");
        var manager = Manager(Mock.Of<IPakDir>(), parent, library, leaf, disabled);
        var settings = new SettingsCodec(null!, null!, null!, "").Deserialize("{\"installation_type\":0}").Result!;
        var list = new ModListVM(manager, Mock.Of<IUserDialogueSpawner>(), settings);
        var catalog = new AvailableModsAndMapsService(manager, new ModScanTabVM(), settings);
        Assert.Equal(ModSortMode.Alphabetical, list.SelectedSortMode);
        Assert.Equal(new[] { "A Disabled", "Leaf", "Library", "Parent" }, list.DisplayMods.Select(card => card.Mod.LatestReleaseInfo.Name));
        list.SelectedSortMode = ModSortMode.EnabledFirst;
        var parentCard = list.DisplayMods.Single(card => card.Mod.LatestReleaseInfo.Name == "Parent");
        var libraryCard = list.DisplayMods.Single(card => card.Mod.LatestReleaseInfo.Name == "Library");
        var leafCard = list.DisplayMods.Single(card => card.Mod.LatestReleaseInfo.Name == "Leaf");

        Assert.False(libraryCard.IsEnabled);
        parentCard.EnableOrDisableCommand.Execute(null);
        Assert.Equal("Disable", parentCard.ButtonText);
        Assert.Equal("Dependency", libraryCard.ButtonText);
        Assert.True(leafCard.IsDependency);
        Assert.Equal(new[] { "Parent", "Leaf", "Library", "A Disabled" }, list.DisplayMods.Select(card => card.Mod.LatestReleaseInfo.Name));
        Assert.Equal(library, libraryCard.SelectedRelease);
        Assert.Single(manager.EnabledModReleaseCoordinates);
        Assert.Contains(leaf.Manifest.Maps.Single(), catalog.AvailableMaps);

        libraryCard.EnableOrDisableCommand.Execute(null);
        Assert.False(libraryCard.IsDependency);
        Assert.Equal("Disable", libraryCard.ButtonText);
        Assert.Equal(2, manager.EnabledModReleaseCoordinates.Count());
        Assert.Equal(new[] { "Library", "Parent", "Leaf", "A Disabled" }, list.DisplayMods.Select(card => card.Mod.LatestReleaseInfo.Name));
        libraryCard.EnableOrDisableCommand.Execute(null);
        Assert.True(libraryCard.IsDependency);
        Assert.Single(manager.EnabledModReleaseCoordinates);

        settings.AutomaticallyDownloadPakDependencies = false;
        Assert.False(libraryCard.IsEnabled);
        Assert.False(leafCard.IsEnabled);
        Assert.True(parentCard.IsEnabled);
        Assert.DoesNotContain(leaf.Manifest.Maps.Single(), catalog.AvailableMaps);
        Assert.Equal([ReleaseCoordinates.FromRelease(parent)], manager.GetEnabledAndDependencies(false));

        libraryCard.EnableOrDisableCommand.Execute(null);
        Assert.Equal("Disable", libraryCard.ButtonText);
        settings.AutomaticallyDownloadPakDependencies = true;
        Assert.False(libraryCard.IsDependency);
        Assert.True(leafCard.IsDependency);
        parentCard.EnableOrDisableCommand.Execute(null);
        Assert.False(parentCard.IsEnabled);
        Assert.True(leafCard.IsDependency);
        libraryCard.EnableOrDisableCommand.Execute(null);
        Assert.False(leafCard.IsEnabled);
        Assert.Empty(manager.EnabledModReleaseCoordinates);
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public Task LaunchPreferenceControlsManagedDependenciesButPreservesUnknownDevelopmentPaks(bool automatic, bool libraryInstalled, bool libraryExplicit) => ui.RunAsync(async () => {
        var directory = Path.Combine("TestDirectories", nameof(PakDependencyPreferenceTests), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try {
            var library = Release("Library");
            var parent = Release("Parent", new Dependency(library.Info.RepoUrl, library.Tag));
            File.WriteAllText(Path.Combine(directory, "parent.pak"), "Parent");
            if (libraryInstalled) File.WriteAllText(Path.Combine(directory, "library.pak"), "Library");
            File.WriteAllText(Path.Combine(directory, "development.pak"), "my development library");
            var tracked = new List<ManagedPak> { new(ReleaseCoordinates.FromRelease(parent), "parent.pak", 1) };
            if (libraryInstalled) tracked.Add(new(ReleaseCoordinates.FromRelease(library), "library.pak", 0));
            var paks = new Core.Services.Mods.PakDir(directory, tracked);
            var manager = Manager(paks, parent, library);
            manager.EnableModRelease(ReleaseCoordinates.FromRelease(parent));
            if (libraryExplicit) manager.EnableModRelease(ReleaseCoordinates.FromRelease(library));
            var settings = new SettingsCodec(null!, null!, null!, "").Deserialize("{\"installation_type\":0}").Result!;
            settings.AlwaysKeepUnmanagedPaks = true;
            settings.AutomaticallyDownloadPakDependencies = automatic;
            var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
            dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => { });
            var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);
            Assert.True((await new Chivalry2ModsInstaller(manager, dialogue.Object, settings).PrepareLaunch(options)).IsSome);
            Assert.Equal(automatic || libraryExplicit ? 2 : 1, paks.ManagedPaks.Count());
            Assert.Equal(automatic || libraryExplicit, paks.ManagedPaks.Any(pak => pak.Coordinates == ReleaseCoordinates.FromRelease(library)));
            Assert.Equal("my development library", File.ReadAllText(Path.Combine(directory, "development.pak")));
            Assert.Equal(libraryExplicit ? 2 : 1, manager.EnabledModReleaseCoordinates.Count());
        }
        finally {
            Directory.Delete(directory, true);
        }
    });
}