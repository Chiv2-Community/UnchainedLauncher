using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class RegistryAndReleaseTests : IDisposable {
    private readonly string _directory = Path.Combine(
        OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "cli-tests-" + Guid.NewGuid().ToString("N"));

    public RegistryAndReleaseTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ConfigurationSupportsNestedAggregatesAndRelativeLocalPaths() {
        var local = Path.Combine(_directory, "local");
        Directory.CreateDirectory(local);
        var path = Path.Combine(_directory, "registries.json");
        var metadata = new AggregateModRegistryMetadata([
            new GithubModRegistryMetadata("owner", "repo"),
            new AggregateModRegistryMetadata([new LocalModRegistryMetadata("local")])
        ]);
        File.WriteAllText(path, JsonHelpers.Serialize<ModRegistryMetadata>(metadata));
        var registry = Assert.IsType<AggregateModRegistry>(RegistrySpec.Parse("config:" + path).Create());
        // Core flattens nested aggregates.
        var localRegistry = Assert.Single(registry.ModRegistries.OfType<LocalModRegistry>());
        Assert.Equal(local, localRegistry.RegistryPath);
        Assert.Single(registry.ModRegistries.OfType<GithubModRegistry>());
    }

    [Fact]
    public void MissingLocalRegistryDoesNotCreateADirectory() {
        var path = Path.Combine(_directory, "missing");
        Assert.Throws<DirectoryNotFoundException>(() => RegistrySpec.Parse("local:" + path).Create());
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void InvalidConfigurationFailsClearly() {
        var path = Path.Combine(_directory, "bad.json");
        File.WriteAllText(path, "{ broken");
        Assert.Throws<ArgumentException>(() => RegistrySpec.Parse("config:" + path).Create());
    }

    private static Mod Mod(string org, string id, string tag, params Dependency[] dependencies) {
        var info = new ModInfo($"https://github.com/{org}/{id}", id, "test", null, null, [], [], dependencies.ToList());
        var release = new Release(tag, "hash", "test.pak", DateTime.UtcNow, info,
            new AssetCollections([], [], [], [], []), null);
        return new Mod(info, [release]);
    }

    private static Mod Common() => Mod("Chiv2-Community", "Unchained-Mods", "v1.0.0");

    private ModManager Manager(ReleaseCoordinates[] requested, params Mod[] mods) =>
        new(new AggregateModRegistry(), new PakDir(_directory, []), requested, mods);

    [Fact]
    public void ValidRequestedReleaseAndCommonDependencyPass() {
        var coordinate = new ReleaseCoordinates("org", "mod", "v1.2.3");
        var manager = Manager([coordinate], Common(), Mod("org", "mod", "v1.2.3"));
        ServerLauncher.ValidateReleases(manager, [coordinate]);
    }

    [Fact]
    public void MissingRequestedReleaseFailsInsteadOfSilentlyIgnoringIt() {
        var coordinate = new ReleaseCoordinates("org", "mod", "v1.2.3");
        var manager = Manager([coordinate], Common(), Mod("org", "mod", "v2.0.0"));
        Assert.Throws<ArgumentException>(() => ServerLauncher.ValidateReleases(manager, [coordinate]));
    }

    [Fact]
    public void MissingCommonReleaseFails() {
        Assert.Throws<InvalidOperationException>(() => ServerLauncher.ValidateReleases(Manager([]), []));
    }

    [Fact]
    public void MissingDependencyFailsInsteadOfSilentlyDroppingIt() {
        var coordinate = new ReleaseCoordinates("org", "mod", "v1.0.0");
        var manager = Manager([coordinate], Common(), Mod("org", "mod", "v1.0.0",
            new Dependency("https://github.com/org/missing", "*")));
        Assert.Throws<InvalidOperationException>(() => ServerLauncher.ValidateReleases(manager, [coordinate]));
    }

    [Fact]
    public void ConflictBetweenPinnedCommonAndLatestAutomaticCommonFails() {
        var common = Common();
        var older = Mod("Chiv2-Community", "Unchained-Mods", "v0.9.0").Releases.Single();
        common.Releases.Add(older);
        var coordinate = ReleaseCoordinates.FromRelease(older);
        var manager = Manager([coordinate], common);
        Assert.Throws<ArgumentException>(() => ServerLauncher.ValidateReleases(manager, [coordinate]));
    }

    [Fact]
    public void PinnedReleaseDependencyMustActuallyBeInTheInstallSet() {
        var older = Mod("org", "mod", "v1.0.0", new Dependency("https://github.com/org/old-dependency", "*"));
        older.Releases.Add(Mod("org", "mod", "v2.0.0").Releases.Single());
        var coordinate = new ReleaseCoordinates("org", "mod", "v1.0.0");
        var manager = Manager([coordinate], Common(), older, Mod("org", "old-dependency", "v1.0.0"));
        Assert.Throws<InvalidOperationException>(() => ServerLauncher.ValidateReleases(manager, [coordinate]));
    }

    [Fact]
    public async Task CoreModPreparationAndInventoryRoundTripWorkInAConsoleHost() {
        await Task.Run(async () => {
            Assert.Null(SynchronizationContext.Current);
            var paks = Path.Combine(_directory, "paks");
            Directory.CreateDirectory(paks);
            File.WriteAllText(Path.Combine(paks, "pakchunk0-WindowsNoEditor.sig"), "test vanilla signature");
            var source = Path.Combine(_directory, "source.pak");
            File.WriteAllText(source, "test pak payload");
            var registry = new LocalModRegistry(Path.Combine(_directory, "registry"));
            var release = Common().Releases.Single() with {
                ReleaseHash = FileHelpers.Sha512(source).Match(Right: hash => hash, Left: error => throw new Exception(error.Message))
            };
            await registry.AddRelease(release, source);
            var manager = new ModManager(registry, new PakDir(paks, []), []);
            Assert.False((await manager.UpdateModsList()).HasErrors);
            var options = ServerOptions.Parse(["server", "--chivalry2-path", _directory, "--platform", "Steam"]).ToLaunchOptions();
            var dialogue = new ConsoleDialogue();
            Assert.True((await new Chivalry2ModsInstaller(manager, dialogue).PrepareLaunch(options)).IsSome);
            Assert.True((await SigPreparer.Create(manager.PakDir, dialogue).PrepareLaunch(LanguageExt.Unit.Default)).IsSome);
            var installed = Assert.Single(((PakDir)manager.PakDir).ManagedPaks);
            Assert.Equal("test pak payload", File.ReadAllText(Path.Combine(paks, installed.PakFileName)));
            Assert.True(File.Exists(Path.ChangeExtension(Path.Combine(paks, installed.PakFileName), ".sig")));

            var codec = new ModManagerCodec(registry);
            var restored = codec.Deserialize(codec.Serialize(manager));
            Assert.True(restored.Success);
            Assert.True((await new Chivalry2ModsInstaller(restored.Result!, dialogue).PrepareLaunch(options)).IsSome);
            Assert.Single(((PakDir)restored.Result!.PakDir).ManagedPaks);
        });
    }
}