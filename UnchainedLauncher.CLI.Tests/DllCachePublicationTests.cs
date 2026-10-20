using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using UnchainedLauncher.Core.Services.Dlls;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class DllCachePublicationTests {
    [Theory]
    [InlineData("valid")]
    [InlineData("size")]
    [InlineData("hash")]
    [InlineData("directory")]
    public async Task DownloadValidatesConcurrentCacheWinnerWithoutOverwritingIt(string winnerKind) {
        var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ??
            (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "dll-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] bytes = [1, 2, 3];
        var release = new DllRelease("org", "Plugin", "1.0.0", "https://example.test/Plugin.dll",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            using var http = new HttpClient(new PausedDownload(bytes, started, finish));
            var cacheRoot = Path.Combine(root, "cache");
            var plugins = Path.Combine(root, "Plugins");
            var manager = DllManager.CreateForLaunch(plugins, [release], [release], http, () => [], cacheRoot);
            var preparation = manager.PrepareAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var source = Path.Combine(root, "source.dll");
            File.WriteAllBytes(source, bytes);
            var cache = Path.Combine(cacheRoot, release.Sha256, release.EffectiveFileName);
            // Exercise the real portable publisher; native archival uses this publication boundary on Windows.
            var copy = typeof(DllManager).GetMethod("CopyValidated", BindingFlags.NonPublic | BindingFlags.Static)!;
            copy.Invoke(null, [source, cache, release, false, null, null, true]);
            if (winnerKind == "size") File.WriteAllBytes(cache, [9]);
            if (winnerKind == "hash") File.WriteAllBytes(cache, [9, 8, 7]);
            if (winnerKind == "directory") { File.Delete(cache); Directory.CreateDirectory(cache); }
            var winner = winnerKind == "directory" ? null : File.ReadAllBytes(cache);
            finish.SetResult();
            if (winnerKind == "valid") {
                await preparation.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories))));
            }
            else {
                if (winnerKind == "directory") {
                    var error = await Assert.ThrowsAnyAsync<Exception>(() => preparation.WaitAsync(TimeSpan.FromSeconds(10)));
                    Assert.True(error is IOException or UnauthorizedAccessException, error.ToString());
                }
                else
                    await Assert.ThrowsAsync<InvalidDataException>(() => preparation.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.False(Directory.Exists(plugins));
            }
            if (winnerKind == "directory") Assert.True(Directory.Exists(cache));
            else Assert.Equal(winner, File.ReadAllBytes(cache));
            Assert.Equal(bytes, File.ReadAllBytes(source));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally { finish.TrySetResult(); Directory.Delete(root, true); }
    }

    private sealed class PausedDownload(byte[] bytes, TaskCompletionSource started, TaskCompletionSource finish) : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            started.SetResult();
            await finish.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) };
        }
    }
}
