using System.Net;
using System.Security.Cryptography;
using UnchainedLauncher.Core.Services.Dlls;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class PortableDllPreparationTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinuxPreparationUsesOnlySelectedImmutableObjects(bool cachedUpgrade) {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine("/tmp/opencode", "portable-dll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            byte[] one = [1, 2, 3], two = [4, 5, 6];
            DllRelease Release(string version, byte[] bytes) => new("org", "Plugin", version,
                "https://example.test/Plugin.dll", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
            var v1 = Release("1.0.0", one);
            var v2 = Release("2.0.0", two);
            var plugins = Path.Combine(root, "Plugins");
            var cache = Path.Combine(root, "cache");
            string Cache(DllRelease release) => Path.Combine(cache, release.Sha256, release.EffectiveFileName);
            var handler = new Download(one);
            using var http = new HttpClient(handler);
            DllManager Manager(params DllRelease[] selected) => DllManager.CreateForLaunch(plugins, [v1, v2], selected, http, () => [], cache);
            async Task AssertSelection(DllRelease release, bool downloads) {
                var manager = Manager(release);
                await manager.PrepareAsync(allowDownloads: downloads);
                var files = manager.OpenForInjection();
                try {
                    var dll = Assert.Single(files);
                    Assert.Equal(release.Size, new FileInfo(dll.Path).Length);
                    Assert.Equal(release.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll.Path))).ToLowerInvariant());
                }
                finally { foreach (var dll in files) dll.Dispose(); }
            }
            await AssertSelection(v1, true); // First install.
            await AssertSelection(v1, false); // Same version, no download.
            Assert.Equal(1, handler.Requests);
            // Old fixed active paths, including unknown bytes, are never deleted or injected.
            var old = Path.Combine(plugins, "Managed", "org", "Plugin", v1.EffectiveFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(old)!);
            File.WriteAllBytes(old, one);
            handler.Bytes = [0];
            await Assert.ThrowsAsync<InvalidDataException>(() => Manager(v2).PrepareAsync());
            Assert.Equal(one, File.ReadAllBytes(old));
            Assert.Equal(one, File.ReadAllBytes(Cache(v1)));
            Assert.False(File.Exists(Cache(v2)));
            if (cachedUpgrade) { Directory.CreateDirectory(Path.GetDirectoryName(Cache(v2))!); File.WriteAllBytes(Cache(v2), two); }
            handler.Bytes = two;
            await AssertSelection(v2, !cachedUpgrade);
            var selected = Manager(v2).OpenForInjection();
            try { Assert.Equal(Cache(v2), Assert.Single(selected).Path); }
            finally { foreach (var dll in selected) dll.Dispose(); }
            await AssertSelection(v1, false); // Downgrade.
            Assert.Equal(one, File.ReadAllBytes(old));
            File.WriteAllBytes(old, [9, 9, 9]);
            await AssertSelection(v2, false);
            var disabled = Manager();
            await disabled.PrepareAsync(allowDownloads: false);
            Assert.Empty(disabled.OpenForInjection());
            Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(old));
            Assert.Equal(one, File.ReadAllBytes(Cache(v1)));
            Assert.Equal(two, File.ReadAllBytes(Cache(v2)));
            var outside = Path.Combine(root, "outside.dll");
            File.WriteAllBytes(outside, [8]);
            File.Delete(old);
            File.CreateSymbolicLink(old, outside);
            await AssertSelection(v2, false);
            await Manager().PrepareAsync(allowDownloads: false);
            Assert.NotNull(new FileInfo(old).LinkTarget);
            Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(outside));
            File.WriteAllBytes(Cache(v2), [9]);
            await Assert.ThrowsAsync<InvalidDataException>(() => Manager(v2).PrepareAsync());
            Assert.Throws<InvalidDataException>(() => Manager(v2).OpenForInjection());
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Cache(v2)));
            Assert.Equal(one, File.ReadAllBytes(Cache(v1)));
            Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(old));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Download(byte[] bytes) : HttpMessageHandler {
        public byte[] Bytes = bytes;
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(Bytes) });
        }
    }
}
