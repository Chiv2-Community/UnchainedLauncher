using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ModQueryTests : IDisposable {
    private readonly string _directory = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "mod-query-" + Guid.NewGuid().ToString("N"));
    public ModQueryTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ModQueryOptions Parse(params string[] args) => Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(["mod-query", .. args]));

    [Fact]
    public void QueriesNeedNoGameInstallationAndAlwaysIncludeOfficialRegistry() {
        var query = Parse("list");
        var official = Assert.IsType<GithubModRegistry>(Assert.Single(query.RegistryOptions.CreateRegistry().ModRegistries));
        Assert.Equal("Chiv2-Community", official.Organization);
        Assert.Equal("C2ModRegistry", official.RepoName);
    }

    [Fact]
    public void QueryParsingUsesOneLibraryParseAndSupportsRepeatedRegistryOptions() {
        var calls = 0;
        var args = new List<string> { "mod-query", "get", "org/mod", "--version=v1.0.0" };
        for (var i = 0; i < 12; i++) args.Add($"--add-registry=github:owner/repo{i}");
        var query = Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(args.ToArray(), (root, tokens, config) => {
            calls++;
            return root.Parse(tokens, config);
        }));
        Assert.Equal(1, calls);
        Assert.Equal("v1.0.0", query.Version);
        Assert.Equal("org", query.Mod!.Org);
        Assert.Equal(13, query.RegistryOptions.CreateRegistry().ModRegistries.Count);
    }

    [Fact]
    public void RegistryYamlUsesRelativePathsAndIgnoresNonQuerySettings() {
        var local = Path.Combine(_directory, "local");
        Directory.CreateDirectory(local);
        var path = Path.Combine(_directory, "launch.yaml");
        File.WriteAllText(path, """
            platform: invalid
            chiv2-path: null
            mod-setup:
              registries: ['local:local']
              mods: {ignored: invalid-selection}
              dlls: not-a-list
            server: {name: one, name: two}
            client: not-a-mapping
            """);
        var query = Parse("get", "org/mod", "--config-path", path, "--version", "v1.0.0", "--add-registry=github:ignored/repo");
        Assert.Equal("v1.0.0", query.Version);
        Assert.Equal(local, Assert.Single(query.RegistryOptions.Registries).Location);
        Assert.Equal(2, query.RegistryOptions.CreateRegistry().ModRegistries.Count);
        File.WriteAllText(path, "mod-setup: {registries: ['local:local']}\nmod-setup: {}\n");
        Assert.Throws<ArgumentException>(() => Parse("list", "--config-path", path));
    }

    [Fact]
    public void CoreRegistryConfigurationAndQueryOptionsWorkBeforeSubcommands() {
        Directory.CreateDirectory(Path.Combine(_directory, "local"));
        var path = Path.Combine(_directory, "registries.json");
        File.WriteAllText(path, JsonHelpers.Serialize<ModRegistryMetadata>(new AggregateModRegistryMetadata([
            new LocalModRegistryMetadata("local"), new GithubModRegistryMetadata("owner", "repo")
        ])));
        var query = Parse("--add-registry", "config:" + path, "versions", "org/mod");
        var registry = query.RegistryOptions.CreateRegistry();
        Assert.Equal(3, registry.ModRegistries.Count);
        Assert.Equal(Path.Combine(_directory, "local"), Assert.Single(registry.ModRegistries.OfType<LocalModRegistry>()).RegistryPath);
    }

    [Fact]
    public async Task QueriesHonorCancellation() {
        var registry = CreateRegistry();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ModQuery.RunAsync(Parse("get", "org/mod"), registry, new StringWriter(), new StringWriter(), stop.Token));
    }

    [Theory]
    [InlineData("mod-query")]
    [InlineData("mod-query", "get")]
    [InlineData("mod-query", "versions", "bad-id")]
    [InlineData("mod-query", "get", "org/mod:v1")]
    [InlineData("mod-query", "get", "org/mod", "--version=")]
    [InlineData("mod-query", "get", "org/mod", "--version=a", "--version=b")]
    [InlineData("mod-query", "list", "--version=v1")]
    [InlineData("mod-query", "list", "--config-path")]
    [InlineData("mod-query", "list", "--config-path=a", "--config-path=b")]
    [InlineData("mod-query", "list", "--", "game-args")]
    public void InvalidQueryInputIsRejected(params string[] args) => Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(args));

    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("versions")]
    public async Task HelpDoesNotAccessRegistries(string operation) {
        var result = CommandLineOptions.Parse(["mod-query", operation, "--help"]);
        Assert.True(result.Help);
        Assert.Contains("--add-registry", result.HelpText);
        Assert.Contains("--config-path", result.HelpText);
        Assert.Equal(0, await Program.Main(["mod-query", operation, "--help"]));
    }

    private LocalModRegistry CreateRegistry(bool stable = true) {
        var info = new ModInfo("https://github.com/org/mod", "Display name", "Description", "https://example.test", null, [], ["Author"],
            [new Dependency("https://github.com/org/dependency", ">=1.0.0")]);
        var manifest = new AssetCollections([], [], [new MapDto("/Game/Maps/Test", "map-hash", null, null, "FFA", "Test Map", null, null, null, null, null)], [], []);
        var versions = stable ? new[] { "v1.0.0", "v2.0.0", "v3.0.0-beta.1" } : new[] { "v3.0.0-beta.1" };
        var releases = versions.Select((tag, index) => new Release(tag, "sha512-" + tag, "mod.pak", new DateTime(2026, 1, index + 1, 0, 0, 0, DateTimeKind.Utc), info, manifest, "Release notes")).ToList();
        var metadataPath = Path.Combine(_directory, "org", "mod");
        Directory.CreateDirectory(metadataPath);
        File.WriteAllText(Path.Combine(metadataPath, "mod.json"), JsonHelpers.Serialize(new Mod(info, releases)));
        foreach (var release in releases) {
            var pakDirectory = Path.Combine(metadataPath, release.Tag);
            Directory.CreateDirectory(pakDirectory);
            File.WriteAllBytes(Path.Combine(pakDirectory, release.PakFileName), new byte[123]);
        }
        return new LocalModRegistry(_directory);
    }

    [Fact]
    public async Task ListShowsIdNameAndLatestStableWithoutChangingFiles() {
        var registry = CreateRegistry();
        var files = Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Order().ToArray();
        var output = new StringWriter();
        Assert.Equal(0, await ModQuery.RunAsync(Parse("list"), registry, output, new StringWriter(), CancellationToken.None));
        Assert.Contains("org/mod\tDisplay name\tv2.0.0", output.ToString());
        Assert.Equal(files, Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Order());
    }

    [Theory]
    [InlineData(null, "v2.0.0")]
    [InlineData("v1.0.0", "v1.0.0")]
    [InlineData("v3.0.0-beta.1", "v3.0.0-beta.1")]
    public async Task GetShowsSelectedReleaseAndManifestAndSizeWithoutDownloading(string? version, string expected) {
        var registry = new AggregateModRegistry(CreateRegistry());
        var query = version is null ? Parse("get", "org/mod") : Parse("get", "org/mod", "--version", version);
        var output = new StringWriter();
        Assert.Equal(0, await ModQuery.RunAsync(query, registry, output, new StringWriter(), CancellationToken.None));
        var text = output.ToString();
        Assert.Contains("Version: " + expected, text);
        Assert.Contains("Pak hash (SHA-512): sha512-" + expected, text);
        Assert.Contains("Pak file: mod.pak", text);
        Assert.Contains("Size: 123 bytes", text);
        Assert.Contains("Dependencies (1):", text);
        Assert.Contains("org/dependency: >=1.0.0", text);
        Assert.Contains("Maps (1):", text);
        Assert.Contains("Test Map | /Game/Maps/Test", text);
        Assert.Contains("Author", text);
        Assert.Contains("Release notes", text);
    }

    [Fact]
    public async Task VersionsIncludePrereleasesAndUnavailableIdsFailClearly() {
        var registry = CreateRegistry();
        var output = new StringWriter();
        Assert.Equal(0, await ModQuery.RunAsync(Parse("versions", "org/mod"), registry, output, new StringWriter(), CancellationToken.None));
        foreach (var version in new[] { "v1.0.0", "v2.0.0", "v3.0.0-beta.1" }) Assert.Contains(version, output.ToString());
        Assert.Equal(1, await ModQuery.RunAsync(Parse("get", "org/mod", "--version", "missing"), registry, new StringWriter(), new StringWriter(), CancellationToken.None));
        Assert.Equal(1, await ModQuery.RunAsync(Parse("get", "org/unknown"), registry, new StringWriter(), new StringWriter(), CancellationToken.None));
    }

    [Fact]
    public async Task PrereleaseOnlyModCanBeQueriedAndMissingPakSizeRemainsUnknown() {
        var registry = CreateRegistry(stable: false);
        File.Delete(Path.Combine(_directory, "org", "mod", "v3.0.0-beta.1", "mod.pak"));
        var output = new StringWriter();
        Assert.Equal(0, await ModQuery.RunAsync(Parse("get", "org/mod"), registry, output, new StringWriter(), CancellationToken.None));
        Assert.Contains("Version: v3.0.0-beta.1", output.ToString());
        Assert.Contains("Size: unknown", output.ToString());
    }

    [Fact]
    public async Task PartialCatalogKeepsRowsButReportsFailureAndDuplicateModsAreMerged() {
        var local = CreateRegistry();
        var registry = new AggregateModRegistry(local, local, new UnavailableRegistry());
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(1, await ModQuery.RunAsync(Parse("list"), registry, output, error, CancellationToken.None));
        Assert.Single(output.ToString().Split('\n').Where(line => line.StartsWith("org/mod\t")));
        Assert.Contains("registry unavailable", error.ToString());
    }

    [Fact]
    public async Task EmptyReleaseCatalogIsHandledWithoutInventingAVersion() {
        var registry = CreateRegistry();
        var path = Path.Combine(_directory, "org", "mod", "mod.json");
        var mod = await registry.GetMod(new ModIdentifier("org", "mod")).Match(value => value, _ => throw new Exception());
        File.WriteAllText(path, JsonHelpers.Serialize(mod with { Releases = [] }));
        var output = new StringWriter();
        Assert.Equal(0, await ModQuery.RunAsync(Parse("list"), registry, output, new StringWriter(), CancellationToken.None));
        Assert.Contains("org/mod\tDisplay name\tnone", output.ToString());
        Assert.Equal(1, await ModQuery.RunAsync(Parse("get", "org/mod"), registry, new StringWriter(), new StringWriter(), CancellationToken.None));
    }

    private sealed class UnavailableRegistry : IModRegistry {
        public string Name => "unavailable";
        private static RegistryMetadataException Error => RegistryMetadataException.PackageListRetrieval("registry unavailable", LanguageExt.Prelude.None);
        public Task<GetAllModsResult> GetAllMods() => Task.FromResult(new GetAllModsResult([Error], []));
        public LanguageExt.EitherAsync<RegistryMetadataException, Mod> GetMod(ModIdentifier id) => LanguageExt.Prelude.LeftAsync<RegistryMetadataException, Mod>(Error);
        public LanguageExt.EitherAsync<RegistryMetadataException, Release> GetModRelease(ReleaseCoordinates coordinate) => LanguageExt.Prelude.LeftAsync<RegistryMetadataException, Release>(Error);
        public LanguageExt.EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinate, string output) => throw new InvalidOperationException("Queries must never download paks.");
    }
}
