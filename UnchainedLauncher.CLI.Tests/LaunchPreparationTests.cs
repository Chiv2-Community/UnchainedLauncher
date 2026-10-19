using System.Net;
using System.Security.Cryptography;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class LaunchPreparationTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "launch-preparation-" + Guid.NewGuid().ToString("N"));

    public LaunchPreparationTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("client", true, false)]
    [InlineData("server", true, false)]
    [InlineData("client", true, true)]
    [InlineData("server", true, true)]
    [InlineData("client", false, false)]
    public async Task PreparationValidatesDllSelectedPakDependenciesBeforeInstalling(
        string command, bool hasTransitiveDependency, bool dependencyPublished) {
        var local = new LocalModRegistry(Path.Combine(_directory, "registry"));
        var source = Path.Combine(_directory, "payload.pak");
        File.WriteAllText(source, "pak payload");
        var hash = FileHelpers.Sha512(source).Match(value => value, error => throw new Exception(error.Message));
        Release Pak(string id, string tag, params Dependency[] dependencies) => new(tag, hash, "mod.pak",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new ModInfo("https://github.com/" + id, id.Split('/')[1], "test", null, null, [], ["test"], dependencies.ToList()),
            new AssetCollections([], [], [], [], []), null);
        await local.AddRelease(Pak("Chiv2-Community/Unchained-Mods", "v1.0.0"), source);
        await local.AddRelease(Pak("org/required", "v1.0.0", hasTransitiveDependency
            ? [new Dependency("https://github.com/org/transitive", "*")] : []), source);
        // Core traverses latest-release metadata, which omits the older release's dependency.
        await local.AddRelease(Pak("org/required", "v2.0.0"), source);
        if (dependencyPublished) await local.AddRelease(Pak("org/transitive", "v1.0.0"), source);

        AddPlugin(local, "org/required", "<2.0.0");

        var request = CliLaunchOptions.Parse([
            command, "--chivalry2-path", _directory, "--platform", "Steam",
            .. command == "server" ? new[] { "--local-ip", "127.0.0.1" } : []
        ]);
        var pakPath = Path.Combine(_directory, FilePaths.PakDir);
        request.Shared.Assets = new ProfileAssetPaths(request.Shared.SavedDirSuffix, _directory);
        Directory.CreateDirectory(pakPath);
        File.WriteAllText(Path.Combine(pakPath, "pakchunk0-WindowsNoEditor.sig"), "signature");
        var registry = new AggregateModRegistry(local);
        var manager = ServerLauncher.CreateModManager(request, registry);
        Assert.False((await manager.UpdateModsList()).HasErrors);
        ServerLauncher.ValidateReleases(manager, request.Shared.Mods);
        Assert.Empty(manager.EnabledModReleaseCoordinates);

        using var client = new HttpClient(new DownloadHandler());
        var plugins = Path.Combine(_directory, "plugins");
        var session = new DllLaunchSession(registry, manager, plugins, client: client,
            cacheDirectory: Path.Combine(_directory, "dll-cache"));
        var cachePath = Path.Combine(_directory, "cli_mod_manager.json");
        var preparer = ServerLauncher.CreateLaunchPreparer(request, manager, session, new ModManagerCodec(registry), cachePath);
        if (hasTransitiveDependency) {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => preparer.PrepareLaunch(request.ToLaunchOptions()));
            Assert.Contains(dependencyPublished ? "Core did not resolve dependency org/transitive" : "Missing stable dependency: org/transitive", error.Message);
            Assert.Empty(Directory.GetFiles(request.InstancePaths.Paks, "*.pak"));
            Assert.False(File.Exists(cachePath));
        }
        else {
            Assert.True((await preparer.PrepareLaunch(request.ToLaunchOptions())).IsSome);
            Assert.True(File.Exists(cachePath));
            var paks = Directory.GetFiles(request.InstancePaths.Paks, "*.pak");
            Assert.Equal(2, paks.Length);
            Assert.All(paks, pak => Assert.Equal("signature", File.ReadAllText(Path.ChangeExtension(pak, ".sig"))));
        }
        Assert.True(File.Exists(Path.Combine(plugins, "dll_manager.json")));
        Assert.Contains(manager.EnabledModReleaseCoordinates, coordinate =>
            coordinate.Org == "org" && coordinate.ModuleName == "required" && coordinate.Version == "v1.0.0");
    }

    [Theory]
    [InlineData("cycle", true)]
    [InlineData("self", true)]
    [InlineData("acyclic", true)]
    [InlineData("cycle", false)]
    [InlineData("self", false)]
    [InlineData("acyclic", false)]
    public async Task DependencySortMustRetainEverySelectedReleaseBeforePakMutation(string graph, bool validateDirectly) {
        var local = new LocalModRegistry(Path.Combine(_directory, "registry"));
        var source = Path.Combine(_directory, "payload.pak");
        File.WriteAllText(source, "pak payload");
        var hash = FileHelpers.Sha512(source).Match(value => value, error => throw new Exception(error.Message));
        Release Pak(string id, params string[] dependencies) => new("v1.0.0", hash, "mod.pak",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new ModInfo("https://github.com/" + id, id.Split('/')[1], "test", null, null, [], ["test"],
                dependencies.Select(dep => new Dependency("https://github.com/" + dep, "*")).ToList()),
            new AssetCollections([], [], [], [], []), null);
        await local.AddRelease(Pak("Chiv2-Community/Unchained-Mods"), source);
        await local.AddRelease(Pak("org/a", graph == "self" ? "org/a" : "org/b"), source);
        await local.AddRelease(Pak("org/b", graph == "cycle" ? ["org/a"] : []), source);
        AddPlugin(local, "org/a", ">=1.0.0");

        var request = CliLaunchOptions.Parse(["client", "--chivalry2-path", _directory, "--platform", "Steam"]);
        request.Shared.Assets = new ProfileAssetPaths(request.Shared.SavedDirSuffix, _directory);
        var pakPath = Path.Combine(_directory, FilePaths.PakDir);
        Directory.CreateDirectory(pakPath);
        File.WriteAllText(Path.Combine(pakPath, "pakchunk0-WindowsNoEditor.sig"), "signature");
        var registry = new AggregateModRegistry(local);
        var manager = ServerLauncher.CreateModManager(request, registry);
        Assert.False((await manager.UpdateModsList()).HasErrors);
        var selected = new ReleaseCoordinates("org", "a", "v1.0.0");
        if (validateDirectly) {
            manager.EnableModRelease(selected);
            if (graph == "acyclic") ServerLauncher.ValidateReleases(manager, [selected]);
            else Assert.Contains("cycle", Assert.Throws<InvalidOperationException>(() =>
                ServerLauncher.ValidateReleases(manager, [selected])).Message);
            Assert.Empty(Directory.GetFiles(request.InstancePaths.Paks, "*.pak"));
            return;
        }
        ServerLauncher.ValidateReleases(manager, []);

        var unknown = Path.Combine(request.InstancePaths.Paks, "unknown.pak");
        File.WriteAllText(unknown, "preserve unknown bytes");
        using var client = new HttpClient(new DownloadHandler());
        var session = new DllLaunchSession(registry, manager, Path.Combine(_directory, "plugins"), client: client,
            cacheDirectory: Path.Combine(_directory, "dll-cache"));
        var cachePath = Path.Combine(_directory, "cli_mod_manager.json");
        var preparer = ServerLauncher.CreateLaunchPreparer(request, manager, session, new ModManagerCodec(registry), cachePath);
        if (graph == "acyclic") {
            Assert.True((await preparer.PrepareLaunch(request.ToLaunchOptions())).IsSome);
            Assert.Equal(4, Directory.GetFiles(request.InstancePaths.Paks, "*.pak").Length);
        }
        else {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => preparer.PrepareLaunch(request.ToLaunchOptions()));
            Assert.Contains("cycle", error.Message);
            Assert.Equal([unknown], Directory.GetFiles(request.InstancePaths.Paks, "*.pak"));
            Assert.False(File.Exists(cachePath));
        }
        Assert.Contains(selected, manager.EnabledModReleaseCoordinates);
        Assert.Equal("preserve unknown bytes", File.ReadAllText(unknown));
    }

    private static void AddPlugin(LocalModRegistry local, string dependency, string version) {
        var dll = new DllRelease("v1.0.0", Convert.ToHexString(SHA256.HashData([1, 2, 3])).ToLowerInvariant(),
            "UnchainedPlugin.dll", "https://example.test/UnchainedPlugin.dll", 3,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DllInfo {
                RepoUrl = "https://github.com/Chiv2-Community/UnchainedPlugin",
                Name = "core",
                Description = "test",
                Authors = ["test"],
                ModDependencies = [new Dependency("https://github.com/" + dependency, version)]
            }, null);
        var packages = Path.Combine(local.RegistryPath, "dll_db", "packages", "Chiv2-Community");
        Directory.CreateDirectory(packages);
        File.WriteAllText(Path.Combine(packages, "UnchainedPlugin.json"), JsonHelpers.Serialize(new DllPackage(dll.Info, [dll])));
    }

    private sealed class DownloadHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                RequestMessage = request,
                Content = new ByteArrayContent([1, 2, 3])
            });
    }
}