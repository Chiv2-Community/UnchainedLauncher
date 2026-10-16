using LanguageExt;
using System.Security.Cryptography;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using Xunit;
using DllManager = UnchainedLauncher.Core.Services.Mods.DllLaunchSession;

namespace UnchainedLauncher.CLI.Tests;

public sealed class DllManagerTests {
    private static DllRelease Release(string id, string version = "v1.0.0", string target = "shared", params Dependency[] dependencies) =>
        new(version, Convert.ToHexString(SHA256.HashData([1, 2, 3])).ToLowerInvariant(), "Plugin.dll", "https://example.test/plugin.dll", 3,
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependencyPinsAreHonoredInEitherRequestOrder(bool dependencyFirst) {
        var core = Release("Chiv2-Community/UnchainedPlugin");
        var older = Release("org/dependency");
        var newer = older with { Tag = "v2.0.0" };
        var plugin = Release("org/plugin", dependencies: [new Dependency("https://github.com/org/dependency", ">=1.0.0")]);
        ReleaseCoordinates[] roots = [new("org", "plugin", plugin.Tag), new("ORG", "Dependency", older.Tag)];
        if (dependencyFirst) Array.Reverse(roots);
        var resolved = DllManager.Resolve([Package(core), Package(older, newer), Package(plugin)], roots, false, false);
        Assert.Equal(new[] { core, older, plugin }, resolved);
    }

    [Theory]
    [InlineData(false, "v2.0.0", "shared", ">=1.0.0", false, false)]
    [InlineData(true, "v2.0.0", "shared", ">=1.0.0", false, false)]
    [InlineData(false, "latest", "shared", ">=1.0.0", false, true)]
    [InlineData(true, "latest", "shared", ">=1.0.0", false, true)]
    [InlineData(false, "v1.0.0", "shared", ">=2.0.0", false, false)]
    [InlineData(true, "v1.0.0", "shared", ">=2.0.0", false, false)]
    [InlineData(false, "v1.0.0", "server", ">=1.0.0", false, false)]
    [InlineData(true, "v1.0.0", "server", ">=1.0.0", false, false)]
    [InlineData(false, "v1.0.0", "server", ">=1.0.0", true, true)]
    [InlineData(true, "v1.0.0", "server", ">=1.0.0", true, true)]
    public void IndexedPinsStillEnforceConflictsRequirementsAndTargets(bool reverse, string otherPin,
        string target, string requirement, bool server, bool succeeds) {
        var core = Release("Chiv2-Community/UnchainedPlugin");
        var older = Release("org/dependency", target: target);
        var newer = older with { Tag = "v2.0.0", Info = older.Info with { Target = "shared" } };
        var plugin = Release("org/plugin", dependencies: [new Dependency("https://github.com/org/dependency", requirement)]);
        ReleaseCoordinates[] roots = [new("org", "plugin", plugin.Tag), new("org", "dependency", older.Tag), new("org", "dependency", otherPin)];
        if (reverse) Array.Reverse(roots);
        DllPackage[] packages = [Package(core), Package(older, newer), Package(plugin)];
        if (succeeds) Assert.Equal(new[] { core, older, plugin }, DllManager.Resolve(packages, roots, server, false));
        else Assert.Throws<InvalidOperationException>(() => DllManager.Resolve(packages, roots, server, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitPrereleaseDependencyPinIsHonoredInEitherRequestOrder(bool reverse) {
        var core = Release("Chiv2-Community/UnchainedPlugin");
        var dependency = Release("org/dependency", "v1.0.0-rc.1");
        var plugin = Release("org/plugin", dependencies: [new Dependency("https://github.com/org/dependency", "==1.0.0-rc.1")]);
        ReleaseCoordinates[] roots = [new("org", "plugin", plugin.Tag), new("org", "dependency", dependency.Tag)];
        if (reverse) Array.Reverse(roots);
        Assert.Equal(new[] { core, dependency, plugin }, DllManager.Resolve(
            [Package(core), Package(dependency), Package(plugin)], roots, false, false));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AutomaticDependencyPrereleasesStillRequirePermission(bool allowUnstable, bool requestLatest) {
        var core = Release("Chiv2-Community/UnchainedPlugin");
        var dependency = Release("org/dependency", "v1.0.0-rc.1");
        var plugin = Release("org/plugin", dependencies: [new Dependency("https://github.com/org/dependency", "==1.0.0-rc.1")]);
        List<ReleaseCoordinates> roots = [new("org", "plugin", plugin.Tag)];
        if (requestLatest) roots.Add(new("org", "dependency", "latest"));
        DllPackage[] packages = [Package(core), Package(dependency), Package(plugin)];
        if (allowUnstable) Assert.Equal(new[] { core, dependency, plugin }, DllManager.Resolve(packages, roots, false, true));
        else Assert.Throws<InvalidOperationException>(() => DllManager.Resolve(packages, roots, false, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedPinsDoNotBypassDependencyCycleDetection(bool reverse) {
        var core = Release("Chiv2-Community/UnchainedPlugin");
        var dependency = Release("org/dependency", dependencies: [new Dependency("https://github.com/org/plugin", ">=1.0.0")]);
        var plugin = Release("org/plugin", dependencies: [new Dependency("https://github.com/org/dependency", ">=1.0.0")]);
        ReleaseCoordinates[] roots = [new("org", "plugin", plugin.Tag), new("org", "dependency", dependency.Tag)];
        if (reverse) Array.Reverse(roots);
        var error = Assert.Throws<InvalidOperationException>(() => DllManager.Resolve(
            [Package(core), Package(dependency), Package(plugin)], roots, false, false));
        Assert.StartsWith("Cyclic DLL dependency:", error.Message);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingFilesRequireRegistryHashAndSizeAndInventoryRecordsApprovedSelection(bool legacyCli) {
        var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "dll-manager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var release = Release("Chiv2-Community/UnchainedPlugin") with { DllFileName = "UnchainedPlugin.dll" };
            var packagePath = Path.Combine(root, "dll_db", "packages", "Chiv2-Community");
            Directory.CreateDirectory(packagePath);
            File.WriteAllText(Path.Combine(packagePath, "UnchainedPlugin.json"), JsonHelpers.Serialize(Package(release)));
            var registry = new AggregateModRegistry(new LocalModRegistry(root));
            var mods = new ModManager(registry, new PakDir(root, []), []);
            var plugins = Path.Combine(root, "Plugins");
            Directory.CreateDirectory(plugins);
            var manager = new DllManager(registry, mods, plugins);
            var options = new LaunchOptions([], Option<string>.None, "", false, false, Option<string>.None, Option<ServerLaunchOptions>.None);
            var plugin = legacyCli
                ? Path.Combine(plugins, ".dll-managed", release.Hash, "UnchainedPlugin.dll")
                : Path.Combine(plugins, "Managed", "Chiv2-Community", "UnchainedPlugin", release.Hash, "UnchainedPlugin.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
            await File.WriteAllBytesAsync(plugin, [1, 2, 3]);
            if (!OperatingSystem.IsWindows()) {
                await Assert.ThrowsAsync<PlatformNotSupportedException>(() => manager.PrepareLaunch(options));
                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(plugin));
                Assert.False(File.Exists(Path.Combine(plugins, "dll_manager.json")));
                return;
            }
            Assert.True((await manager.PrepareLaunch(options)).IsSome);
            plugin = Assert.Single(Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories));
            Assert.Contains(release.Hash, await File.ReadAllTextAsync(Path.Combine(plugins, "dll_manager.json")));
            Assert.Equal(Path.Combine(plugins, "Managed", "Chiv2-Community", "UnchainedPlugin", "UnchainedPlugin.dll"), plugin);
            await File.WriteAllBytesAsync(plugin, [3, 2, 1]);
            Assert.False(await DllManager.MatchesFileAsync(plugin, release));
            Assert.False(manager.Inject(new System.Diagnostics.Process()));
            await File.WriteAllBytesAsync(Path.Combine(root, ".cache", "dlls", release.Hash.ToLowerInvariant(), release.DllFileName), [3, 2, 1]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareLaunch(options));
            Assert.False(manager.Inject(new System.Diagnostics.Process()));
            Assert.Equal(new byte[] { 3, 2, 1 }, await File.ReadAllBytesAsync(plugin));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("client")]
    [InlineData("server")]
    public async Task DllPreparationUsesOnlyEachProfilePluginDirectory(string command) {
        var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "dll-instances-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var release = Release("Chiv2-Community/UnchainedPlugin") with { DllFileName = "UnchainedPlugin.dll" };
            var packagePath = Path.Combine(root, "registry", "dll_db", "packages", "Chiv2-Community");
            Directory.CreateDirectory(packagePath);
            File.WriteAllText(Path.Combine(packagePath, "UnchainedPlugin.json"), JsonHelpers.Serialize(Package(release)));
            var registry = new AggregateModRegistry(new LocalModRegistry(Path.Combine(root, "registry")));
            var globalPlugins = Path.Combine(root, FilePaths.PluginDir);
            Directory.CreateDirectory(globalPlugins);
            var loose = Path.Combine(globalPlugins, "user.dll");
            await File.WriteAllBytesAsync(loose, [9, 9, 9]);
            var paths = new List<string>();
            using var http = new HttpClient(new DownloadHandler([1, 2, 3]));
            foreach (var suffix in new[] { "One", "Two" }) {
                var options = CliLaunchOptions.Parse([
                    command, "--chivalry2-path", root, "--platform", "Steam", "--saved-dir-suffix", suffix]);
                options.Shared.Assets = new ProfileAssetPaths(suffix, root);
                var mods = ServerLauncher.CreateModManager(options, registry);
                var session = ServerLauncher.CreateDllSession(options, registry, mods, client: http);
                Assert.True((await session.PrepareLaunch(options.ToLaunchOptions())).IsSome);
                var installed = Path.Combine(options.InstancePaths.Plugins, "Managed", "Chiv2-Community", "UnchainedPlugin", "UnchainedPlugin.dll");
                Assert.True(await DllManager.MatchesFileAsync(installed, release));
                Assert.True(File.Exists(Path.Combine(options.InstancePaths.Plugins, "dll_manager.json")));
                paths.Add(installed);
            }
            Assert.NotEqual(paths[0], paths[1]);
            Assert.All(paths, path => Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path)));
            Assert.Equal([loose], Directory.GetFiles(globalPlugins, "*", SearchOption.AllDirectories));
            Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(loose));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class DownloadHandler(byte[] bytes) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) });
    }

    [Fact]
    public async Task SwitchingVersionsKeepsOnlySelectedDllInPluginsAndReusesExternalCache() {
        var root = Path.Combine(Path.GetTempPath(), "dll-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var plugins = Path.Combine(root, "Plugins");
            var rc3 = Release("Chiv2-Community/UnchainedPlugin", "v1.0.0-RC3") with { DllFileName = "UnchainedPlugin.dll" };
            var rc2 = rc3 with { Tag = "v1.0.0-RC2", Hash = Convert.ToHexString(SHA256.HashData(new byte[] { 4, 5, 6 })).ToLowerInvariant() };
            var oldManagedPath = Path.Combine(plugins, ".dll-managed", rc2.Hash.ToLowerInvariant(), rc2.DllFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(oldManagedPath)!);
            await File.WriteAllBytesAsync(oldManagedPath, [4, 5, 6]);
            var packagePath = Path.Combine(root, "dll_db", "packages", "Chiv2-Community");
            Directory.CreateDirectory(packagePath);
            File.WriteAllText(Path.Combine(packagePath, "UnchainedPlugin.json"), JsonHelpers.Serialize(Package(rc3, rc2)));
            var registry = new AggregateModRegistry(new LocalModRegistry(root));
            var mods = new ModManager(registry, new PakDir(root, []), []);
            var options = new LaunchOptions([], Option<string>.None, "", true, false, Option<string>.None, Option<ServerLaunchOptions>.None);
            foreach (var (release, bytes, download) in new[] {
                (rc3, new byte[] { 1, 2, 3 }, true),
                (rc2, new byte[] { 4, 5, 6 }, true),
                (rc3, Array.Empty<byte>(), false)
            }) {
                using var client = new HttpClient(new DownloadHandler(bytes));
                var manager = new DllManager(registry, mods, plugins,
                    [new ReleaseCoordinates("Chiv2-Community", "UnchainedPlugin", release.Tag)], client: client);
                var launch = download ? options : new LaunchOptions([], Option<string>.None, "", false, false, Option<string>.None, Option<ServerLaunchOptions>.None);
                if (!OperatingSystem.IsWindows()) {
                    await Assert.ThrowsAsync<PlatformNotSupportedException>(() => manager.PrepareLaunch(launch));
                    Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(oldManagedPath));
                    Assert.True(await DllManager.MatchesFileAsync(Path.Combine(root, ".cache", "dlls", rc3.Hash, rc3.DllFileName), rc3));
                    Assert.False(File.Exists(Path.Combine(plugins, "Managed", "Chiv2-Community", "UnchainedPlugin", rc3.DllFileName)));
                    return;
                }
                Assert.True((await manager.PrepareLaunch(launch)).IsSome);
                var active = Assert.Single(Directory.EnumerateFiles(plugins, "*.dll", SearchOption.AllDirectories));
                Assert.True(await DllManager.MatchesFileAsync(active, release));
                Assert.False(Directory.Exists(Path.Combine(plugins, ".cache")));
            }
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(root, ".cache"), "*.dll", SearchOption.AllDirectories).Count());
            Assert.False(File.Exists(oldManagedPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PakDependenciesAreEnabledOrRejectedWhenExistingSelectionConflicts(bool conflict) {
        var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "dll-pak-" + Guid.NewGuid().ToString("N"));
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
            var managed = Path.Combine(plugins, "Managed", "Chiv2-Community", "UnchainedPlugin", "UnchainedPlugin.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(managed)!);
            await File.WriteAllBytesAsync(managed, [1, 2, 3]);
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
        var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "dll-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var release = Release("Chiv2-Community/UnchainedPlugin") with { DllFileName = "UnchainedPlugin.dll" };
            var packagePath = Path.Combine(root, "dll_db", "packages", "Chiv2-Community");
            Directory.CreateDirectory(packagePath);
            File.WriteAllText(Path.Combine(packagePath, "UnchainedPlugin.json"), JsonHelpers.Serialize(Package(release)));
            var plugins = Path.Combine(root, "Plugins");
            Directory.CreateDirectory(plugins);
            var legacy = Path.Combine(plugins, "UnchainedPlugin.dll");
            await File.WriteAllBytesAsync(legacy, [9, 9, 9]);
            var registry = new AggregateModRegistry(new LocalModRegistry(root));
            var mods = new ModManager(registry, new PakDir(root, []), []);
            using var client = new HttpClient(new DownloadHandler(valid ? [1, 2, 3] : [3, 2, 1]));
            var manager = new DllManager(registry, mods, plugins, client: client);
            var options = new LaunchOptions([], Option<string>.None, "", true, false, Option<string>.None, Option<ServerLaunchOptions>.None);
            var destination = Path.Combine(root, ".cache", "dlls", release.Hash.ToLowerInvariant(), release.DllFileName);
            if (valid) {
                Assert.True((await manager.PrepareLaunch(options)).IsSome);
                Assert.True(await DllManager.MatchesFileAsync(destination, release));
                var installed = Assert.Single(Directory.GetFiles(Path.Combine(plugins, "Managed"), "*.dll", SearchOption.AllDirectories));
                Assert.True(await DllManager.MatchesFileAsync(installed, release));
                Assert.True(File.Exists(Path.Combine(plugins, "dll_manager.json")));
            }
            else {
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareLaunch(options));
                Assert.False(File.Exists(destination));
                Assert.False(File.Exists(Path.Combine(plugins, "dll_manager.json")));
                Assert.False(manager.Inject(new System.Diagnostics.Process()));
                Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(legacy));
            }
            Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(legacy));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, ".cache", "dlls"), "*.tmp", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}