using LanguageExt;
using System.Security.Cryptography;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class DllManagerTests {
    private static DllRelease Release(string id, string version = "v1.0.0", string target = "shared", params Dependency[] dependencies) =>
        new(version, Convert.ToHexString(SHA256.HashData([1, 2, 3])), "Plugin.dll", "https://example.test/plugin.dll", 3,
            DateTimeOffset.UtcNow, new DllInfo {
                RepoUrl = "https://github.com/" + id,
                Name = id,
                Description = "test",
                Authors = ["test"],
                Target = target,
                PluginDependencies = dependencies.ToList()
            }, null);
    private static DllPackage Package(params DllRelease[] releases) => new(releases[0].Info, releases.ToList());

    [Theory]
    [InlineData("^1.0.0", true)]
    [InlineData(">=v1.0.0,<2.0.0", true)]
    [InlineData("==1.0.0", true)]
    [InlineData("!=1.0.0", false)]
    [InlineData("v1.0.0", true)]
    [InlineData(">=2.0.0", false)]
    public void RegistryVersionRequirementsAreEnforced(string requirement, bool matches) =>
        Assert.Equal(matches, DllManager.Satisfies(Release("org/test"), requirement));

    [Fact]
    public void DependenciesAreOrderedBeforeDependentsAndExplicitPinsAreHonored() {
        var core = Release("Chiv2-Community/UnchainedPlugin");
        var dependency = Release("org/dependency");
        var plugin = Release("org/plugin", dependencies: [new Dependency("https://github.com/org/dependency", "^1.0.0")]);
        var newer = plugin with { Tag = "v2.0.0" };
        var resolved = DllManager.Resolve([Package(core), Package(dependency), Package(plugin, newer)],
            [new ReleaseCoordinates("org", "plugin", "v1.0.0")], false, false);
        Assert.Equal(new[] { core, dependency, plugin }, resolved);
    }

    [Fact]
    public void WrongTargetsMissingDependenciesAndCyclesFailClosed() {
        var core = Release("Chiv2-Community/UnchainedPlugin", target: "server");
        Assert.Throws<InvalidOperationException>(() => DllManager.Resolve([Package(core)], [], false, false));
        core = core with { Info = core.Info with { Target = "shared", PluginDependencies = [new Dependency("https://github.com/org/missing", "1.0.0")] } };
        Assert.Throws<InvalidOperationException>(() => DllManager.Resolve([Package(core)], [], false, false));
        var dependency = Release("org/missing", dependencies: [new Dependency("https://github.com/Chiv2-Community/UnchainedPlugin", "1.0.0")]);
        Assert.Throws<InvalidOperationException>(() => DllManager.Resolve([Package(core), Package(dependency)], [], false, false));
    }

    [Fact]
    public void ConflictingPinsAndUnapprovedAutomaticPrereleasesAreRejected() {
        var core = Release("Chiv2-Community/UnchainedPlugin");
        var newer = core with { Tag = "v2.0.0" };
        Assert.Throws<InvalidOperationException>(() => DllManager.Resolve([Package(core, newer)],
            [new ReleaseCoordinates("Chiv2-Community", "UnchainedPlugin", core.Tag),
             new ReleaseCoordinates("Chiv2-Community", "UnchainedPlugin", newer.Tag)], false, false));
        var prerelease = core with { Tag = "v3.0.0-rc.1" };
        Assert.Throws<InvalidOperationException>(() => DllManager.Resolve([Package(prerelease)], [], false, false));
        Assert.Equal(prerelease, Assert.Single(DllManager.Resolve([Package(prerelease)], [], false, true)));
        Assert.Equal(prerelease, Assert.Single(DllManager.Resolve([Package(prerelease)],
            [new ReleaseCoordinates("Chiv2-Community", "UnchainedPlugin", prerelease.Tag)], false, false)));
    }

    [Theory]
    [InlineData("../x.dll")]
    [InlineData("..\\x.dll")]
    [InlineData("C:x.dll")]
    [InlineData("x?.dll")]
    [InlineData("x.exe")]
    public void UnsafeRegistryFileNamesAreRejected(string name) =>
        Assert.Throws<InvalidOperationException>(() => DllManager.SafeFileName(name));

    [Fact]
    public async Task ExistingFilesRequireRegistryHashAndSizeAndInventoryRecordsApprovedSelection() {
        var root = Path.Combine(OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "dll-manager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var release = Release("Chiv2-Community/UnchainedPlugin") with { DllFileName = "UnchainedPlugin.dll" };
            var packagePath = Path.Combine(root, "dll_db", "packages", "Chiv2-Community");
            Directory.CreateDirectory(packagePath);
            File.WriteAllText(Path.Combine(packagePath, "UnchainedPlugin.json"), JsonHelpers.Serialize(Package(release)));
            var registry = new AggregateModRegistry(new LocalModRegistry(root));
            var mods = new ModManager(registry, new PakDir(root, []), []);
            var manager = new DllManager(registry, mods, root);
            var options = new LaunchOptions([], Option<string>.None, "", false, false, Option<string>.None, Option<ServerLaunchOptions>.None);
            var plugin = Path.Combine(root, "UnchainedPlugin.dll");
            await File.WriteAllBytesAsync(plugin, [1, 2, 3]);
            Assert.True((await manager.PrepareLaunch(options)).IsSome);
            Assert.Contains(release.Hash, await File.ReadAllTextAsync(Path.Combine(root, "dll_manager.json")));
            await File.WriteAllBytesAsync(plugin, [3, 2, 1]);
            Assert.False(await DllManager.MatchesFileAsync(plugin, release));
            Assert.False(manager.Inject(new System.Diagnostics.Process()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareLaunch(options));
            Assert.False(manager.Inject(new System.Diagnostics.Process()));
            Assert.Equal(new byte[] { 3, 2, 1 }, await File.ReadAllBytesAsync(plugin));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class DownloadHandler(byte[] bytes) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PakDependenciesAreEnabledOrRejectedWhenExistingSelectionConflicts(bool conflict) {
        var root = Path.Combine(OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "dll-pak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var registryRoot = Path.Combine(root, "registry");
            var plugins = Path.Combine(root, "plugins");
            Directory.CreateDirectory(plugins);
            var release = Release("Chiv2-Community/UnchainedPlugin") with { DllFileName = "UnchainedPlugin.dll" };
            release = release with { Info = release.Info with { ModDependencies = [new Dependency("https://github.com/org/mod", "<2.0.0")] } };
            var packagePath = Path.Combine(registryRoot, "dll_db", "packages", "Chiv2-Community");
            Directory.CreateDirectory(packagePath);
            File.WriteAllText(Path.Combine(packagePath, "UnchainedPlugin.json"), JsonHelpers.Serialize(Package(release)));
            var info = new ModInfo("https://github.com/org/mod", "test", "test", null, null, [], ["test"], []);
            var pakReleases = new[] { "v1.0.0", "v2.0.0" }.Select(tag => new Release(tag, "hash", "mod.pak", DateTime.UtcNow,
                info, new UnchainedLauncher.UnrealModScanner.JsonModels.AssetCollections([], [], [], [], []), null)).ToList();
            var metadata = Path.Combine(registryRoot, "org", "mod");
            Directory.CreateDirectory(metadata);
            File.WriteAllText(Path.Combine(metadata, "mod.json"), JsonHelpers.Serialize(new Mod(info, pakReleases)));
            await File.WriteAllBytesAsync(Path.Combine(plugins, "UnchainedPlugin.dll"), [1, 2, 3]);
            var registry = new AggregateModRegistry(new LocalModRegistry(registryRoot));
            var mods = new ModManager(registry, new PakDir(root, []), conflict ? [new ReleaseCoordinates("org", "mod", "v2.0.0")] : []);
            var manager = new DllManager(registry, mods, plugins);
            var options = new LaunchOptions([], Option<string>.None, "", false, false, Option<string>.None, Option<ServerLaunchOptions>.None);
            if (conflict) await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareLaunch(options));
            else {
                Assert.True((await manager.PrepareLaunch(options)).IsSome);
                Assert.Contains(new ReleaseCoordinates("org", "mod", "v1.0.0"), mods.EnabledModReleaseCoordinates);
                Assert.True((await manager.PrepareLaunch(options)).IsSome); // Already enabled is not an error.
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadsAreVerifiedBeforePublicationAndUnknownRootFilesArePreserved(bool valid) {
        var root = Path.Combine(OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "dll-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var release = Release("Chiv2-Community/UnchainedPlugin") with { DllFileName = "UnchainedPlugin.dll" };
            var packagePath = Path.Combine(root, "dll_db", "packages", "Chiv2-Community");
            Directory.CreateDirectory(packagePath);
            File.WriteAllText(Path.Combine(packagePath, "UnchainedPlugin.json"), JsonHelpers.Serialize(Package(release)));
            var legacy = Path.Combine(root, "UnchainedPlugin.dll");
            await File.WriteAllBytesAsync(legacy, [9, 9, 9]);
            var registry = new AggregateModRegistry(new LocalModRegistry(root));
            var mods = new ModManager(registry, new PakDir(root, []), []);
            using var client = new HttpClient(new DownloadHandler(valid ? [1, 2, 3] : [3, 2, 1]));
            var manager = new DllManager(registry, mods, root, client: client);
            var options = new LaunchOptions([], Option<string>.None, "", true, false, Option<string>.None, Option<ServerLaunchOptions>.None);
            var destination = Path.Combine(root, ".dll-managed", release.Hash.ToLowerInvariant(), release.DllFileName);
            if (valid) {
                Assert.True((await manager.PrepareLaunch(options)).IsSome);
                Assert.True(await DllManager.MatchesFileAsync(destination, release));
                Assert.True(File.Exists(Path.Combine(root, "dll_manager.json")));
            }
            else {
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareLaunch(options));
                Assert.False(File.Exists(destination));
                Assert.False(File.Exists(Path.Combine(root, "dll_manager.json")));
                Assert.False(manager.Inject(new System.Diagnostics.Process()));
            }
            Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(legacy));
            Assert.Empty(Directory.EnumerateFiles(root, ".dll-download-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}