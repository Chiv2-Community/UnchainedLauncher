namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

public class CatalogStartupSourceTests {
    [Fact]
    public void StartupUsesTheGamePakFolderAndDisposesItsWatcher() {
        var app = ReadSource("UnchainedLauncher.GUI", "App.xaml.cs");
        Assert.Contains("DefaultPakDirectory = Path.GetFullPath(FilePaths.PakDir)", app);
        Assert.Contains("availableModsAndMaps.StartWatchingLocalPaks();", app);
        Assert.Contains("Exit += (_, _) => availableModsAndMaps.Dispose();", app);
        var view = ReadSource("UnchainedLauncher.UnrealModScanner.GUI", "Views", "UnrealModsView.xaml.cs");
        foreach (var property in new[] { "InitialDirectory", "DefaultDirectory", "FolderName" })
            Assert.Contains($"{property} = ModScannerVM.DefaultPakDirectory", view);
    }

    private static string ReadSource(params string[] parts) {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent) {
            var path = Path.Combine(directory.FullName, Path.Combine(parts));
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException($"Could not locate repository source: {Path.Combine(parts)}");
    }
}
