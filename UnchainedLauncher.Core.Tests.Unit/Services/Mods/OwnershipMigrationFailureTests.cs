using LanguageExt;
using Moq;
using System.Text.Json;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using PakDirectory = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods;

public sealed class OwnershipMigrationFailureTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OwnershipFailure_" + Guid.NewGuid().ToString("N"));
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private string Paks => Path.Combine(_root, "TBL", "Content", "Paks");
    private string Config => Path.Combine(_root, ".mod_cache", "mod_manager_config.json");
    private string Inventory => Path.Combine(Paks, ".unchained", "inventory.json");
    private readonly ManagedPak _owned = new(new("Org", "Mod", "v1"), "owned.pak", 1);

    public OwnershipMigrationFailureTests() {
        Directory.CreateDirectory(Path.GetDirectoryName(Inventory)!);
        File.WriteAllText(Path.Combine(Paks, _owned.PakFileName), "owned");
        var release = new Release("v1", "hash", "owned.pak", DateTime.UtcNow,
            new("https://github.com/Org/Mod", "Mod", "", null, null, [], [], []), new([], [], [], [], []), "");
        new ModManagerCodec(_registry).SerializeFile(Config,
            new ModManager(_registry, new PakDirectory(Paks, []), [], [new Mod(release.Info, [release])]));
        File.WriteAllText(Inventory, JsonSerializer.Serialize(new[] { _owned }));
    }

    private ModManager Startup() => LegacyOwnershipMigration.Load(Config, _registry, installationPakDirectory: Paks).Manager;

    [Fact]
    public void FailedConfigReplaceLeavesInventoryAndBackupForRetry() {
        var before = File.ReadAllBytes(Config);
        using (var held = new FileStream(Config, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Assert.True(Record.Exception(() => Startup()) is IOException or UnauthorizedAccessException);
            Assert.True(File.Exists(Inventory));
            Assert.Equal(before, File.ReadAllBytes(Config));
            Assert.NotEmpty(Directory.GetFiles(LegacyOwnershipMigration.BackupRoot(Config), "inventory.json", SearchOption.AllDirectories));
        }
        Assert.Equal(_owned, Assert.Single(((PakDirectory)Startup().PakDir).ManagedPaks));
    }

    [Fact]
    public async Task FailedRetirementStopsStartupThenRetryCannotResurrectLaterRemoval() {
        using (var held = new FileStream(Inventory, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Assert.True(Record.Exception(() => Startup()) is IOException or UnauthorizedAccessException);
            Assert.True(File.Exists(Inventory));
            var flushed = new ModManagerCodec(_registry).Deserialize(File.ReadAllText(Config)).Result!;
            Assert.Equal(_owned, Assert.Single(((PakDirectory)flushed.PakDir).ManagedPaks));
        }
        var manager = Startup();
        Assert.False(File.Exists(Inventory));
        Assert.True((await manager.PakDir.Uninstall(_owned.Coordinates)).IsRight);
        Assert.Empty(((PakDirectory)Startup().PakDir).ManagedPaks);
    }

    [Fact]
    public void FailedSelectionSaveIsContainedAndRetryable() {
        var manager = Startup();
        var failures = new List<Exception>();
        var persistence = new ModManagerPersistence(manager, Config, failures.Add);
        using (var held = new FileStream(Config, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            manager.EnableModRelease(_owned.Coordinates);
            Assert.Single(failures);
            Assert.Single(manager.EnabledModReleaseCoordinates);
            Assert.True(Record.Exception(persistence.Save) is IOException or UnauthorizedAccessException);
        }
        persistence.Save();
        Assert.Single(Startup().EnabledModReleaseCoordinates);
    }

    [Fact]
    public async Task FailedFinalCheckpointCancelsLaunchAndPreservesWriteAheadOwnership() {
        File.Delete(Path.Combine(Paks, _owned.PakFileName));
        var release = new Release("v1", "hash", "owned.pak", DateTime.UtcNow,
            new("https://github.com/Org/Mod", "Mod", "", null, null, [], [], []), new([], [], [], [], []), "");
        var registry = new Mock<IModRegistry>();
        var manager = new ModManager(registry.Object, new PakDirectory(Paks, []), [_owned.Coordinates], [new Mod(release.Info, [release])]);
        var persistence = new ModManagerPersistence(manager, Config);
        persistence.Save();
        var before = File.ReadAllBytes(Config);
        FileStream? held = null;
        registry.Setup(r => r.DownloadPak(It.IsAny<ReleaseCoordinates>(), It.IsAny<string>()))
            .Returns((ReleaseCoordinates _, string path) => {
                held = new FileStream(Config, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Prelude.RightAsync<ModPakStreamAcquisitionFailure, FileWriter>(new FileWriter(path, new MemoryStream([1, 2, 3]), 3));
            });
        var closed = false;
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => closed = true);
        try {
            var result = await new Chivalry2ModsInstaller(manager, dialogue.Object).PrepareLaunch(
                new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None));
            Assert.True(result.IsNone);
            Assert.True(closed);
            Assert.Single(((PakDirectory)manager.PakDir).ManagedPaks);
            Assert.NotEqual(before, File.ReadAllBytes(Config));
            var durable = new ModManagerCodec(registry.Object).Deserialize(File.ReadAllText(Config)).Result!;
            Assert.Equal(((PakDirectory)manager.PakDir).ManagedPaks, ((PakDirectory)durable.PakDir).ManagedPaks);
            dialogue.Verify(d => d.DisplayMessage(It.Is<string>(s => s.Contains("ownership") && s.Contains("cancelled"))), Times.Once);
        }
        finally { held?.Dispose(); }
        persistence.Save();
        Assert.Single(((PakDirectory)new ModManagerCodec(_registry).Deserialize(File.ReadAllText(Config)).Result!.PakDir).ManagedPaks);
    }

    public void Dispose() => Directory.Delete(_root, true);
}