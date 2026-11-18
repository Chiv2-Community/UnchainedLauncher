using Moq;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using LauncherPakDir = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

public sealed class SavedPakMigrationRootTests {
    [Fact]
    public void VolumeRootsAreBoundariesIncludingProtonDriveMappings() {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady)) {
            var published = false;
            SavedPakMigration.Migrate(drive.RootDirectory.FullName, [], _ => published = true);
            Assert.True(published);
        }
    }

    [Fact]
    public async Task LockedSourceCleanupKeepsPublishedOwnershipAndRetriesAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "SavedMigration_" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "TBL", "Saved", "Paks");
        var target = Path.Combine(root, "TBL", "Content", "Paks");
        Directory.CreateDirectory(source);
        var coordinates = new ReleaseCoordinates("Org", "Mod", "v1");
        var metadata = new StandardModManagerMetadata([], [], new(source, [new(coordinates, "managed.pak", 0)]));
        var registry = Mock.Of<IModRegistry>();
        var manager = ModManagerCodec.ToClassType(metadata, registry, target);
        var pak = Path.Combine(source, "managed.pak");
        File.WriteAllText(pak, "owned");
        try {
            using (var held = new FileStream(pak, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                await Assert.ThrowsAsync<IOException>(() => ((LauncherPakDir)manager.PakDir).MigrateSavedPaks());
                Assert.True(File.Exists(pak));
                Assert.Equal("owned", File.ReadAllText(Path.Combine(target, "managed.pak")));
                Assert.Equal(source, ((StandardModManagerMetadata)ModManagerCodec.ToJsonType(manager)).PakDir!.DirPath);
            }
            var restarted = ModManagerCodec.ToClassType(ModManagerCodec.ToJsonType(manager), registry, target);
            await ((LauncherPakDir)restarted.PakDir).MigrateSavedPaks();
            Assert.Single(((LauncherPakDir)restarted.PakDir).ManagedPaks);
            Assert.False(File.Exists(pak));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WindowsSourceAliasesAcquireOnlyOneInventoryLock() {
        var root = Path.Combine(Path.GetTempPath(), "SavedMigration_" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "TBL", "Saved_Unchained", "Paks");
        var target = Path.Combine(root, "TBL", "Content", "Paks");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "unknown.pak"), "preserved");
        try {
            var alias = Path.Combine(root, "TBL", "Saved_UNCHAINED", "Paks");
            var metadata = new StandardModManagerMetadata([], [], new(alias, []));
            var manager = ModManagerCodec.ToClassType(metadata, Mock.Of<IModRegistry>(), target);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ((LauncherPakDir)manager.PakDir).MigrateSavedPaks(cancellationToken: cancellation.Token);
            Assert.False(File.Exists(Path.Combine(source, "unknown.pak")));
            Assert.Equal("preserved", File.ReadAllText(Path.Combine(target, "unknown.pak")));
        }
        finally { Directory.Delete(root, true); }
    }
}