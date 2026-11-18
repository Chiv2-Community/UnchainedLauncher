using Moq;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using LauncherPakDir = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

[CollectionDefinition("Saved ownership migration", DisableParallelization = true)]
public sealed class SavedOwnershipMigrationCollection;

[Collection("Saved ownership migration")]
public sealed class SavedPakMigrationRootTests {
    [Fact]
    public void VolumeRootsAreBoundariesIncludingProtonDriveMappings() {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady)) {
            var published = false;
            SavedPakMigration.Migrate(drive.RootDirectory.FullName, [], (_, _) => published = true);
            Assert.True(published);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task LockedSourceCleanupKeepsPublishedOwnershipAndRetries(bool attributedGlobal, bool restart) {
        var root = Path.Combine(Path.GetTempPath(), "SavedMigration_" + Guid.NewGuid().ToString("N"));
        var previousLocal = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var local = Path.Combine(root, "Local");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", local);
        var source = attributedGlobal ? Path.Combine(local, "Chivalry 2", "Saved_Custom", "Paks")
            : Path.Combine(root, "TBL", "Saved", "Paks");
        var target = Path.Combine(root, "TBL", "Content", "Paks");
        Directory.CreateDirectory(source);
        var coordinates = new ReleaseCoordinates("Org", "Mod", "v1");
        var metadata = new StandardModManagerMetadata([], [], new(source, [new(coordinates, "managed.pak", 0)]));
        var registry = Mock.Of<IModRegistry>();
        var manager = ModManagerCodec.ToClassType(metadata, registry, target);
        var config = Path.Combine(root, ".mod_cache", "mod_manager_config.json");
        _ = new ModManagerPersistence(manager, config);
        var pak = Path.Combine(source, "managed.pak");
        File.WriteAllText(pak, "owned");
        try {
            using (var held = new FileStream(pak, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                await Assert.ThrowsAsync<IOException>(() => ((LauncherPakDir)manager.PakDir).MigrateSavedPaks());
                Assert.True(File.Exists(pak));
                Assert.Equal("owned", File.ReadAllText(Path.Combine(target, "managed.pak")));
                Assert.Equal(target, ((StandardModManagerMetadata)ModManagerCodec.ToJsonType(manager)).PakDir!.DirPath);
            }
            var restarted = restart ? LegacyOwnershipMigration.Load(config, registry, installationPakDirectory: target).Manager : manager;
            await ((LauncherPakDir)restarted.PakDir).MigrateSavedPaks();
            Assert.Single(((LauncherPakDir)restarted.PakDir).ManagedPaks);
            Assert.False(File.Exists(pak));
        }
        finally {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", previousLocal);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WindowsSourceAliasesTransferOnlyOnce() {
        var root = Path.Combine(Path.GetTempPath(), "SavedMigration_" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "TBL", "Saved_Unchained", "Paks");
        var target = Path.Combine(root, "TBL", "Content", "Paks");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "unknown.pak"), "preserved");
        try {
            var alias = Path.Combine(root, "TBL", "Saved_UNCHAINED", "Paks");
            var metadata = new StandardModManagerMetadata([], [], new(alias, []));
            var manager = ModManagerCodec.ToClassType(metadata, Mock.Of<IModRegistry>(), target);
            _ = new ModManagerPersistence(manager, Path.Combine(root, ".mod_cache", "mod_manager_config.json"));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ((LauncherPakDir)manager.PakDir).MigrateSavedPaks(cancellationToken: cancellation.Token);
            Assert.False(File.Exists(Path.Combine(source, "unknown.pak")));
            Assert.Equal("preserved", File.ReadAllText(Path.Combine(target, "unknown.pak")));
        }
        finally { Directory.Delete(root, true); }
    }
}