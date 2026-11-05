using System.Diagnostics;
using UnchainedLauncher.Core.Services.Server.A2S;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

[Collection("Installation directory")]
public sealed class ServerGroupTests : IDisposable {
    private readonly string _root = Path.Combine(System.Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(),
        "server-group-" + Guid.NewGuid().ToString("N"));

    private CliCommandOptions Parse(string fixture, string command = "server", params string[] extra) {
        var path = YamlTestResources.Write(_root, "server-group/" + fixture);
        return CommandLineOptions.Parse([command, "--config-path", path, .. extra]);
    }

    private ServerGroupOptions Group() {
        var group = Assert.IsType<ServerGroupOptions>(Parse("servers"));
        foreach (var entry in group.Servers)
            entry.Options.Shared.Assets = new UnchainedLauncher.Core.Services.Mods.ProfileAssetPaths(entry.Options.Shared.SavedDirSuffix, _root);
        return group;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void DictionaryPreservesOrderSharedSettingsAndIsolatedProfiles() {
        var group = Group();
        Assert.True(group.NoTui);
        Assert.Equal(["courtyard", "arena"], group.Servers.Select(entry => entry.Id));
        Assert.Equal(["courtyard", "arena"], group.Servers.Select(entry => entry.Options.Shared.SavedDirSuffix));
        Assert.All(group.Servers, entry => Assert.Equal(Path.Combine(_root, "game"), entry.Options.Shared.Chivalry2Path));
        Assert.Equal(["Courtyard", "Arena"], group.Servers.Select(entry => entry.Options.Settings.Name));
        Assert.Equal(["FFA_Courtyard", "TO_Stronghold"], group.Servers.Select(entry => entry.Options.ToServerLaunchOptions().Map));
        Assert.Equal(7006, group.Servers[1].Options.ToServerLaunchOptions().QueryPort);
        Assert.Equal(2, group.Servers.Select(entry => entry.Options.InstancePaths.Paks).Distinct().Count());
        Assert.Equal(2, group.Servers.Select(entry => entry.Options.InstancePaths.Plugins).Distinct().Count());
    }

    [Fact]
    public void PerServerProfilesAndArgumentsRemainIndependent() {
        var group = Assert.IsType<ServerGroupOptions>(Parse("independent-arguments"));
        Assert.Equal("ArenaProfile", group.Servers[1].Options.Shared.SavedDirSuffix);
        Assert.Equal(["common"], group.Servers[0].Options.GameArguments);
        Assert.Equal(["common", "arena-only"], group.Servers[1].Options.GameArguments);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("singular")]
    [InlineData("singular-with-servers")]
    [InlineData("missing")]
    [InlineData("global-only")]
    [InlineData("null-servers")]
    [InlineData("scalar-servers")]
    [InlineData("list-servers")]
    [InlineData("unsafe-id")]
    [InlineData("case-folded-ids")]
    [InlineData("default-port-collision")]
    [InlineData("zero-port")]
    [InlineData("wrong-map-key")]
    [InlineData("unsafe-suffix")]
    [InlineData("unsafe-dll-option")]
    [InlineData("root-mods")]
    [InlineData("root-suffix")]
    public void InvalidGroupsFailBeforeLaunch(string fixture) => Assert.Throws<ArgumentException>(() => Parse(fixture));

    [Theory]
    [InlineData("singular")]
    [InlineData("singular-with-servers")]
    public void SingularServerIsNotRecognizedByTheServerSchema(string fixture) {
        var error = Assert.Throws<ArgumentException>(() => Parse(fixture));
        Assert.Contains("Invalid YAML configuration or schema", error.Message);
        Assert.Null(typeof(ServerConfiguration).GetProperty("Server"));
        Assert.IsType<ClientOptions>(Parse(fixture, "client"));
        var path = YamlTestResources.Write(_root, "server-group/" + fixture);
        Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(["mod-query", "list", "--config-path", path]));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("global-only")]
    [InlineData("empty")]
    [InlineData("null-servers")]
    public void NamedServersAreRequiredEvenWithGlobalSettings(string fixture) {
        var error = Assert.Throws<ArgumentException>(() => Parse(fixture));
        Assert.Contains("servers must contain at least one named server", error.Message);
    }

