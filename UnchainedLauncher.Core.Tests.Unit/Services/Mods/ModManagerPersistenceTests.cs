using LanguageExt;
using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using PakDirectory = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

public sealed class ModManagerPersistenceTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ModPersistence_" + Guid.NewGuid().ToString("N"));
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private string Config => Path.Combine(_root, "cache", "mods.json");
    private string Paks => Path.Combine(_root, "Paks");

    public ModManagerPersistenceTests() => Directory.CreateDirectory(Paks);

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
            var reloaded = Reload();
            Assert.Equal(paks.ManagedPaks, reloaded.PakDir.ManagedPaks);
            Assert.Equal(releases.Length, reloaded.PakDir.ManagedPaks.Count());
        }
    }

    [Fact]
    public async Task OwnershipIsSavedOnceAfterTheDownloadBatch() {
        var giant = Release("GiantSlayers");
        var other = Release("Other");
        var manager = Manager(giant, other);
        new ModManagerPersistence(manager, Config).Save();
        var paks = (PakDirectory)manager.PakDir;
        var checkpoints = 0;
        paks.OwnershipChanged += (_, _) => checkpoints++;
        await using (var results = paks.InstallModSet([Install(giant), Install(other)], Option<AccumulatedMemoryProgress>.None).GetAsyncEnumerator()) {
            for (var i = 0; i < 2; i++) {
                Assert.True(await results.MoveNextAsync());
                Assert.True(results.Current.IsRight);
                Assert.Equal(0, checkpoints);
                Assert.Empty(Reload().PakDir.ManagedPaks);
            }
            Assert.False(await results.MoveNextAsync());
        }
        Assert.Equal(1, checkpoints);
        Assert.Equal(2, Reload().PakDir.ManagedPaks.Count());
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
        Assert.Equal(ReleaseCoordinates.FromRelease(giant), Assert.Single(Reload().PakDir.ManagedPaks).Coordinates);
    }

    [Fact]
    public void AdoptedOwnershipSurvivesReloadWithoutShutdown() {
        var release = Release("GiantSlayers");
        var manager = Manager(release);
        new ModManagerPersistence(manager, Config).Save();
        manager.EnableModRelease(release);
        File.WriteAllText(Path.Combine(Paks, "GiantSlayers (1).pak"), "pak");
        var adopted = new ManagedPak(ReleaseCoordinates.FromRelease(release), "GiantSlayers (1).pak", 0);
        ((PakDirectory)manager.PakDir).TrackPak(adopted);

        Assert.Equal(adopted, Assert.Single(Reload().PakDir.ManagedPaks));
    }

    [Fact]
    public void FailedPublicationPreservesPreviousSnapshotAndReportsFailure() {
        var release = Release("GiantSlayers");
        var manager = Manager(release);
        var persistence = new ModManagerPersistence(manager, Config);
        persistence.Save();
        var previous = File.ReadAllBytes(Config);
        using (var locked = new FileStream(Config, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            var error = Record.Exception(() => manager.EnableModRelease(release));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            Assert.Equal(previous, File.ReadAllBytes(Config));
        }
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Config)!, "*.tmp"));
        persistence.Save();
        Assert.Single(Reload().EnabledModReleaseCoordinates);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}