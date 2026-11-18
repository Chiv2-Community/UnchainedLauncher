using System.Text.Json;
using DiscriminatedUnions;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;

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
    public void ReleasedRegistriesPreserveShapeAndAggregateContents() {
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
        var released = Assert.IsType<ReleasedAggregate>(TypedJsonCodec.Derive<ReleasedMetadata>().Deserialize(migrated).Result);
        Assert.Equal(new ReleasedGithub("Chiv2-Community", "C2ModRegistry"), released.Registries[0]);
        Assert.Equal(new ReleasedGithub("Other", "CustomRegistry"), released.Registries[1]);
        Assert.Equal(new ReleasedLocal(Path.GetFullPath("local-mods")), released.Registries[2]);
        Assert.False(document.RootElement.GetProperty("Registries")[0].TryGetProperty("BaseUrl", out _));
        Assert.Equal(migrated, codec.Serialize(codec.Deserialize(migrated).Result!));
    }

    // Exact v1.0.2 DTO fields and union tags, independent of the current metadata types.
    [UnionTag(nameof(Kind))]
    [UnionCase(typeof(ReleasedLocal), "LocalModRegistry")]
    [UnionCase(typeof(ReleasedGithub), "GithubModRegistry")]
    [UnionCase(typeof(ReleasedAggregate), "AggregateModRegistry")]
    public abstract record ReleasedMetadata(string Kind);
    public record ReleasedLocal(string PakReleaseDir) : ReleasedMetadata("LocalModRegistry");
    public record ReleasedGithub(string Org, string RepoName) : ReleasedMetadata("GithubModRegistry");
    public record ReleasedAggregate(ReleasedMetadata[] Registries) : ReleasedMetadata("AggregateModRegistry");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultGithubEmitsExactReleasedPayload(bool explicitRoot) {
        var registry = explicitRoot
            ? new GithubModRegistry("https://raw.githubusercontent.com/Other/CustomRegistry/refs/heads/main/")
            : new GithubModRegistry("Other", "CustomRegistry");
        registry.Organization = "UpdatedOrg";
        registry.RepoName = "UpdatedRepo";
        var json = ModRegistryCodec.Instance.Serialize(registry);
        var released = new ReleasedGithub("UpdatedOrg", "UpdatedRepo");
        Assert.Equal(TypedJsonCodec.Derive<ReleasedMetadata>().Serialize(released), json);
        Assert.Equal(released, TypedJsonCodec.Derive<ReleasedMetadata>().Deserialize(json).Result);
    }

    [Fact]
    public void OldMainDefaultBaseUrlReserializesToReleasedPayload() {
        const string oldMain = """
            {"Kind":"GithubModRegistry","BaseUrl":"https://raw.githubusercontent.com/Other/CustomRegistry/refs/heads/main"}
            """;
        var loaded = ModRegistryCodec.Instance.Deserialize(oldMain);
        Assert.True(loaded.Success);
        Assert.Equal(TypedJsonCodec.Derive<ReleasedMetadata>().Serialize(new ReleasedGithub("Other", "CustomRegistry")),
            ModRegistryCodec.Instance.Serialize(loaded.Result!));
    }

    [Fact]
    public void ManagerConfigurationRetainsModernPluginAndModMetadata() {
        var directory = Path.Combine(Path.GetTempPath(), "registry-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var modDependency = new Dependency("https://github.com/Other/Dependency", ">=1.0.0");
            var pluginDependency = new Dependency("https://github.com/Other/Plugin", ">=2.0.0");
            var info = new ModInfo("https://github.com/Other/Mod", "Mod", "Description", null, null, [], [],
                ModDependencies: [modDependency], PluginDependencies: [pluginDependency]);
            var release = new Release("v1.0.0", "hash", "mod.pak", new DateTime(2026, 1, 1), info,
                new AssetCollections([], [], [], [], []), "notes");
            var metadata = new StandardModManagerMetadata([new ReleaseCoordinates("Other", "Mod", "v1.0.0")],
                [new Mod(info, [release])], new PakDirMetadata(directory, []));
            var codec = new ModManagerCodec(new GithubModRegistry("Other", "Registry"));
            var loaded = codec.Deserialize(JsonHelpers.Serialize<ModManagerMetadata>(metadata));
            Assert.True(loaded.Success);
            Assert.False(loaded.Result!.NeedsMetadataRefresh);
            var restored = Assert.IsType<StandardModManagerMetadata>(
                JsonHelpers.Deserialize<ModManagerMetadata>(codec.Serialize(loaded.Result)).Result);
            var cached = Assert.Single(restored.CachedMods!);
            foreach (var retained in new[] { cached.LatestReleaseInfo, Assert.Single(cached.Releases).Info }) {
                Assert.Null(retained.Dependencies);
                Assert.Equal(new[] { modDependency }, retained.EffectiveModDependencies);
                Assert.Equal(new[] { pluginDependency }, retained.EffectivePluginDependencies);
            }
            Assert.Single(restored.EnabledModReleases);
        }
        finally { Directory.Delete(directory, true); }
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
    [InlineData("refs/tags/main")]
    [InlineData("refs/tags/v1.0.0")]
    [InlineData("refs/heads/testing")]
    [InlineData("refs/heads/feature/registry")]
    public void RootConfigurationPreservesRevisionAndDerivesBothDatabases(string revision) {
        var root = $"https://raw.githubusercontent.com/Other/CustomRegistry/{revision}";
        var registry = new GithubModRegistry(root + "/");
        Assert.Equal(root, registry.RegistryBaseUrl);
        Assert.Equal(root + "/package_db", registry.PackageDBBaseUrl);
        Assert.Equal(root + "/dll_db", registry.DllDatabaseUrl);
        var codec = ModRegistryCodec.Instance;
        var json = codec.Serialize(registry);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "BaseUrl", "Kind" }, document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(root, document.RootElement.GetProperty("BaseUrl").GetString());
        var restored = Assert.IsType<GithubModRegistry>(codec.Deserialize(json).Result);
        Assert.Equal(root, restored.RegistryBaseUrl);
        Assert.Equal(root + "/package_db", restored.PackageDBBaseUrl);
        Assert.Equal(root + "/dll_db", restored.DllDatabaseUrl);
        restored.Organization = "NewOrg";
        restored.RepoName = "NewRepo";
        Assert.Equal($"https://raw.githubusercontent.com/NewOrg/NewRepo/{revision}", restored.RegistryBaseUrl);
    }

    [Fact]
    public void BaseUrlTakesPrecedenceOverReleasedIdentityWithinNestedAggregate() {
        const string json = """
            {"Kind":"AggregateModRegistry","Registries":[
              {"Kind":"LocalModRegistry","PakReleaseDir":"local-mods"},
              {"Kind":"AggregateModRegistry","Registries":[
                {"Kind":"GithubModRegistry","Org":"Ignored","RepoName":"Ignored",
                 "BaseUrl":"https://raw.githubusercontent.com/Other/CustomRegistry/refs/tags/v1.0.0"},
                {"Kind":"GithubModRegistry","Org":"DefaultOrg","RepoName":"DefaultRepo"}]}]}
            """;
        var codec = ModRegistryCodec.Instance;
        var aggregate = Assert.IsType<AggregateModRegistry>(codec.Deserialize(json).Result);
        var serialized = codec.Serialize(aggregate);
        var restored = Assert.IsType<AggregateModRegistry>(codec.Deserialize(serialized).Result);
        Assert.Equal(Path.GetFullPath("local-mods"), Assert.IsType<LocalModRegistry>(restored.ModRegistries.First()).RegistryPath);
        // Aggregate construction already flattens nested input; preserve that behavior and order.
        Assert.Equal(3, restored.ModRegistries.Count);
        var custom = Assert.IsType<GithubModRegistry>(restored.ModRegistries[1]);
        Assert.Equal("https://raw.githubusercontent.com/Other/CustomRegistry/refs/tags/v1.0.0/package_db", custom.PackageDBBaseUrl);
        Assert.Equal("https://raw.githubusercontent.com/Other/CustomRegistry/refs/tags/v1.0.0/dll_db", custom.DllDatabaseUrl);
        Assert.Equal("https://raw.githubusercontent.com/DefaultOrg/DefaultRepo/refs/heads/main",
            Assert.IsType<GithubModRegistry>(restored.ModRegistries[2]).RegistryBaseUrl);
        Assert.Equal(serialized, codec.Serialize(restored));
    }

    [Theory]
    [InlineData("http://raw.githubusercontent.com/Org/Repo/main")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo/main/package_db")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo/main/dll_db")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo/main?token=secret")]
    [InlineData("https://example.test/Org/Repo/main")]
    [InlineData("https://raw.githubusercontent.com:444/Org/Repo/main")]
    [InlineData("https://user@raw.githubusercontent.com/Org/Repo/main")]
    [InlineData("https://raw.githubusercontent.com/Org/Repo/main#fragment")]
    public void InvalidOrDatabaseSpecificRootsAreRejected(string root) {
        Assert.Throws<InvalidDataException>(() => new GithubModRegistry(root));
        var json = JsonSerializer.Serialize(new {
            Kind = "GithubModRegistry", Org = "Valid", RepoName = "Valid", BaseUrl = root
        });
        Assert.Throws<InvalidDataException>(() => ModRegistryCodec.Instance.Deserialize(json));
    }
}
