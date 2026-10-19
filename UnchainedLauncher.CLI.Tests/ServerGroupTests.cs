using System.Diagnostics;
using UnchainedLauncher.Core.Services.Server.A2S;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ServerGroupTests : IDisposable {
    private readonly string _root = Path.Combine(System.Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(),
        "server-group-" + Guid.NewGuid().ToString("N"));

    private const string Servers = """
        servers:
          courtyard:
            name: Courtyard
            starting-map: FFA_Courtyard
            game-port: 7000
            ping-port: 7001
            query-port: 7002
            rcon-port: 7003
          arena:
            name: Arena
            starting-map: TO_Stronghold
            game-port: 7004
            ping-port: 7005
            query-port: 7006
            rcon-port: 7007
        """;

    private CliCommandOptions Parse(string body, string command = "server", params string[] extra) {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "servers.yaml");
        File.WriteAllText(path, "chiv2-path: ./game\nplatform: Steam\nno-tui: true\n" + body);
        return CommandLineOptions.Parse([command, "--config-path", path, .. extra]);
    }

    private ServerGroupOptions Group() {
        var group = Assert.IsType<ServerGroupOptions>(Parse(Servers));
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
        var group = Assert.IsType<ServerGroupOptions>(Parse(Servers.Replace("name: Arena", "name: Arena\n    saved-dir-suffix: ArenaProfile\n    game-args: [arena-only]")
            + "\ngame-args: [common]"));
        Assert.Equal("ArenaProfile", group.Servers[1].Options.Shared.SavedDirSuffix);
        Assert.Equal(["common"], group.Servers[0].Options.GameArguments);
        Assert.Equal(["common", "arena-only"], group.Servers[1].Options.GameArguments);
    }

    [Theory]
    [InlineData("servers: {}")]
    [InlineData("servers: {one: null}")]
    [InlineData("server: {}\nservers: {one: {}}")]
    [InlineData("servers: {'../one': {}}")]
    [InlineData("servers: {one: {}, ONE: {}}")]
    [InlineData("servers: {one: {}, two: {}}")]
    [InlineData("servers: {one: {game-port: 0}}")]
    [InlineData("servers: {one: {map: TO_Test}}")]
    [InlineData("servers: {one: {saved-dir-suffix: '../outside'}}")]
    [InlineData("servers: {one: {unsafe-very-dangerous-load-all-dlls: true}}")]
    [InlineData("mod-setup: {mods: []}\nservers: {one: {}}")]
    [InlineData("saved-dir-suffix: Host\nservers: {one: {}}")]
    public void InvalidGroupsFailBeforeLaunch(string yaml) => Assert.Throws<ArgumentException>(() => Parse(yaml));

    [Fact]
    public void PerServerModsDllsRegistriesAndIniDoNotLeakAcrossServers() {
        var group = Assert.IsType<ServerGroupOptions>(Parse(Servers
            .Replace("name: Courtyard", "name: Courtyard\n    mod-setup: {registries: ['local:one'], mods: ['org/one:v1'], dlls: ['org/plugin:v1']}\n    ini: {Game: {'/Script/TBL.TBLGameMode': {BotBackfillEnabled: true}}}")
            .Replace("name: Arena", "name: Arena\n    mod-setup: {registries: ['local:two'], mods: ['org/two:v2'], dlls: []}")));
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
        Assert.Throws<ArgumentException>(() => Parse(Servers.Replace("7007", "7000")));
        Assert.Throws<ArgumentException>(() => Parse(Servers.Replace("name: Courtyard", "name: Courtyard\n    saved-dir-suffix: SAME")
            .Replace("name: Arena", "name: Arena\n    saved-dir-suffix: same")));
    }

    [Fact]
    public void ProfilesThatCollideAfterGameHyphenRemovalAreRejected() {
        Assert.Throws<ArgumentException>(() => Parse(Servers.Replace("name: Courtyard", "name: Courtyard\n    saved-dir-suffix: Host-one")
            .Replace("name: Arena", "name: Arena\n    saved-dir-suffix: Hostone")));
    }

    [Fact]
    public void InstanceLeasePreventsConcurrentReuseAndCanBeReacquiredAfterRelease() {
        var paths = Group().Servers[0].Options.InstancePaths;
        using (paths.AcquireLease()) Assert.Throws<IOException>(() => paths.AcquireLease());
        using (paths.AcquireLease()) Assert.True(Directory.Exists(paths.Root));
    }

    [Theory]
    [InlineData("--additional-pak-dir")]
    [InlineData("--additional-pak-dir=other")]
    public void ManagedPakDirectoryCannotBeOverriddenByForwardedArguments(string argument) {
        Assert.Throws<ArgumentException>(() => Parse(Servers + $"\ngame-args: ['{argument}']"));
    }

    [Fact]
    public void CliOverridesAreIgnoredAndUnsafeAuthorizationAppliesToEveryServer() {
        var group = Assert.IsType<ServerGroupOptions>(Parse(Servers, "server", "--name", "ignored", "--platform", "invalid",
            "--unsafe-very-dangerous-load-all-dlls"));
        Assert.All(group.Servers, entry => Assert.True(entry.Options.Shared.UnsafeLoadAllDlls));
        Assert.Equal("Courtyard", group.Servers[0].Options.Settings.Name);
        Assert.All(Group().Servers, entry => Assert.False(entry.Options.Shared.UnsafeLoadAllDlls));
    }

    [Fact]
    public void ClientAndRegistryQueriesSkipInactiveServersSchema() {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "query.yaml");
        File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\nservers: [invalid, server, schema]\nmod-setup: {registries: []}");
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
    public async Task StartupFailureStopsAlreadyLaunchedServers() {
        using var stop = new CancellationTokenSource();
        Task? exited = null;
        var calls = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ServerGroupRunner.RunAsync(Group(), stop,
            (_, _) => {
                if (++calls == 2) throw new InvalidOperationException("launch failed");
                var child = Child();
                exited = child.WaitForExitAsync();
                return Task.FromResult(child);
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
            return child;
        }, _ => new PendingQuery()).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(["Courtyard", "Arena"], order);
        await Task.WhenAll(exits).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NaturalExitsReturnFailureWithoutStoppingOtherServersEarly() {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var calls = 0;
        Assert.Equal(7, await ServerGroupRunner.RunAsync(Group(), stop,
            (_, _) => Task.FromResult(Child(++calls == 1 ? 7 : 0)), _ => new FailingQuery()));
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
                return Task.FromResult(first);
            }
            survivor = Child();
            survivorExit = survivor.WaitForExitAsync();
            return Task.FromResult(survivor);
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
}