using LanguageExt;
using LanguageExt.Common;
using System.Security.Cryptography;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using Xunit;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.CLI.Tests;

public sealed class PakCacheTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pak-cache-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Bytes = [1, 2, 3, 4];
    private static string Hash => Convert.ToHexString(SHA512.HashData(Bytes)).ToLowerInvariant();
    public PakCacheTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ModInstallRequest Request(Func<string, EitherAsync<Error, FileWriter>> writer, string? hash = null) =>
        new(new ReleaseCoordinates("org", "mod", "v1.0.0"), path => writer(path), hash ?? Hash);

    [Fact]
    public async Task ConcurrentInstancesDownloadOnceAndUnlinkIndependently() {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var downloads = 0;
        var request = Request(path => {
            Interlocked.Increment(ref downloads);
            return RightAsync<Error, FileWriter>(new FileWriter(path, new MemoryStream(Bytes), Bytes.Length));
        });
        var first = Path.Combine(_root, "q__first.pak");
        var second = Path.Combine(_root, "r__second.pak");
        var results = await Task.WhenAll(cache.Install(request, first, None).ToEither(), cache.Install(request, second, None).ToEither());
        Assert.All(results, result => Assert.True(result.IsRight, result.ToString()));
        Assert.Equal(1, downloads);
        Assert.Equal(Bytes, File.ReadAllBytes(first));
        File.Delete(first);
        Assert.Equal(Bytes, File.ReadAllBytes(second));
        Assert.Equal(Bytes, File.ReadAllBytes(Path.Combine(cache.Root, Hash, "mod.pak")));
    }

    [Fact]
    public async Task WaitingForAnotherDownloaderCanBeCancelled() {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var directory = Path.Combine(cache.Root, Hash);
        Directory.CreateDirectory(directory);
        using var held = new FileStream(Path.Combine(directory, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var request = Request(_ => throw new InvalidOperationException("Cancelled waiter must not download."));
        var result = await cache.Install(request, Path.Combine(_root, "mod.pak"), None, stop.Token).ToEither()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.IsLeft);
        Assert.False(File.Exists(Path.Combine(_root, "mod.pak")));
    }

    [Fact]
    public async Task SecondInstanceCanLinkWhileFirstPakIsOpenWithoutDeleteSharing() {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var request = Request(path => RightAsync<Error, FileWriter>(new FileWriter(path, new MemoryStream(Bytes), Bytes.Length)));
        var first = Path.Combine(_root, "first.pak");
        Assert.True((await cache.Install(request, first, None)).IsRight);
        using var openPak = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read);
        var second = Path.Combine(_root, "second.pak");
        var result = await cache.Install(request, second, None);
        Assert.True(result.IsRight, result.ToString());
        Assert.Equal(Bytes, File.ReadAllBytes(second));
    }

    [Fact]
    public async Task HashMismatchNeverPublishesCacheOrActiveFile() {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var request = Request(path => RightAsync<Error, FileWriter>(new FileWriter(path, new MemoryStream([9, 9]), 2)));
        var destination = Path.Combine(_root, "mod.pak");
        Assert.True((await cache.Install(request, destination, None)).IsLeft);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(Path.Combine(cache.Root, Hash, "mod.pak")));
        Assert.Empty(Directory.GetFiles(cache.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UnknownTransportSizeStillRequiresAndAcceptsTheRegisteredHash() {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var request = Request(path => RightAsync<Error, FileWriter>(new FileWriter(path, new MemoryStream(Bytes), 0)));
        var destination = Path.Combine(_root, "mod.pak");
        Assert.True((await cache.Install(request, destination, None)).IsRight);
        Assert.Equal(Bytes, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task TruncatedDownloadNeverPublishes() {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var request = Request(path => RightAsync<Error, FileWriter>(new FileWriter(path, new MemoryStream(Bytes), Bytes.Length + 1)));
        Assert.True((await cache.Install(request, Path.Combine(_root, "mod.pak"), None)).IsLeft);
        Assert.False(File.Exists(Path.Combine(cache.Root, Hash, "mod.pak")));
    }

    [Fact]
    public async Task OccupiedActivePathIsNeverOverwritten() {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var request = Request(path => RightAsync<Error, FileWriter>(new FileWriter(path, new MemoryStream(Bytes), Bytes.Length)));
        var destination = Path.Combine(_root, "user.pak");
        File.WriteAllBytes(destination, [9]);
        Assert.True((await cache.Install(request, destination, None)).IsLeft);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CorruptCacheFailsClosedWithoutRedownloadingOrPublishing() {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var directory = Path.Combine(cache.Root, Hash);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "mod.pak"), [9]);
        var request = Request(_ => throw new InvalidOperationException("Must not download over a cache object."));
        Assert.True((await cache.Install(request, Path.Combine(_root, "mod.pak"), None)).IsLeft);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(directory, "mod.pak")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("")]
    [InlineData("abcd")]
    public async Task InvalidHashIsRejectedBeforeDownloading(string hash) {
        var cache = new PakCache(Path.Combine(_root, "cache"));
        var request = Request(_ => throw new InvalidOperationException("Must not download."), hash);
        Assert.True((await cache.Install(request, Path.Combine(_root, "mod.pak"), None)).IsLeft);
    }

    [Fact]
    public void DefaultClientUsesGuiSharedProfileWhileCliCanSelectAnother() {
        var client = CliLaunchOptions.Parse(["client", "--chivalry2-path", _root, "--platform", "Steam"]);
        var separate = CliLaunchOptions.Parse(["client", "--chivalry2-path", _root, "--platform", "Steam", "--saved-dir-suffix", "Another"]);
        Assert.Equal(ProfileAssetPaths.SharedSuffix, client.Shared.SavedDirSuffix);
        Assert.Equal(new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix).Plugins, client.InstancePaths.Plugins);
        Assert.NotEqual(client.InstancePaths.Plugins, separate.InstancePaths.Plugins);
    }

    [Fact]
    public void CacheAndSignaturePathsSurviveMetadataRoundTrip() {
        var directory = Path.Combine(_root, "paks");
        Directory.CreateDirectory(directory);
        var cache = Path.Combine(_root, "cache");
        var signature = Path.Combine(_root, "base.sig");
        var registry = new AggregateModRegistry(System.Array.Empty<IModRegistry>());
        var manager = new ModManager(registry, new PakDir(directory, [], signature, cache), []);
        var codec = new ModManagerCodec(registry);
        var restored = codec.Deserialize(codec.Serialize(manager));
        Assert.True(restored.Success);
        var paks = Assert.IsType<PakDir>(restored.Result!.PakDir);
        Assert.Equal(cache, paks.CachePath);
        Assert.Equal(signature, paks.DefaultSigPath);
    }

    [Fact]
    public void ProfileAssetsAndSharedCachesUseLocalAppData() {
        var first = new ProfileAssetPaths("Host-one", _root);
        var second = new ProfileAssetPaths("Host-two", _root);
        Assert.Equal(Path.Combine(_root, "Chivalry 2", "Saved_Hostone", "Paks"), first.Paks);
        Assert.Equal(first.Paks, new ServerInstancePaths(first.Root, first.PakCache).Paks);
        Assert.Equal(Path.Combine(_root, "Chivalry 2", "Saved_Hostone", "unchained", "plugins"), first.Plugins);
        Assert.NotEqual(first.Paks, second.Paks);
        Assert.Equal(first.PakCache, second.PakCache);
        Assert.Equal(first.DllCache, second.DllCache);
        Assert.Equal(Path.Combine(_root, "Chivalry 2", "Saved_unchained", "Paks"),
            new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix, _root).Paks);
    }
}
