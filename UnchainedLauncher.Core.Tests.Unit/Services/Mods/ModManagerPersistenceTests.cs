using LanguageExt;
using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using PakDirectory = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

[Collection("Home launch UI")]
public sealed class ModManagerPersistenceTests : IDisposable {
    private readonly Processes.HomeLaunchUi ui;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ModPersistence_" + Guid.NewGuid().ToString("N"));
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private string Config => Path.Combine(_root, "cache", "mods.json");
    private string Paks => Path.Combine(_root, "Paks");

    public ModManagerPersistenceTests(Processes.HomeLaunchUi ui) {
        this.ui = ui;
        Directory.CreateDirectory(Paks);
    }

    private static Release Release(string name) => new("v1.0.0", "hash", name + ".pak", DateTime.UtcNow,
        new($"https://github.com/Org/{name}", name, "", null, null, [], [], []),
        new AssetCollections([], [], [], [], []), "");

    private ModManager Manager(params Release[] releases) => new(_registry, new PakDirectory(Paks, []), [],
        releases.Select(release => new Mod(release.Info, [release])));

    private ModManager Reload() => new ModManagerCodec(_registry).Deserialize(File.ReadAllText(Config)).Result!;

    private static ModInstallRequest Install(Release release) => new(ReleaseCoordinates.FromRelease(release),
        path => new FileWriter(path, new MemoryStream([1, 2, 3]), 3));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionChangesSurviveReloadWithoutShutdown(bool disable) {
        var release = Release("GiantSlayers");
        var manager = Manager(release);
        new ModManagerPersistence(manager, Config).Save();
        Assert.True(manager.EnableModRelease(release));
        Assert.Equal(ReleaseCoordinates.FromRelease(release), Assert.Single(Reload().EnabledModReleaseCoordinates));
        if (disable) Assert.True(manager.DisableModRelease(release));

        Assert.Equal(disable ? [] : new[] { ReleaseCoordinates.FromRelease(release) }, Reload().EnabledModReleaseCoordinates);
    }

    [Fact]
    public async Task InstalledAndRelocatedOwnershipSurvivesReloadWithoutShutdown() {
        var giant = Release("GiantSlayers");
        var dependency = Release("Dependency");
        var manager = Manager(giant, dependency);
        new ModManagerPersistence(manager, Config).Save();
        manager.EnableModRelease(giant);
        var paks = (PakDirectory)manager.PakDir;
        foreach (var releases in new[] { new[] { giant }, new[] { dependency, giant }, Array.Empty<Release>() }) {
            var results = await paks.InstallModSet(releases.Select(Install), Option<AccumulatedMemoryProgress>.None).ToListAsync();
            Assert.All(results, result => Assert.True(result.IsRight, result.ToString()));
            Assert.Equal(await paks.GetManagedPaks(), await Reload().PakDir.GetManagedPaks());
            Assert.Equal(releases.Length, (await Reload().PakDir.GetManagedPaks()).Length);
        }
    }

    [Fact]
    public async Task SuccessfulDownloadSurvivesALaterFailedDownload() {
        var giant = Release("GiantSlayers");
        var other = Release("Other");
        var manager = Manager(giant, other);
        new ModManagerPersistence(manager, Config).Save();
        manager.EnableModRelease(giant);
        var failed = new ModInstallRequest(ReleaseCoordinates.FromRelease(other),
            _ => LanguageExt.Common.Error.New("Download failed"));

        var results = await manager.PakDir.InstallModSet([Install(giant), failed], Option<AccumulatedMemoryProgress>.None).ToListAsync();

        Assert.True(results[0].IsRight);
        Assert.True(results[1].IsLeft);
        Assert.Equal(ReleaseCoordinates.FromRelease(giant), Assert.Single(await Reload().PakDir.GetManagedPaks()).Coordinates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSelectionSaveReportsWithoutInterruptingTheRealCommand(bool disable) => ui.Run(() => {
        var release = Release("GiantSlayers");
        var manager = Manager(release);
        var failures = new List<Exception>();
        var persistence = new ModManagerPersistence(manager, Config, failures.Add);
        if (disable) manager.EnableModRelease(release);
        persistence.Save();
        var previous = File.ReadAllBytes(Config);
        var notified = false;
        manager.ModEnabled += (_, _) => notified = true;
        manager.ModDisabled += _ => notified = true;
        var vm = new ModVM(manager.Mods.Single(), disable ? Option<Release>.Some(release) : Option<Release>.None, manager);
        using (var locked = new FileStream(Config, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Assert.Null(Record.Exception(() => vm.EnableOrDisableCommand.Execute(null)));
            var error = Assert.Single(failures);
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            Assert.True(notified);
            Assert.Equal(!disable, vm.IsEnabled);
            Assert.Equal(disable ? [] : new[] { ReleaseCoordinates.FromRelease(release) }, manager.EnabledModReleaseCoordinates);
            Assert.Equal(previous, File.ReadAllBytes(Config));
            Assert.True(Record.Exception(persistence.Save) is IOException or UnauthorizedAccessException);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Config)!, "*.tmp"));
        }
        persistence.Save();
        Assert.Equal(manager.EnabledModReleaseCoordinates, Reload().EnabledModReleaseCoordinates);
    });

    public void Dispose() => Directory.Delete(_root, recursive: true);
}