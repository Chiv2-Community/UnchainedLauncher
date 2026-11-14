using Moq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views.Controls;
using UnchainedLauncher.GUI.Views.Servers.Sections;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class LocalModScanHeaderTests(HomeLaunchUi ui) {
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
            Assert.Equal(Visibility.Collapsed, button.Visibility);
            local.HasUnmanagedPaks = local.HasUnscannedPaks = true;
            local.LocalModActorCount = 3;
            window.UpdateLayout();
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.True(button.IsEnabled);
            Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "3 Local Mod Actors Detected");
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
            Assert.Equal(Visibility.Collapsed, button.Visibility);
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
