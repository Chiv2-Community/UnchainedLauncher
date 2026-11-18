using System.Text.Json;
using LanguageExt;
using Moq;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.GUI.Services;
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

    private static void Write(string directory, string name, string bytes) {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), bytes);
    }

    private PakDir DirectoryFor(PakDirMetadata? metadata = null) =>
        (PakDir)ModManagerCodec.ToClassType(new StandardModManagerMetadata([], [], metadata), _registry, Target).PakDir;

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
    public async Task OwnershipIsPublishedBeforeCleanupAndSurvivesStaleHostSnapshot() {
        Write(Source, "managed.pak", "owned");
        var metadata = new PakDirMetadata(Source, [new(_coordinates, "managed.pak", 0)]);
        var directory = DirectoryFor(metadata);
        await directory.MigrateSavedPaks();
        Assert.Single(directory.ManagedPaks);
        Assert.Single(JsonSerializer.Deserialize<List<ManagedPak>>(File.ReadAllText(Inventory))!);
        Assert.False(File.Exists(Path.Combine(Source, "managed.pak")));
        var restarted = DirectoryFor(metadata);
        await restarted.MigrateSavedPaks();
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
    public async Task IncomingUnknownCannotReviveMissingDestinationSnapshotEntry() {
        Write(Source, "managed.pak", "unknown");
        var directory = DirectoryFor(new(Target, [new(_coordinates, "managed.pak", 0)]));
        await directory.MigrateSavedPaks();
        Assert.Empty(directory.ManagedPaks);
    }

    [Fact]
    public async Task ExistingSharedInventoryWinsOverConflictingLegacyCoordinates() {
        Write(Target, "current.pak", "current");
        Write(Path.GetDirectoryName(Inventory)!, "inventory.json",
            JsonSerializer.Serialize(new[] { new ManagedPak(_coordinates, "current.pak", 0) }));
        Write(Source, "old.pak", "old");
        var directory = DirectoryFor(new(Source, [new(_coordinates, "old.pak", 0)]));
        await directory.MigrateSavedPaks();
        Assert.Equal("current.pak", Assert.Single(directory.ManagedPaks).PakFileName);
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
        await Assert.ThrowsAsync<IOException>(() => DirectoryFor().MigrateSavedPaks());
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
        await Assert.ThrowsAsync<IOException>(() => DirectoryFor().MigrateSavedPaks());
        Assert.Equal("preserved", File.ReadAllText(Path.Combine(external, "unknown.pak")));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("inventory.json")]
    public async Task InterruptedOrCorruptInventoryBlocksMigration(string name) {
        Write(Source, "unknown.pak", "preserved");
        Write(Path.GetDirectoryName(Inventory)!, name, "invalid");
        await Assert.ThrowsAnyAsync<Exception>(() => DirectoryFor().MigrateSavedPaks());
        Assert.True(File.Exists(Path.Combine(Source, "unknown.pak")));
        Assert.False(File.Exists(Path.Combine(Target, "unknown.pak")));
    }

    [Fact]
    public async Task FailedPublicationRetainsSourcesAndRetriesVerifiedCopies() {
        Write(Source, "managed.pak", "owned");
        Directory.CreateDirectory(Path.Combine(Target, ".unchained", "inventory.tmp"));
        var directory = DirectoryFor(new(Source, [new(_coordinates, "managed.pak", 0)]));
        var failure = await Record.ExceptionAsync(() => directory.MigrateSavedPaks());
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
        Assert.Equal("owned", File.ReadAllText(Path.Combine(Target, "managed.pak")));
        Assert.Empty(System.IO.Directory.GetFiles(Target, "*.partial"));
        System.IO.Directory.Delete(Path.Combine(Target, ".unchained", "inventory.tmp"));
        await directory.MigrateSavedPaks();
        Assert.Single(directory.ManagedPaks);
        Assert.False(File.Exists(Path.Combine(Source, "managed.pak")));
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
        await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryFor().MigrateSavedPaks());
        Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
    }

    [Fact]
    public async Task SharedSourceInventoryOverridesStaleHostOwnership() {
        Write(Source, "managed.pak", "owned");
        Write(Path.Combine(Source, ".unchained"), "inventory.json", "[]");
        var directory = DirectoryFor(new(Source, [new(_coordinates, "managed.pak", 0)]));
        await directory.MigrateSavedPaks();
        Assert.Empty(directory.ManagedPaks);
        Assert.True(File.Exists(Path.Combine(Target, "managed.pak")));
    }

    [Fact]
    public async Task CodecDefersPhysicalMigrationUntilAsyncStartup() {
        Write(Source, "managed.pak", "owned");
        var manager = new ModManager(_registry, new PakDir(Source, [new(_coordinates, "managed.pak", 0)]), [_coordinates]);
        var codec = new ModManagerCodec(_registry, Target);
        var decoded = codec.Deserialize(codec.Serialize(manager)).Result!;
        Assert.Equal(Target, decoded.PakDir.DirPath);
        Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
        Assert.Equal(Source, ((StandardModManagerMetadata)ModManagerCodec.ToJsonType(decoded)).PakDir!.DirPath);
        await ((PakDir)decoded.PakDir).MigrateSavedPaks();
        Assert.Single(((PakDir)decoded.PakDir).ManagedPaks);
        Assert.Equal(Target, ((StandardModManagerMetadata)ModManagerCodec.ToJsonType(decoded)).PakDir!.DirPath);
        Assert.Equal(new[] { _coordinates }, decoded.EnabledModReleaseCoordinates);
    }

    [Fact]
    public async Task ConcurrentMigrationsSerializeAndRespectCancellation() {
        Write(Source, "managed.pak", "owned");
        var metadata = new PakDirMetadata(Source, [new(_coordinates, "managed.pak", 0)]);
        var directory = DirectoryFor(metadata);
        var lockPath = Path.Combine(Target, ".unchained", "inventory.lock");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var cancellation = new CancellationTokenSource()) {
            var blocked = directory.MigrateSavedPaks(cancellationToken: cancellation.Token);
            Assert.False(blocked.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
            Assert.True(File.Exists(Path.Combine(Source, "managed.pak")));
        }
        var other = DirectoryFor(metadata);
        await Task.WhenAll(directory.MigrateSavedPaks(), other.MigrateSavedPaks());
        Assert.Single(directory.ManagedPaks);
        Assert.Single(other.ManagedPaks);
    }

    public void Dispose() {
        if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, true);
    }
}
