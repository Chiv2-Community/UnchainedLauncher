using LanguageExt;
using LanguageExt.Common;
using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using LauncherPakDir = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

[CollectionDefinition("Pak installation files", DisableParallelization = true)]
public sealed class PakInstallationFiles;

// File-safety cases retained from SharedPakInventoryTests, using released persistence.
[Collection("Pak installation files")]
public sealed class PakMutationSafetyTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PakSafety_" + Guid.NewGuid().ToString("N"));
    private string Paks => Path.Combine(_root, "Paks");
    private string Config => Path.Combine(_root, ".mod_cache", "mod_manager_config.json");
    private readonly ReleaseCoordinates _a = new("test", "A", "1");
    private readonly ReleaseCoordinates _b = new("test", "B", "1");
    private readonly IModRegistry _registry = Mock.Of<IModRegistry>();
    private int _downloads;

    public PakMutationSafetyTests() {
        Directory.CreateDirectory(Paks);
        File.WriteAllText(Path.Combine(Paks, "pakchunk0-WindowsNoEditor.sig"), "signature");
    }

    private LauncherPakDir New(IEnumerable<ManagedPak>? legacy = null) {
        var paks = new LauncherPakDir(Paks, legacy ?? []);
        new ModManagerPersistence(new ModManager(_registry, paks, []), Config).Save();
        return paks;
    }

    private LauncherPakDir Reload() {
        var manager = new ModManagerCodec(_registry).Deserialize(File.ReadAllText(Config)).Result!;
        _ = new ModManagerPersistence(manager, Config);
        return (LauncherPakDir)manager.PakDir;
    }

    private EitherAsync<Error, FileWriter> Write(string path) {
        _downloads++;
        return new FileWriter(path, new MemoryStream([1, 2, 3]), 3);
    }

    private async Task Install(LauncherPakDir paks, params ReleaseCoordinates[] coordinates) {
        await foreach (var result in paks.InstallModSet(coordinates.Select(c => new ModInstallRequest(c, Write)), Prelude.None))
            result.IfLeft(e => throw e);
    }

    [Fact]
    public async Task DeniedReservationLeavesOwnershipUnchangedAndAllowsRetryAndReset() {
        var destination = Path.Combine(Paks, "q__-__A.pak.partial");
        Directory.CreateDirectory(destination);
        var paks = New();
        var results = await paks.InstallModSet([new(_a, Write)], Prelude.None).ToListAsync();
        Assert.True(Assert.Single(results).IsLeft);
        Assert.Equal(0, _downloads);
        Assert.Empty(Reload().ManagedPaks);
        Directory.Delete(destination);
        await Install(paks, _a);
        Assert.Equal(1, _downloads);
        Assert.True((await paks.Reset()).IsRight);
        Assert.Empty(Reload().ManagedPaks);
        Assert.Empty(Directory.GetFiles(Paks, "*.pak"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailedDownloadDoesNotLeaveUnmanagedPartialFiles(int failure) {
        var paks = New();
        var results = await paks.InstallModSet([
            new ModInstallRequest(_a, path => failure switch {
                1 => throw new IOException("writer acquisition failed"),
                2 => Task.FromException<Either<Error, FileWriter>>(new IOException("writer acquisition failed")).ToAsync(),
                _ => new FileWriter(path, new FailedStream(), 3)
            })
        ], Prelude.None).ToListAsync();
        Assert.True(Assert.Single(results).IsLeft);
        Assert.Empty(Directory.GetFiles(Paks, "*.pak"));
        Assert.Empty(Directory.GetFiles(Paks, "*.partial"));
        Assert.Empty(Reload().ManagedPaks);
        await Install(paks, _a);
        Assert.Equal("q__-__A.pak", Assert.Single(Reload().ManagedPaks).PakFileName);
        Assert.True((await paks.Reset()).IsRight);
        Assert.Empty(Reload().ManagedPaks);
    }

    [Fact]
    public async Task SignatureCollisionSkipsUnknownSignatureAndPreservesIt() {
        var paks = New();
        await Install(paks, _a);
        var unknownSignature = Path.Combine(Paks, "qz__-__A.sig");
        File.WriteAllText(unknownSignature, "user signature");
        var results = await paks.InstallModSet([new(_a, Write), new(_b, Write)], Prelude.None).ToListAsync();
        Assert.All(results, result => Assert.True(result.IsRight));
        Assert.Equal("user signature", File.ReadAllText(unknownSignature));
        Assert.All(Reload().ManagedPaks, pak => Assert.True(File.Exists(Path.Combine(Paks, pak.PakFileName))));
        Assert.Equal("qz__-__A (1).pak", Reload().ManagedPaks.Single(pak => pak.Coordinates == _a).PakFileName);
    }

    [Fact]
    public async Task FailedSignatureMoveRollsBackPakRename() {
        var paks = New();
        await Install(paks, _a);
        // Directories are not signature files and cannot be reserved by the allocator.
        var blocked = Path.Combine(Paks, "qz__-__A.sig");
        Directory.CreateDirectory(blocked);
        var results = await paks.InstallModSet([new(_a, Write), new(_b, Write)], Prelude.None).ToListAsync();
        Assert.Contains(results, result => result.IsLeft);
        Assert.True(Directory.Exists(blocked));
        Assert.Equal("q__-__A.pak", Reload().ManagedPaks.Single(pak => pak.Coordinates == _a).PakFileName);
        Assert.All(Reload().ManagedPaks, pak => Assert.True(File.Exists(Path.Combine(Paks, pak.PakFileName))));
    }

    [Fact]
    public async Task DifferentLaunchSetsReuseCommonPaksAndPreserveUnknownFilesAcrossReload() {
        var unknown = Path.Combine(Paks, "q__-__A.pak");
        File.WriteAllText(unknown, "user mod");
        var paks = New();
        await Install(paks, _a, _b);
        paks = Reload();
        await Install(paks, _a);
        await Install(paks, _a);
        Assert.Equal(2, _downloads);
        Assert.Equal("user mod", File.ReadAllText(unknown));
        Assert.Single(Reload().ManagedPaks);
        Assert.Equal(2, Directory.GetFiles(Paks, "*.pak").Length);
    }

    [Fact]
    public async Task AlternatingVersionsReplaceOwnershipWithoutDuplicatesAcrossReload() {
        var newer = _a with { Version = "2" };
        File.WriteAllText(Path.Combine(Paks, "q__-__A.pak"), "requested");
        File.WriteAllText(Path.Combine(Paks, "old-version.pak"), "obsolete");
        var paks = New([new(_a, "q__-__A.pak", 0), new(newer, "old-version.pak", 0)]);
        foreach (var release in new[] { _a, newer, _a }) {
            await Install(paks, release);
            paks = Reload();
            Assert.Equal(release, Assert.Single(paks.ManagedPaks).Coordinates);
            Assert.Single(Directory.GetFiles(Paks, "*.pak"));
        }
        Assert.Equal(2, _downloads);
    }

    [Fact]
    public async Task RealLocalRegistryInstallAllowsImmediateExclusiveOpenAndDelete() {
        var source = Path.Combine(_root, "source.pak");
        File.WriteAllBytes(source, [1, 2, 3]);
        var release = new Release("1", "hash", "source.pak", DateTime.UtcNow,
            new("https://github.com/test/A", "A", "", null, null, [], [], []),
            new([], [], [], [], []), "");
        var registry = new LocalModRegistry(Path.Combine(_root, "registry"));
        await registry.AddRelease(release, source);
        var copiedSource = Path.Combine(registry.RegistryPath, "test", "A", "1", "source.pak");
        Assert.NotEqual(source, copiedSource);
        AssertExclusiveOpen(source);
        AssertExclusiveOpen(copiedSource);
        Stream? acquired = null;
        try {
            var results = await New().InstallModSet([new(_a, path =>
                registry.DownloadPak(_a, path).MapLeft(e => Error.New(e)).Map(writer => {
                    acquired = writer.InputStream;
                    Assert.Equal(copiedSource, Assert.IsType<FileStream>(acquired).Name);
                    AssertExclusiveOpen(source);
                    Assert.Throws<IOException>(() => AssertExclusiveOpen(copiedSource));
                    return writer;
                }))], Prelude.None).ToListAsync();
            Assert.True(Assert.Single(results).IsRight);
            Assert.NotNull(acquired);
            Assert.False(acquired!.CanRead);
            AssertExclusiveOpen(source);
            AssertExclusiveOpen(copiedSource);
            File.Delete(source);
            File.Delete(copiedSource);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Paks, Assert.Single(Reload().ManagedPaks).PakFileName)));
        }
        finally { acquired?.Dispose(); }
    }

    private static void AssertExclusiveOpen(string path) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("read-error")]
    [InlineData("read-cancel")]
    [InlineData("property-throw")]
    [InlineData("publish-throw")]
    public async Task AcquiredInputIsDisposedBeforePublishingResult(string outcome) {
        using var stream = new TrackingStream(outcome);
        var paks = New();
        var results = await paks.InstallModSet([new(_a, path => {
            if (outcome == "publish-throw")
                Directory.CreateDirectory(Path.Combine(Paks, "q__-__A.pak"));
            return new FileWriter(path, stream, 3);
        })], Prelude.None).ToListAsync();
        Assert.Equal(outcome == "success", Assert.Single(results).IsRight);
        Assert.Equal(1, stream.DisposeCount);
        Assert.Empty(Directory.GetFiles(Paks, "*.partial"));
        if (outcome == "success") {
            var owned = Assert.Single(Reload().ManagedPaks);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Paks, owned.PakFileName)));
        }
        else {
            Assert.Empty(Reload().ManagedPaks);
            Assert.Empty(Directory.GetFiles(Paks, "*.pak"));
        }
    }

    [Fact]
    public async Task PartialEnumerationClosesAcquiredInputWithoutAcquiringNext() {
        using var first = new TrackingStream("success");
        using var second = new TrackingStream("success");
        var secondAcquired = false;
        var paks = New();
        await using (var iterator = paks.InstallModSet([
            new(_a, path => new FileWriter(path, first, 3)),
            new(_b, path => {
                secondAcquired = true;
                return new FileWriter(path, second, 3);
            })
        ], Prelude.None).GetAsyncEnumerator()) {
            Assert.True(await iterator.MoveNextAsync());
            Assert.True(iterator.Current.IsRight);
            Assert.Equal(1, first.DisposeCount);
        }
        Assert.False(secondAcquired);
        Assert.Equal(0, second.DisposeCount);
        Assert.Equal(_a, Assert.Single(Reload().ManagedPaks).Coordinates);
        Assert.Empty(Directory.GetFiles(Paks, "*.partial"));
    }

    [Fact]
    public async Task PreCancelledEnumerationDoesNotAcquireInput() {
        var acquired = false;
        var paks = New();
        await using var iterator = paks.InstallModSet([new(_a, path => {
            acquired = true;
            return Write(path);
        })], Prelude.None).GetAsyncEnumerator(new CancellationToken(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await iterator.MoveNextAsync());
        Assert.False(acquired);
        Assert.Empty(Reload().ManagedPaks);
        Assert.Empty(Directory.GetFiles(Paks, "*.partial"));
    }

    [Fact]
    public async Task FileWriterKeepsBorrowedInputOpen() {
        using var stream = new TrackingStream("success");
        var result = await new FileWriter(Path.Combine(_root, "utility.bin"), stream, 3)
            .WriteAsync(Prelude.None, CancellationToken.None);
        Assert.True(result.IsRight);
        Assert.Equal(0, stream.DisposeCount);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task InputRemainsOpenUntilDeferredWriteCompletes() {
        using var stream = new TrackingStream("deferred");
        var installation = New().InstallModSet([new(_a, path => new FileWriter(path, stream, 3))],
            Prelude.None).ToListAsync().AsTask();
        try {
            await stream.ReadStarted.Task;
            Assert.False(installation.IsCompleted);
            Assert.Equal(0, stream.DisposeCount);
            stream.AllowRead.SetResult(true);
            Assert.True(Assert.Single(await installation).IsRight);
            Assert.Equal(1, stream.DisposeCount);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Paks, Assert.Single(Reload().ManagedPaks).PakFileName)));
        }
        finally {
            stream.AllowRead.TrySetResult(true);
            await installation;
        }
    }

    private sealed class TrackingStream(string outcome) : MemoryStream(new byte[] { 1, 2, 3 }) {
        public int DisposeCount { get; private set; }
        public TaskCompletionSource<bool> ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> AllowRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => outcome == "property-throw"
            ? throw new InvalidOperationException("readability failed") : base.CanRead;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            if (outcome == "read-error") throw new IOException("read failed");
            if (outcome == "read-cancel") throw new OperationCanceledException(new CancellationToken(true));
            if (outcome == "deferred") {
                ReadStarted.TrySetResult(true);
                await AllowRead.Task;
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing) {
            if (disposing) DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private sealed class FailedStream : MemoryStream {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("stream failed"));
    }

    public void Dispose() => Directory.Delete(_root, true);
}