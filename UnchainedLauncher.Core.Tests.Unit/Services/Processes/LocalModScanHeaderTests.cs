using Moq;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views.Controls;
using UnchainedLauncher.GUI.Views.Servers.Sections;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class LocalModScanHeaderTests(HomeLaunchUi ui) {
    [Fact]
    public void ClickingDisabledScanButtonDoesNotToggleExpandedOrCollapsedCard() => ui.Run(() => {
        var scanner = new ModScanTabVM();
        var local = new LocalModScanVM(Mock.Of<IModManager>(), scanner) { HasUnmanagedPaks = true };
        var view = new ServerModActorsSectionView { DataContext = new { LocalModScan = local } };
        var window = new Window { Content = view, Width = 1100, Height = 300, ShowInTaskbar = false };
        var renderMode = RenderOptions.ProcessRenderMode;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        try {
            window.Show();
            var button = Descendants<Button>(view).Single(button => Equals(button.Content, "Scan local mods"));
            var target = Assert.IsType<Border>(VisualTreeHelper.GetParent(button));
            var card = Assert.Single(Descendants<CollapsibleCard>(view));
            Assert.False(button.IsEnabled);
            foreach (var expanded in new[] { true, false }) {
                card.IsExpanded = expanded;
                window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                var point = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), window);
                Assert.Same(target, window.InputHitTest(point));
                foreach (var routedEvent in new[] { Mouse.PreviewMouseDownEvent, Mouse.PreviewMouseUpEvent }) {
                    var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) {
                        RoutedEvent = routedEvent
                    };
                    target.RaiseEvent(args);
                    Assert.True(args.Handled);
                    Assert.Equal(expanded, card.IsExpanded);
                }
                Assert.False(scanner.IsScanning);
            }
        }
        finally {
            window.Close();
            RenderOptions.ProcessRenderMode = renderMode;
        }
    });

    [Fact]
    public void ActorEntriesAreCheckboxesWithVersionAndSourceNotSelectableRows() => ui.Run(() => {
        var first = new BlueprintDto("Actor.uasset", "hash", "/Game/Actor.Actor_C", null, "Actor", "1.2.3",
            null, null, null, false, false, true, false, false, false, false);
        var second = first with { Path = "Other.uasset", ClassPath = "/Game/Other.Other_C", ModName = "Other" };
        var rows = new ObservableCollection<ServerModActorVM> {
            new(first, "Managed title"), new(second, "local.pak")
        };
        var view = new ServerModActorsSectionView {
            DataContext = new {
                AvailableServerModActors = rows,
                EnabledServerModList = new ObservableCollection<BlueprintDto> { first },
                LocalModScan = new LocalModScanVM(Mock.Of<IModManager>(), new ModScanTabVM())
            }
        };
        var renderMode = RenderOptions.ProcessRenderMode;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var window = new Window { Content = view, Width = 1000, Height = 300, ShowInTaskbar = false };
        try {
            window.Show();
            void Flush() => window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Flush();
            Assert.Empty(Descendants<ListBox>(view));
            Assert.Empty(Descendants<ListBoxItem>(view));
            var checkboxes = Descendants<CheckBox>(view).ToArray();
            Assert.Equal(2, checkboxes.Length);
            Assert.True(checkboxes[0].IsChecked);
            Assert.False(checkboxes[1].IsChecked);
            Assert.Equal("Actor (1.2.3 from Managed title)", Assert.IsType<TextBlock>(checkboxes[0].Content).Text);
            Assert.Equal("Other (1.2.3 from local.pak)", Assert.IsType<TextBlock>(checkboxes[1].Content).Text);
            rows[1].Source = "renamed.pak";
            Flush();
            Assert.Equal("Other (1.2.3 from renamed.pak)", Assert.IsType<TextBlock>(checkboxes[1].Content).Text);
        }
        finally {
            window.Close();
            RenderOptions.ProcessRenderMode = renderMode;
        }
    });

    [Fact]
    public void HeaderBindsVisibilityScanStateCountAndRemainingWidth() => ui.Run(() => {
        var scanner = new ModScanTabVM();
        var local = new LocalModScanVM(Mock.Of<IModManager>(), scanner);
        var view = new ServerModActorsSectionView { DataContext = new { LocalModScan = local } };
        var window = new Window { Content = view, Width = 1000, Height = 300, ShowInTaskbar = false };
        var renderMode = RenderOptions.ProcessRenderMode;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        try {
            window.Show();
            window.UpdateLayout();
            var button = Descendants<Button>(view).Single(button => Equals(button.Content, "Scan local mods"));
            var progress = Assert.Single(Descendants<ProgressBar>(view));
            Assert.False(button.IsVisible);
            local.HasUnmanagedPaks = local.HasUnscannedPaks = true;
            local.LocalModActorCount = 3;
            window.UpdateLayout();
            Assert.True(button.IsVisible);
            Assert.True(button.IsEnabled);
            Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "3 Mod Actors from Unmanaged Paks detected");
            var card = Assert.Single(Descendants<CollapsibleCard>(view));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(card.IsExpanded);
            local.HasUnscannedPaks = false;
            Assert.False(button.IsEnabled);
            scanner.IsScanning = true;
            scanner.ScanProgress = 42;
            window.UpdateLayout();
            Assert.Equal(Visibility.Visible, progress.Visibility);
            Assert.Equal(42, progress.Value);
            Assert.Equal(button.ActualHeight, progress.ActualHeight);
            var grid = Assert.IsType<Grid>(VisualTreeHelper.GetParent(progress));
            Assert.True(grid.ColumnDefinitions[Grid.GetColumn(progress)].Width.IsStar);
            Assert.True(progress.ActualWidth > button.ActualWidth);
            local.HasUnmanagedPaks = false;
            Assert.False(button.IsVisible);
        }
        finally {
            window.Close();
            RenderOptions.ProcessRenderMode = renderMode;
        }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
