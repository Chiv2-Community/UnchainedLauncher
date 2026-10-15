using System.Text.Json;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry;

public sealed class RegistryConfigurationTests {
    private sealed class Handler : HttpMessageHandler {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(request.RequestUri.AbsoluteUri.EndsWith("plugin_list_index.txt")
                ? System.Net.HttpStatusCode.NotFound : System.Net.HttpStatusCode.OK) {
                RequestMessage = request,
                Content = new StringContent("[]")
            });
        }
    }

    [Fact]
    public void LegacyRegistriesMigrateToRootUrlsAndPreserveAggregateContents() {
        const string legacy = """
            {"Kind":"AggregateModRegistry","Registries":[
                {"Kind":"GithubModRegistry","Org":"Chiv2-Community","RepoName":"C2ModRegistry"},
                {"Kind":"GithubModRegistry","Org":"Other","RepoName":"CustomRegistry"},
                {"Kind":"LocalModRegistry","PakReleaseDir":"local-mods"}
            ]}
            """;
        var codec = ModRegistryCodec.Instance;
        var loaded = codec.Deserialize(legacy);
        Assert.True(loaded.Success);
        var aggregate = Assert.IsType<AggregateModRegistry>(loaded.Result);
        var entries = aggregate.ModRegistries.ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Equal(DllManager.DefaultDatabaseUrl, Assert.IsType<GithubModRegistry>(entries[0]).DllDatabaseUrl);
        Assert.Equal("https://raw.githubusercontent.com/Other/CustomRegistry/refs/heads/main/dll_db",
            Assert.IsType<GithubModRegistry>(entries[1]).DllDatabaseUrl);
        Assert.Equal(Path.GetFullPath("local-mods"), Assert.IsType<LocalModRegistry>(entries[2]).RegistryPath);

        var migrated = codec.Serialize(aggregate);
        using var document = JsonDocument.Parse(migrated);
        foreach (var entry in document.RootElement.GetProperty("Registries").EnumerateArray().Take(2)) {
            Assert.True(entry.TryGetProperty("BaseUrl", out var url));
            Assert.EndsWith("/refs/heads/main", url.GetString());
            Assert.False(entry.TryGetProperty("Org", out _));
            Assert.False(entry.TryGetProperty("RepoName", out _));
        }
        Assert.Equal(migrated, codec.Serialize(codec.Deserialize(migrated).Result!));
    }

    [Fact]
    public async Task MigratedAlternateRegistryDrivesDllRefreshWithoutOfficialRegistryRequests() {
        var directory = Path.Combine(Path.GetTempPath(), "registry-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var registry = Assert.IsType<GithubModRegistry>(ModRegistryCodec.Instance.Deserialize(
                """{"Kind":"GithubModRegistry","Org":"Other","RepoName":"CustomRegistry"}""").Result);
            var handler = new Handler();
            using var http = new HttpClient(handler);
            var manager = new DllManager(Path.Combine(directory, "Plugins"), Path.Combine(directory, "state.json"), http,
                registryUrls: () => [registry.DllDatabaseUrl]);
            await manager.RefreshAsync();
            Assert.Equal(new[] {
                "https://raw.githubusercontent.com/Other/CustomRegistry/refs/heads/main/dll_db/plugin_list_index.txt",
                "https://raw.githubusercontent.com/Other/CustomRegistry/refs/heads/main/dll_db/index.json"
            }, handler.Requests);
            Assert.Empty(manager.Releases);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("main")]
    [InlineData("refs/heads/testing")]
    [InlineData("refs/heads/feature/registry")]
    public void RootConfigurationPreservesRevisionAndDerivesBothDatabases(string revision) {
        var root = $"https://raw.githubusercontent.com/Other/CustomRegistry/{revision}";
        var registry = new GithubModRegistry(root + "/");
        Assert.Equal(root, registry.RegistryBaseUrl);
        Assert.Equal(root + "/package_db", registry.PackageDBBaseUrl);
        Assert.Equal(root + "/dll_db", registry.DllDatabaseUrl);
        var codec = ModRegistryCodec.Instance;
        var restored = Assert.IsType<GithubModRegistry>(codec.Deserialize(codec.Serialize(registry)).Result);
        Assert.Equal(root, restored.RegistryBaseUrl);
        restored.Organization = "NewOrg";
        restored.RepoName = "NewRepo";
        Assert.Equal($"https://raw.githubusercontent.com/NewOrg/NewRepo/{revision}", restored.RegistryBaseUrl);
    }

    [Theory]
    [InlineData("http://raw.githubusercontent.com/Org/Repo/main")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo/main/package_db")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo/main/dll_db")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo/main?token=secret")]
    [InlineData("https://example.test/Org/Repo/main")]
    public void InvalidOrDatabaseSpecificRootsAreRejected(string root) {
        Assert.Throws<InvalidDataException>(() => new GithubModRegistry(root));
    }
}