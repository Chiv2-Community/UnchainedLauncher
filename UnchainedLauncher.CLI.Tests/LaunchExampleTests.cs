using UnchainedLauncher.Core.Services.Mods.Registry;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class LaunchExampleTests {
    private const string Plugin = "Chiv2-Community/UnchainedPlugin:v1.0.0-RC3";
    private const string Framework = "Chiv2-Community/Unchained-Mods:v0.1.0";
    private const string Mod = "Chiv2-Community/Chiv2Turbo:v0.0.4";
    private const string Actor = "/Game/Mods/AgMods/Chiv2Turbo/Chiv2Turbo.Chiv2Turbo_C";

    [Theory]
    [InlineData("client")]
    [InlineData("server")]
    public void DirectLaunchExamplesSelectPakAndDllDependencies(string command) {
        var options = CliLaunchOptions.Parse([command, "--chivalry2-path", ".", "--platform", "Steam",
            "--dll", Plugin, "--mod", Framework, "--mod", Mod,
            .. command == "server" ? new[] { "--starting-map", "FFA_Courtyard", "--server-mod", Actor } : []]);
        AssertDependencies(options.Shared);
        if (options is ServerOptions server) {
            Assert.Equal("FFA_Courtyard", server.ToServerLaunchOptions().Map);
            Assert.Equal([Actor], server.ToServerLaunchOptions().ServerMods);
        }
    }

    [Theory]
    [InlineData("server.yaml")]
    [InlineData("servers.yaml")]
    public void YamlLaunchExamplesSelectPakDllAndActorDependencies(string file) {
        var command = CommandLineOptions.Parse(["server", "--config-path", Path.Combine(AppContext.BaseDirectory, "examples", file)]);
        var servers = command is ServerGroupOptions group ? group.Servers.Select(entry => entry.Options)
            : [Assert.IsType<ServerOptions>(command)];
        Assert.Equal(file == "servers.yaml" ? 2 : 1, servers.Count());
        Assert.All(servers, server => {
            AssertDependencies(server.Shared);
            Assert.Equal("FFA_Courtyard", server.ToServerLaunchOptions().Map);
            Assert.Equal([Actor], server.ToServerLaunchOptions().ServerMods);
        });
    }

    private static void AssertDependencies(SharedLaunchOptions options) {
        Assert.Equal(new[] { new ReleaseCoordinates("Chiv2-Community", "Unchained-Mods", "v0.1.0"),
            new ReleaseCoordinates("Chiv2-Community", "Chiv2Turbo", "v0.0.4") }, options.Mods);
        Assert.Equal(new ReleaseCoordinates("Chiv2-Community", "UnchainedPlugin", "v1.0.0-RC3"), Assert.Single(options.Dlls));
    }
}