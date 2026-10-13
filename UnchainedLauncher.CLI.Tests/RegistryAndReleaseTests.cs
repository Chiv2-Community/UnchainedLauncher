using LanguageExt;
using LanguageExt.Common;
using System.Diagnostics;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

[Collection("Installation directory")]
public class RegistryAndReleaseTests : IDisposable {
    private readonly string _directory = Path.Combine(
        Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "cli-tests-" + Guid.NewGuid().ToString("N"));

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
            var options = CliLaunchOptions.Parse(["server", "--chivalry2-path", _directory, "--platform", "Steam"]).ToLaunchOptions();
            var dialogue = new ConsoleDialogue();
            Assert.True((await new Chivalry2ModsInstaller(manager, dialogue).PrepareLaunch(options)).IsSome);
            var registryPak = Path.Combine(registry.RegistryPath, release.Info.Organization, release.Info.RepoName,
                release.Tag, release.PakFileName);
            using (File.Open(registryPak, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionWiringInstallsManyModsAndForwardsActorsFromAnotherDirectory(bool useYaml) {
        var callerDirectory = Directory.GetCurrentDirectory();
        var install = Path.Combine(_directory, "arbitrary game installation");
        var paks = Path.Combine(install, FilePaths.PakDir);
        Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "pakchunk0-WindowsNoEditor.sig"), "vanilla signature");
        var source = Path.Combine(_directory, "source.pak");
        File.WriteAllText(source, "test payload");
        var hash = FileHelpers.Sha512(source).Match(Right: value => value, Left: error => throw new Exception(error.Message));
        var args = new List<string> { "server", "--chivalry2-path", install, "--platform", "Steam" };
        var releases = new List<Release>();
        var actors = Enumerable.Range(0, 12).Select(i => $"/Game/Mods/Actor{i}.Actor{i}_C").ToArray();
        for (var i = 0; i < 12; i++) {
            var registryPath = Path.Combine(_directory, "registry " + i);
            var registry = new LocalModRegistry(registryPath);
            var release = Mod("org", "mod" + i, "v1.0.0").Releases.Single() with { ReleaseHash = hash };
            releases.Add(release);
            await registry.AddRelease(release, source);
            if (i == 0) await registry.AddRelease(Common().Releases.Single() with { ReleaseHash = hash }, source);
            args.AddRange(["--add-registry", "local:" + registryPath, "--mod", $"org/mod{i}:v1.0.0", "--server-mod", actors[i]]);
        }
        var options = CliLaunchOptions.Parse(args.ToArray());
        if (useYaml) {
            string Quote(string value) => "'" + value.Replace("'", "''") + "'";
            var yaml = new List<string> { "chiv2-path: " + Quote(install), "platform: Steam", "mod-setup:", "  registries:" };
            yaml.AddRange(options.Shared.Registries.Select(r => "    - " + Quote(r.Kind + ":" + r.Location)));
            yaml.Add("  mods:");
            yaml.AddRange(options.Shared.Mods.Select(m => "    - " + Quote($"{m.Org}/{m.ModuleName}:{m.Version}")));
            yaml.AddRange(["server:", "  mod-actors:"]);
            yaml.AddRange(actors.Select(actor => "    - " + Quote(actor)));
            var config = Path.Combine(_directory, "launch.yaml");
            File.WriteAllLines(config, yaml);
            options = CliLaunchOptions.Parse(["server", "--config-path", config]);
        }
        var aggregate = ServerLauncher.CreateRegistry(options);
        var official = Assert.Single(aggregate.ModRegistries.OfType<GithubModRegistry>());
        Assert.Equal(("Chiv2-Community", "C2ModRegistry"), (official.Organization, official.RepoName));
        Assert.Equal(13, aggregate.ModRegistries.Count());
        // Keep metadata deterministic/offline; assert the official production entry above,
        // then use all real local registries created by the production factory for installs.
        var offline = new AggregateModRegistry(aggregate.ModRegistries.OfType<LocalModRegistry>().Cast<IModRegistry>().ToArray());
        var manager = new ModManager(offline, new PakDir(paks, []), options.Shared.Mods);
        Assert.False((await manager.UpdateModsList()).HasErrors);
        ServerLauncher.ValidateReleases(manager, options.Shared.Mods);
        var process = new CapturingProcessLauncher();
        await ServerLauncher.InInstallationDirectoryAsync(options, async () => {
            Assert.Equal(install, Directory.GetCurrentDirectory());
            var dialogue = new ConsoleDialogue();
            var installer = new Chivalry2ModsInstaller(manager, dialogue);
            var signing = SigPreparer.Create(manager.PakDir, dialogue);
            var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(async launch => {
                var prepared = await installer.PrepareLaunch(launch);
                Assert.True(prepared.IsSome);
                Assert.True((await signing.PrepareLaunch(Unit.Default)).IsSome);
                return prepared;
            });
            // Capture at the OS process boundary; retain Core preparation/argument rendering.
            var launcher = new UnchainedChivalry2Launcher(preparer, process, options.Shared.Chivalry2Path, new NullInjector());
            return await launcher.Launch(options.ToLaunchOptions());
        });
        Assert.Equal(callerDirectory, Directory.GetCurrentDirectory());
        Assert.Equal(Path.Combine(install, FilePaths.BinDir), process.WorkingDirectory);
        Assert.Contains("--server-mods " + string.Join(",", actors), process.Arguments);
        var installed = ((PakDir)manager.PakDir).ManagedPaks.ToArray();
        Assert.Equal(13, installed.Length);
        foreach (var release in releases) {
            Assert.Contains(installed, pak => pak.Coordinates == ReleaseCoordinates.FromRelease(release));
        }
        foreach (var pak in installed) {
            Assert.Equal("test payload", File.ReadAllText(Path.Combine(paks, pak.PakFileName)));
            Assert.True(File.Exists(Path.ChangeExtension(Path.Combine(paks, pak.PakFileName), ".sig")));
        }
        foreach (var registry in offline.ModRegistries.Cast<LocalModRegistry>()) {
            foreach (var path in Directory.EnumerateFiles(registry.RegistryPath, "*.pak", SearchOption.AllDirectories)) {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionDirectoryScopeRestoresCallerAfterFailureOrCancellation(bool cancel) {
        var callerDirectory = Directory.GetCurrentDirectory();
        var options = CliLaunchOptions.Parse(["server", "--chivalry2-path", _directory, "--platform", "Steam"]);
        await Assert.ThrowsAnyAsync<Exception>(() => ServerLauncher.InInstallationDirectoryAsync<int>(options, () => {
            Assert.Equal(_directory, Directory.GetCurrentDirectory());
            if (cancel) throw new OperationCanceledException();
            throw new IOException("Preparation failed.");
        }));
        Assert.Equal(callerDirectory, Directory.GetCurrentDirectory());
    }

    private sealed class CapturingProcessLauncher : IProcessLauncher {
        public string Executable => "test-game.exe";
        public string WorkingDirectory { get; private set; } = "";
        public string Arguments { get; private set; } = "";
        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args) {
            WorkingDirectory = workingDirectory;
            Arguments = args;
            return new LaunchFailed(Executable, args, Error.New("Stopped at the OS boundary."));
        }
    }
}

[CollectionDefinition("Installation directory", DisableParallelization = true)]
public class InstallationDirectoryCollection { }
