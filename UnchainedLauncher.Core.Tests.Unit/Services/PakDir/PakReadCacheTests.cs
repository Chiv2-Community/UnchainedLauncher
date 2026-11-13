using log4net;
using log4net.Appender;
using log4net.Core;
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

    private static async Task WithHashLogs(Func<MemoryAppender, Task> test) {
        var logger = (log4net.Repository.Hierarchy.Logger)LogManager.GetLogger(nameof(PakReadCache)).Logger;
        var repository = logger.Repository!;
        var appender = new MemoryAppender();
        var level = logger.Level;
        var configured = repository.Configured;
        logger.Level = Level.Info;
        repository.Configured = true;
        logger.AddAppender(appender);
        try {
            await test(appender);
        }
        finally {
            logger.RemoveAppender(appender);
            logger.Level = level;
            repository.Configured = configured;
            appender.Close();
        }
    }

    [Fact]
    public async Task HashLogsStartBeforeReadingAndCompletionWithDigest() {
        await WithHashLogs(async appender => {
            using var cache = new PakReadCache(directory, readHash: async stream => {
                var started = Assert.Single(appender.GetEvents());
                Assert.Equal(Level.Info, started.Level);
                Assert.Contains($"Hashing pak '{Pak}' (8 bytes) using SHA-512.", started.RenderedMessage);
                return await SHA512.HashDataAsync(stream);
            });
            var hash = await cache.Hash(Pak);
            var logs = appender.GetEvents();
            Assert.Equal(2, logs.Length);
            Assert.Equal(Level.Info, logs[1].Level);
            Assert.Contains($"Finished hashing pak '{Pak}' (8 bytes) in ", logs[1].RenderedMessage);
            Assert.Contains($" ms; SHA-512: {hash}", logs[1].RenderedMessage);
            Assert.Equal(Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(Pak))), hash);
        });
    }

    [Fact]
    public async Task CacheHitLogsReuseWithoutReadingAgain() {
        await WithHashLogs(async appender => {
            using (var cache = new PakReadCache(directory)) await cache.Hash(Pak);
            appender.Clear();
            using var restarted = new PakReadCache(directory, readHash: _ => throw new InvalidOperationException("Must not read cached pak"));
            using var exclusive = new FileStream(Pak, FileMode.Open, FileAccess.Read, FileShare.None);
            await restarted.Hash(Pak);
            var reused = Assert.Single(appender.GetEvents());
            Assert.Equal(Level.Info, reused.Level);
            Assert.Equal($"Using cached SHA-512 hash for pak '{Pak}' (8 bytes).", reused.RenderedMessage);
        });
    }

    [Fact]
    public async Task FailedHashLogsExceptionAndRethrowsWithoutCaching() {
        await WithHashLogs(async appender => {
            var failure = new IOException("Hash read failed");
            using (var cache = new PakReadCache(directory, readHash: _ => throw failure)) {
                Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => cache.Hash(Pak)));
            }
            var logs = appender.GetEvents();
            Assert.Equal(2, logs.Length);
            Assert.Equal(Level.Error, logs[1].Level);
            Assert.Same(failure, logs[1].ExceptionObject);
            Assert.Contains($"Failed to hash pak '{Pak}' (8 bytes) after ", logs[1].RenderedMessage);
            Assert.EndsWith(" ms.", logs[1].RenderedMessage);
            using var restarted = new PakReadCache(directory);
            Assert.Equal(Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(Pak))), await restarted.Hash(Pak));
        });
    }

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