using System.Text.Json;
using LanguageExt;
using Moq;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.Core.Utilities;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

[CollectionDefinition("Saved migration", DisableParallelization = true)]
public sealed class SavedMigrationCollection;

[Collection("Saved migration")]
public sealed class SavedPakMigrationTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SavedMigration_" + Guid.NewGuid().ToString("N"));
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private readonly ReleaseCoordinates _coordinates = new("Org", "Mod", "v1");
    private string Target => Path.Combine(_root, "TBL", "Content", "Paks");
    private string Source => Path.Combine(_root, "TBL", "Saved", "Paks");
    private string Inventory => Path.Combine(Target, ".unchained", "inventory.json");
    private string Config => Path.Combine(_root, ".mod_cache", "mod_manager_config.json");

    private static void Write(string directory, string name, string bytes) {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), bytes);
    }

    private PakDir DirectoryFor(PakDirMetadata? metadata = null) {
        var manager = ModManagerCodec.ToClassType(new StandardModManagerMetadata([], [], metadata), _registry, Target);
        _ = new ModManagerPersistence(manager, Config);
        return (PakDir)manager.PakDir;
    }

    private async Task<PakDir> Startup(PakDirMetadata? metadata = null) {
        await Task.Yield();
        if (metadata != null) {
            var manager = new ModManager(_registry, new PakDir(metadata.DirPath, metadata.ManagedPaks), []);
            new ModManagerCodec(_registry).SerializeFile(Config, manager);
        }
        return (PakDir)LegacyOwnershipMigration.Load(Config, _registry, installationPakDirectory: Target).Manager.PakDir;
    }

    [Fact]
    public async Task NondefaultInventorySurvivesStartupAndPrelaunchCheckpoint() {
        var source = Path.Combine(_root, "TBL", "Saved_Server_1", "Paks");
        var owned = new ManagedPak(_coordinates, "owned.pak", 3);
        Write(source, owned.PakFileName, "owned");
        Write(Path.Combine(source, ".unchained"), "inventory.json", JsonSerializer.Serialize(new[] { owned }));
        var (manager, persistence) = LegacyOwnershipMigration.Load(Config, _registry, installationPakDirectory: Target);
        persistence.Save();
        Assert.True((await new SavedPaksPreparer(manager.PakDir, Mock.Of<IUserDialogueSpawner>())
            .PrepareLaunch(new([], Prelude.None, "", false, false, "Server_1", Prelude.None))).IsSome);
        Assert.Equal(owned, Assert.Single(((PakDir)manager.PakDir).ManagedPaks));
        Assert.Equal(owned, Assert.Single(((PakDir)new ModManagerCodec(_registry).Deserialize(File.ReadAllText(Config)).Result!.PakDir).ManagedPaks));
        Assert.Equal("owned", File.ReadAllText(Path.Combine(Target, owned.PakFileName)));
        Assert.False(File.Exists(Path.Combine(source, owned.PakFileName)));
        Assert.False(File.Exists(Path.Combine(source, ".unchained", "inventory.json")));
    }

    [Theory]
    [InlineData("pending", "")]
    [InlineData("inventory.json", "{")]
    [InlineData("inventory.tmp", "[]")]
    public async Task NondefaultUncertaintyStopsStartupBeforePayloadMutation(string name, string content) {
        var source = Path.Combine(_root, "TBL", "Saved_Server_1", "Paks");
        Write(source, "owned.pak", "owned");
        Write(Source, "default.pak", "default");
        Write(Path.Combine(source, ".unchained"), name, content);
        if (name == "pending")
            Write(Path.Combine(source, ".unchained"), "inventory.json",
                JsonSerializer.Serialize(new[] { new ManagedPak(_coordinates, "owned.pak", 3) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => Startup());
        Assert.Equal("owned", File.ReadAllText(Path.Combine(source, "owned.pak")));
        Assert.Equal("default", File.ReadAllText(Path.Combine(Source, "default.pak")));
        Assert.Equal(content, File.ReadAllText(Path.Combine(source, ".unchained", name)));
        if (name == "pending") Assert.True(File.Exists(Path.Combine(source, ".unchained", "inventory.json")));
        Assert.False(Directory.Exists(Target));
        Assert.False(File.Exists(Config));
    }

    [Theory]
    [InlineData("inventory.json", "[]")]
    [InlineData("pending", "")]
    [InlineData("inventory.json", "{")]
    [InlineData("inventory.tmp", "[]")]
    public async Task LateNondefaultEvidenceIsRefusedBeforePrelaunchPayloadMutation(string name, string content) {
        var (manager, persistence) = LegacyOwnershipMigration.Load(Config, _registry, installationPakDirectory: Target);
        var source = Path.Combine(_root, "TBL", "Saved_Server_1", "Paks");
        Write(source, "owned.pak", "owned");
        Write(Path.Combine(source, ".unchained"), name, content);
        persistence.Save();
        var before = File.ReadAllBytes(Config);
        Assert.True((await new SavedPaksPreparer(manager.PakDir, Mock.Of<IUserDialogueSpawner>())
            .PrepareLaunch(new([], Prelude.None, "", false, false, "Server_1", Prelude.None))).IsNone);
        Assert.Equal(before, File.ReadAllBytes(Config));
        Assert.Equal("owned", File.ReadAllText(Path.Combine(source, "owned.pak")));
        Assert.Equal(content, File.ReadAllText(Path.Combine(source, ".unchained", name)));
        Assert.False(File.Exists(Path.Combine(Target, "owned.pak")));
    }

    [Theory]
    [InlineData("saved_Custom")]
    [InlineData("SAVED_Custom")]
    [InlineData("saved")]
    public async Task SavedCaseAliasesFollowPlatformIdentityInCodecAndStartup(string name) {
        var canonical = name.Equals("saved", StringComparison.Ordinal) ? "Saved" : "Saved_Custom";
        var actual = Path.Combine(_root, "TBL", canonical, "Paks");
        var alias = Path.Combine(_root, "TBL", name, "Paks");
        Write(actual, "owned.pak", "owned");
        var owned = new ManagedPak(_coordinates, "owned.pak", 3);
        var metadata = new PakDirMetadata(alias, [owned]);
        var decoded = ModManagerCodec.ToClassType(new StandardModManagerMetadata([], [], metadata), _registry, Target);
        Assert.Equal(OperatingSystem.IsWindows() ? Target : alias, decoded.PakDir.DirPath);
        var started = await Startup(metadata);
        Assert.Equal(OperatingSystem.IsWindows() ? Target : alias, started.DirPath);
        if (OperatingSystem.IsWindows()) {
            Assert.Equal(owned, Assert.Single(started.ManagedPaks));
            Assert.False(File.Exists(Path.Combine(actual, owned.PakFileName)));
            Assert.Equal("owned", File.ReadAllText(Path.Combine(Target, owned.PakFileName)));
        }
        else {
            Assert.True(File.Exists(Path.Combine(actual, owned.PakFileName)));
            Assert.False(File.Exists(Path.Combine(Target, owned.PakFileName)));
        }
    }

    [Fact]
    public async Task MovesNestedPaksAndSignaturesButDoesNotGrantFilenameOwnership() {
        Write(Path.Combine(Source, "nested"), "unknown.PAK", "pak");
        Write(Source, "orphan.sig", "sig");
        Write(Source, "keep.txt", "keep");
        var directory = DirectoryFor();
        await directory.MigrateSavedPaks();
        await directory.MigrateSavedPaks();
        Assert.Empty(directory.ManagedPaks);
        Assert.Equal("pak", File.ReadAllText(Path.Combine(Target, "unknown.PAK")));
        Assert.Equal("sig", File.ReadAllText(Path.Combine(Target, "orphan.sig")));
        Assert.True(File.Exists(Path.Combine(Source, "keep.txt")));
    }

    [Fact]
    public async Task OwnershipIsPublishedBeforeCleanupAndSurvivesRestart() {
        Write(Source, "managed.pak", "owned");
        var metadata = new PakDirMetadata(Source, [new(_coordinates, "managed.pak", 0)]);
        var directory = DirectoryFor(metadata);
        await directory.MigrateSavedPaks();
        Assert.Single(directory.ManagedPaks);
        Assert.Single(((PakDir)new ModManagerCodec(_registry).Deserialize(File.ReadAllText(Config)).Result!.PakDir).ManagedPaks);
        Assert.False(File.Exists(Inventory));
        Assert.False(File.Exists(Path.Combine(Source, "managed.pak")));
        var restarted = await Startup();
        Assert.Single(restarted.ManagedPaks);
    }

    [Fact]
    public async Task MissingSourceCannotClaimSameNamedDestination() {
        Write(Target, "managed.pak", "unrelated");
        var directory = DirectoryFor(new(Source, [new(_coordinates, "managed.pak", 0)]));
        await directory.MigrateSavedPaks();
        Assert.Empty(directory.ManagedPaks);
    }

    [Fact]
    public async Task MissingAttributedSourceCannotClaimAnotherProfilesSameNamedFile() {
        Write(Path.Combine(_root, "TBL", "Saved_Unchained", "Paks"), "managed.pak", "unknown");
        var directory = DirectoryFor(new(Source, [new(_coordinates, "managed.pak", 0)]));
        await directory.MigrateSavedPaks();
        Assert.Empty(directory.ManagedPaks);
        Assert.Equal("unknown", File.ReadAllText(Path.Combine(Target, "managed.pak")));
    }

    [Fact]
    public async Task IncomingUnknownCannotReviveMissingDestinationSnapshotEntry() {
        Write(Source, "managed.pak", "unknown");
        var directory = DirectoryFor(new(Target, [new(_coordinates, "managed.pak", 0)]));
        await directory.MigrateSavedPaks();
        Assert.Empty(directory.ManagedPaks);
    }

    [Fact]
    public async Task ConflictingLegacyCoordinatesPreserveSources() {
        Write(Target, "current.pak", "current");
        Write(Path.GetDirectoryName(Inventory)!, "inventory.json",
            JsonSerializer.Serialize(new[] { new ManagedPak(_coordinates, "current.pak", 0) }));
        Write(Source, "old.pak", "old");
        await Assert.ThrowsAsync<IOException>(() => Startup(new(Source, [new(_coordinates, "old.pak", 0)])));
        Assert.True(File.Exists(Path.Combine(Source, "old.pak")));
        Assert.True(File.Exists(Path.Combine(Target, "old.pak")));
    }

    [Theory]
    [InlineData("unknown.pak")]
    [InlineData("unknown.sig")]
    public async Task ConflictsPreserveEntireBatch(string conflict) {
        Write(Source, "unknown.pak", "source");
        Write(Source, "unknown.sig", "signature");
        Write(Source, "other.pak", "other");
        Write(Target, conflict, "different");
        await Assert.ThrowsAsync<IOException>(() => Startup());
        Assert.True(File.Exists(Path.Combine(Source, "other.pak")));
        Assert.False(File.Exists(Path.Combine(Target, "other.pak")));
    }

    [Fact]
    public async Task CrossProfileConflictsPreserveSources() {
        Write(Source, "unknown.pak", "source");
        var profile = Path.Combine(_root, "TBL", "Saved_Server_1", "Paks");
        Write(profile, "unknown.pak", "different");
        await Assert.ThrowsAsync<IOException>(() => DirectoryFor().MigrateSavedPaks("Server_1"));
        Assert.False(File.Exists(Path.Combine(Target, "unknown.pak")));
        Assert.True(File.Exists(Path.Combine(profile, "unknown.pak")));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("source-file")]
    [InlineData("inventory")]
    public async Task LinksAreRejected(string kind) {
        var external = Path.Combine(_root, "external");
        Write(external, "unknown.pak", "preserved");
        var link = kind switch {
            "source" => Source,
            "destination" => Target,
            "inventory" => Path.Combine(Target, ".unchained"),
            _ => Path.Combine(Source, "unknown.pak")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (kind == "source-file") File.CreateSymbolicLink(link, Path.Combine(external, "unknown.pak"));
        else Directory.CreateSymbolicLink(link, external);
        await Assert.ThrowsAsync<IOException>(() => Startup());
        Assert.Equal("preserved", File.ReadAllText(Path.Combine(external, "unknown.pak")));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("inventory.json")]
    public async Task InterruptedOrCorruptInventoryBlocksMigration(string name) {
        Write(Source, "unknown.pak", "preserved");
        Write(Path.GetDirectoryName(Inventory)!, name, "invalid");
        await Assert.ThrowsAnyAsync<Exception>(() => Startup());
        Assert.True(File.Exists(Path.Combine(Source, "unknown.pak")));
        Assert.False(File.Exists(Path.Combine(Target, "unknown.pak")));
    }

    [Fact]
    public async Task FailedPublicationRetainsSourcesAndRetriesVerifiedCopies() {
        Write(Source, "managed.pak", "owned");
        Directory.CreateDirectory(Config);
        var directory = DirectoryFor(new(Source, [new(_coordinates, "managed.pak", 0)]));
        var failure = await Record.ExceptionAsync(() => directory.MigrateSavedPaks());
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
        Assert.Equal("owned", File.ReadAllText(Path.Combine(Target, "managed.pak")));
        Assert.Empty(System.IO.Directory.GetFiles(Target, "*.partial"));
        System.IO.Directory.Delete(Config);
        // A failed ownership checkpoint cannot turn cleanup evidence into authority.
        await Assert.ThrowsAsync<IOException>(() => Startup());
        Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
    }

    [Fact]
    public async Task IndependentInstallationsAndCustomDirectoriesAreNotSwept() {
        Write(Source, "unknown.pak", "preserved");
        var other = Path.Combine(_root, "other", "TBL", "Content", "Paks");
        var independent = (PakDir)ModManagerCodec.ToClassType(new StandardModManagerMetadata([], [], null), _registry, other).PakDir;
        await independent.MigrateSavedPaks();
        Assert.True(File.Exists(Path.Combine(Source, "unknown.pak")));
        var custom = Path.Combine(_root, "custom");
        var directory = DirectoryFor(new(custom, []));
        Assert.Equal(custom, directory.DirPath);
        await directory.MigrateSavedPaks();
        Assert.True(File.Exists(Path.Combine(Source, "unknown.pak")));
    }

    [Fact]
    public async Task LaunchConflictCancelsBeforeDownstreamPreparation() {
        Write(Source, "unknown.pak", "source");
        Write(Target, "unknown.pak", "conflict");
        var dialogue = new Mock<IUserDialogueSpawner>();
        var preparer = new SavedPaksPreparer(DirectoryFor(), dialogue.Object);
        Assert.True((await preparer.PrepareLaunch(new([], Prelude.None, "", false, false, Prelude.None, Prelude.None))).IsNone);
        dialogue.Verify(d => d.DisplayMessage(It.Is<string>(s => s.Contains("cancelled"))), Times.Once);
    }

    [Fact]
    public void SourcePolicyDoesNotScanSharedUserRootsWithoutCachedEvidence() {
        Assert.All(SavedPakMigration.SourceDirectories(Target), source => Assert.StartsWith(_root, source));
        Assert.Throws<IOException>(() => SavedPakMigration.SourceDirectories(Target, "../escape").ToArray());
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            Assert.True(SavedPakMigration.SameDirectory(drive.RootDirectory.FullName, drive.RootDirectory.FullName));
    }

    [Fact]
    public async Task SharedLocalProfileRequiresCachedSourceEvidence() {
        var previous = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var local = Path.Combine(_root, "drive_c", "users", "steamuser", "AppData", "Local");
        try {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", local);
            var source = Path.Combine(local, "Chivalry 2", "Saved_Server_1", "Paks");
            Write(source, "managed.pak", "owned");
            await DirectoryFor().MigrateSavedPaks("Server_1");
            Assert.True(File.Exists(Path.Combine(source, "managed.pak")));
            var directory = DirectoryFor(new(source, [new(_coordinates, "managed.pak", 0)]));
            await directory.MigrateSavedPaks("Server_1");
            Assert.Single(directory.ManagedPaks);
            Assert.False(File.Exists(Path.Combine(source, "managed.pak")));
        }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", previous); }
    }

    [Fact]
    public async Task SourcePendingMarkerAlsoBlocksMigration() {
        Write(Source, "managed.pak", "preserved");
        Write(Path.Combine(Source, ".unchained"), "pending", "");
        await Assert.ThrowsAsync<InvalidDataException>(() => Startup());
        Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
    }

    [Fact]
    public async Task SharedSourceInventoryOverridesStaleHostOwnership() {
        Write(Source, "managed.pak", "owned");
        Write(Path.Combine(Source, ".unchained"), "inventory.json", "[]");
        var directory = await Startup(new(Source, [new(_coordinates, "managed.pak", 0)]));
        Assert.Empty(directory.ManagedPaks);
        Assert.True(File.Exists(Path.Combine(Target, "managed.pak")));
    }

    [Fact]
    public async Task CodecDefersPhysicalMigrationUntilAsyncStartup() {
        Write(Source, "managed.pak", "owned");
        var manager = new ModManager(_registry, new PakDir(Source, [new(_coordinates, "managed.pak", 0)]), [_coordinates]);
        var codec = new ModManagerCodec(_registry, Target);
        var decoded = codec.Deserialize(codec.Serialize(manager)).Result!;
        _ = new ModManagerPersistence(decoded, Config);
        Assert.Equal(Target, decoded.PakDir.DirPath);
        Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
        Assert.Equal(Source, ((StandardModManagerMetadata)ModManagerCodec.ToJsonType(decoded)).PakDir!.DirPath);
        await ((PakDir)decoded.PakDir).MigrateSavedPaks();
        Assert.Single(((PakDir)decoded.PakDir).ManagedPaks);
        Assert.Equal(Target, ((StandardModManagerMetadata)ModManagerCodec.ToJsonType(decoded)).PakDir!.DirPath);
        Assert.Equal(new[] { _coordinates }, decoded.EnabledModReleaseCoordinates);
    }

    [Fact]
    public async Task CancellationDoesNotTouchSourcesOrCreateInventoryLocks() {
        Write(Source, "managed.pak", "owned");
        var metadata = new PakDirMetadata(Source, [new(_coordinates, "managed.pak", 0)]);
        var directory = DirectoryFor(metadata);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.MigrateSavedPaks(cancellationToken: cancellation.Token));
        Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
        await directory.MigrateSavedPaks();
        Assert.Single(directory.ManagedPaks);
        Assert.False(File.Exists(Path.Combine(Target, ".unchained", "inventory.lock")));
    }

    public void Dispose() {
        if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, true);
    }
}
