using System.Security.Cryptography;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

public class PakReadCacheTests : IDisposable {
    private readonly string directory = Path.Combine("TestDirectories", nameof(PakReadCacheTests), Guid.NewGuid().ToString());
    private string Pak => Path.Combine(directory, "manual.pak");
    private string Cache => Path.Combine(directory, ".unchained", "pak-read-cache.json");

    public PakReadCacheTests() {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Pak, "original");
    }

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public async Task UnchangedHashAcrossInstancesDoesNotOpenPak() {
        string hash;
        using (var cache = new PakReadCache(directory)) {
            hash = await cache.Hash(Pak);
            using var locked = new FileStream(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.Equal(hash, await cache.Hash(Pak));
        }
        using var exclusive = new FileStream(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
        using var restarted = new PakReadCache(directory);
        Assert.Equal(hash, await restarted.Hash(Pak));
        restarted.Validate(Pak, hash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModificationTimeAndLengthIndependentlyInvalidate(bool sizeOnly) {
        string original;
        using (var cache = new PakReadCache(directory)) original = await cache.Hash(Pak);
        var timestamp = File.GetLastWriteTimeUtc(Pak);
        File.WriteAllText(Pak, sizeOnly ? "longer replacement" : "modified");
        File.SetLastWriteTimeUtc(Pak, sizeOnly ? timestamp : timestamp.AddMinutes(1));
        using var restarted = new PakReadCache(directory);
        Assert.Throws<IOException>(() => restarted.Validate(Pak, original));
        Assert.NotEqual(original, await restarted.Hash(Pak));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1.2.3")]
    public async Task SuccessfulVersionResultIncludingUnknownPersistsUntilFileChanges(string? version) {
        var reads = 0;
        Task<string?> Read(string _) { reads++; return Task.FromResult(version); }
        using (var cache = new PakReadCache(directory, Read))
            Assert.Equal(version, await cache.Version(Pak, await cache.Hash(Pak)));
        using (var cache = new PakReadCache(directory, Read))
            Assert.Equal(version, await cache.Version(Pak, await cache.Hash(Pak)));
        Assert.Equal(1, reads);
        File.AppendAllText(Pak, " changed");
        using (var cache = new PakReadCache(directory, Read))
            Assert.Equal(version, await cache.Version(Pak, await cache.Hash(Pak)));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task FailedVersionReadIsRetriedAfterRestart() {
        using (var cache = new PakReadCache(directory, _ => throw new IOException("transient")))
            Assert.Null(await cache.Version(Pak, await cache.Hash(Pak)));
        using var restarted = new PakReadCache(directory, _ => Task.FromResult<string?>("2.0.0"));
        Assert.Equal("2.0.0", await restarted.Version(Pak, await restarted.Hash(Pak)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangesDuringReadAreNotCachedOrUsed(bool duringVersion) {
        string? stale = null;
        using (var cache = new PakReadCache(directory,
            _ => {
                File.AppendAllText(Pak, " changed");
                return Task.FromResult<string?>("9.0.0");
            }, async stream => {
                var hash = await SHA512.HashDataAsync(stream);
                stale = Convert.ToHexString(hash);
                if (!duringVersion) {
                    stream.Dispose();
                    File.AppendAllText(Pak, " changed");
                }
                return hash;
            })) {
            if (duringVersion) {
                var hash = await cache.Hash(Pak);
                await Assert.ThrowsAsync<IOException>(() => cache.Version(Pak, hash));
            }
            else await Assert.ThrowsAsync<IOException>(() => cache.Hash(Pak));
        }
        using var restarted = new PakReadCache(directory, _ => Task.FromResult<string?>("1.0.0"));
        var fresh = await restarted.Hash(Pak);
        Assert.NotEqual(stale, fresh);
        Assert.Equal("1.0.0", await restarted.Version(Pak, fresh));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("Hash")]
    [InlineData("Length")]
    [InlineData("VersionRead")]
    public async Task CorruptOrIncompleteEntriesAreMisses(string field) {
        using (var cache = new PakReadCache(directory)) await cache.Hash(Pak);
        var json = JsonNode.Parse(File.ReadAllText(Cache))!;
        if (field == "corrupt") File.WriteAllText(Cache, "{invalid");
        else {
            json["manual.pak"]!.AsObject().Remove(field);
            File.WriteAllText(Cache, json.ToJsonString());
        }
        var reads = 0;
        using var restarted = new PakReadCache(directory, readHash: async stream => {
            reads++;
            return await SHA512.HashDataAsync(stream);
        });
        Assert.Equal(Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(Pak))), await restarted.Hash(Pak));
        Assert.Equal(1, reads);
    }
}