    [Fact]
    public void OneNamedServerIsStillAGroupAndUsesItsKeyAsTheProfileDefault() {
        var group = Assert.IsType<ServerGroupOptions>(Parse("single"));
        var entry = Assert.Single(group.Servers);
        Assert.Equal("server", entry.Id);
        Assert.Equal("server", entry.Options.Shared.SavedDirSuffix);
        Assert.Equal("FFA_Courtyard", entry.Options.Settings.StartingMap);
        var cli = Assert.IsType<ServerOptions>(CommandLineOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam"]));
        Assert.Equal("UnchainedCLI", cli.Shared.SavedDirSuffix);
        Assert.Equal("FFA_Courtyard", cli.Settings.StartingMap);
    }

    [Fact]
    public void PerServerModsDllsRegistriesAndIniDoNotLeakAcrossServers() {
        var group = Assert.IsType<ServerGroupOptions>(Parse("independent-packages"));
        var first = group.Servers[0].Options;
        var second = group.Servers[1].Options;
        Assert.Equal("one", Assert.Single(first.Shared.Mods).ModuleName);
        Assert.Equal("two", Assert.Single(second.Shared.Mods).ModuleName);
        Assert.Single(first.Shared.Dlls);
        Assert.Empty(second.Shared.Dlls);
        Assert.Equal(Path.Combine(_root, "one"), Assert.Single(first.Shared.Registries).Location);
        Assert.Equal(Path.Combine(_root, "two"), Assert.Single(second.Shared.Registries).Location);
        Assert.True(first.Ini.Contains("Game", "/Script/TBL.TBLGameMode", "BotBackfillEnabled"));
        Assert.False(second.Ini.Contains("Game", "/Script/TBL.TBLGameMode", "BotBackfillEnabled"));
    }

    [Fact]
    public void DuplicatePortsAndCaseInsensitiveProfilesAreRejected() {
        var path = YamlTestResources.Write(_root, "server-group/servers");
        File.WriteAllText(path, File.ReadAllText(path).Replace("7007", "7000"));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["server", "--config-path", path]));
        var profiles = YamlTestResources.Write(_root, "server-group/profile-collision", ("FIRST", "SAME"), ("SECOND", "same"));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["server", "--config-path", profiles]));
    }

    [Fact]
    public void ProfilesThatCollideAfterGameHyphenRemovalAreRejected() {
        var path = YamlTestResources.Write(_root, "server-group/profile-collision", ("FIRST", "Host-one"), ("SECOND", "Hostone"));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["server", "--config-path", path]));
    }

    [Fact]
    public void InstanceLeasePreventsConcurrentReuseAndCanBeReacquiredAfterRelease() {
        var paths = Group().Servers[0].Options.InstancePaths;
        using (paths.AcquireLease()) Assert.Throws<IOException>(() => paths.AcquireLease());
        using (paths.AcquireLease()) Assert.True(Directory.Exists(paths.Root));
    }

    [Fact]
    public void NativePakDirectoriesDoNotAddPluginArguments() {
        Assert.All(Group().Servers, entry =>
            Assert.DoesNotContain("--additional-pak-dir", entry.Options.ToLaunchOptions().LaunchArgs));
    }

    [Fact]
    public void CliOverridesAreIgnoredAndUnsafeAuthorizationAppliesToEveryServer() {
        var group = Assert.IsType<ServerGroupOptions>(Parse("servers", "server", "--name", "ignored", "--platform", "invalid",
            "--unsafe-very-dangerous-load-all-dlls"));
        Assert.All(group.Servers, entry => Assert.True(entry.Options.Shared.UnsafeLoadAllDlls));
        Assert.Equal("Courtyard", group.Servers[0].Options.Settings.Name);
        Assert.All(Group().Servers, entry => Assert.False(entry.Options.Shared.UnsafeLoadAllDlls));
    }

    [Fact]
    public void ClientAndRegistryQueriesSkipInactiveServersSchema() {
        var path = YamlTestResources.Write(_root, "server-group/inactive");
        Assert.IsType<ClientOptions>(CommandLineOptions.Parse(["client", "--config-path", path]));
        Assert.IsType<ModQueryOptions>(CommandLineOptions.Parse(["mod-query", "list", "--config-path", path]));
    }

    private sealed class PendingQuery : IA2S {
        public Task<A2SInfo> InfoAsync() => new TaskCompletionSource<A2SInfo>().Task;
    }

    private sealed class FailingQuery : IA2S {
        public Task<A2SInfo> InfoAsync() => Task.FromException<A2SInfo>(new TimeoutException());
    }

    private static Process Child(int? exitCode = null) {
        var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh") {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        info.ArgumentList.Add(exitCode.HasValue ? $"exit {exitCode}" : OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 > nul" : "sleep 30");
        return Process.Start(info)!;
    }

    [Fact]
    public async Task DashboardRetainsEachConfigurationAfterLaunchAndQueryInitialization() {
        var group = Group();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var output = new StringWriter();
        var original = Console.Out;
        var launches = 0;
        var queries = 0;
        try {
            Console.SetOut(output);
            Assert.Equal(0, await ServerGroupRunner.RunAsync(group, stop, (options, _) => {
                options.Settings.Name = $"prepared-{++launches}";
                return Task.FromResult(new LaunchedGame(Child()));
            }, options => {
                Assert.Equal(2, launches);
                options.Settings.Name += $"-query-{++queries}";
                if (queries == 2) stop.CancelAfter(250);
                return new PendingQuery();
            }).WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally { Console.SetOut(original); }
        var text = output.ToString();
        Assert.Contains("[courtyard] Starting | prepared-1-query-1 | PID", text);
        Assert.Contains("[arena] Starting | prepared-2-query-2 | PID", text);
        Assert.Contains("Game Port 7000 | Query Port 7002 | Beacon Port 7001 | RCON Port 7003", text);
        Assert.Contains("Game Port 7004 | Query Port 7006 | Beacon Port 7005 | RCON Port 7007", text);
        Assert.Equal(2, queries);
    }

    [Fact]
    public async Task StartupFailureStopsAlreadyLaunchedServers() {
        using var stop = new CancellationTokenSource();
        Task? exited = null;
        var calls = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ServerGroupRunner.RunAsync(Group(), stop,
            (_, _) => {
                if (++calls == 2) throw new InvalidOperationException("launch failed");
                var child = Child();
                exited = child.WaitForExitAsync();
                return Task.FromResult(new LaunchedGame(child));
            }, _ => new PendingQuery()));
        Assert.Equal("launch failed", error.Message);
        await exited!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GroupCancellationStopsEveryServerAndInterruptsPendingQueries() {
        using var stop = new CancellationTokenSource();
        var exits = new List<Task>();
        var order = new List<string>();
        Assert.Equal(0, await ServerGroupRunner.RunAsync(Group(), stop, async (options, _) => {
            order.Add(options.Settings.Name);
            var child = Child();
            exits.Add(child.WaitForExitAsync());
            if (exits.Count == 2) stop.CancelAfter(250);
            await Task.Yield();
            return new LaunchedGame(child);
        }, _ => new PendingQuery()).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(["Courtyard", "Arena"], order);
        await Task.WhenAll(exits).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NaturalExitsReturnFailureWithoutStoppingOtherServersEarly() {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var calls = 0;
        Assert.Equal(7, await ServerGroupRunner.RunAsync(Group(), stop,
            (_, _) => Task.FromResult(new LaunchedGame(Child(++calls == 1 ? 7 : 0))), _ => new FailingQuery()));
        Assert.Equal(2, calls);
        Assert.False(stop.IsCancellationRequested);
    }

    [Fact]
    public async Task ExitAtMonitorInitializationDoesNotStopSurvivingSibling() {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Process? first = null;
        Process? survivor = null;
        Task? survivorExit = null;
        var calls = 0;
        var queriedSurvivor = false;
        Assert.Equal(7, await ServerGroupRunner.RunAsync(Group(), stop, (_, _) => {
            if (++calls == 1) {
                var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh") {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    ArgumentList = { OperatingSystem.IsWindows() ? "/c" : "-c",
                        OperatingSystem.IsWindows() ? "set /p ignored= & exit 7" : "read ignored; exit 7" }
                };
                first = Process.Start(info)!;
                return Task.FromResult(new LaunchedGame(first));
            }
            survivor = Child();
            survivorExit = survivor.WaitForExitAsync();
            return Task.FromResult(new LaunchedGame(survivor));
        }, _ => {
            if (!first!.HasExited) {
                // Release the real child exactly when group monitoring initializes.
                first.StandardInput.WriteLine("exit");
                first.StandardInput.Flush();
                Assert.True(first.WaitForExit(5000));
                return new FailingQuery();
            }
            return new CallbackQuery(() => {
                Assert.NotNull(survivor);
                Assert.False(survivor!.HasExited);
                queriedSurvivor = true;
                survivor.Kill(entireProcessTree: true);
            });
        }));
        Assert.True(queriedSurvivor);
        await survivorExit!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stop.IsCancellationRequested);
    }

    private sealed class CallbackQuery(Action callback) : IA2S {
        public Task<A2SInfo> InfoAsync() {
            callback();
            return Task.FromException<A2SInfo>(new TimeoutException());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalGroupSnapshotsPrintTailForEveryServer(bool cancel) {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var output = new StringWriter();
        var original = Console.Out;
        var count = 0;
        var children = new List<Process>();
        try {
            Console.SetOut(output);
            Assert.Equal(0, await ServerGroupRunner.RunAsync(Group(), stop, async (_, _) => {
                var child = Child(cancel ? null : 0);
                children.Add(child);
                if (!cancel) await child.WaitForExitAsync();
                var console = new ForwardedConsole();
                var text = System.Text.Encoding.UTF8.GetBytes($"group-tail-{++count}\n");
                await console.CaptureAsync(new MemoryStream(text));
                return new LaunchedGame(child) { ConsoleOutput = console };
            }, _ => {
                if (cancel) stop.Cancel();
                return new PendingQuery();
            }).WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { Console.SetOut(original); }
        Assert.Contains("group-tail-1", output.ToString());
        Assert.Contains("group-tail-2", output.ToString());
    }
}
