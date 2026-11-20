using UnchainedLauncher.Core.JsonModels;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class LaunchCompositionTests {
    [Theory]
    [InlineData("server")]
    [InlineData("client")]
    public void BothLaunchTypesComposeSharedSettingsAndUseCorePlatform(string command) {
        var options = CliLaunchOptions.Parse([
            command, "--chivalry2-path", ".", "--platform", "EGS", "--saved-dir-suffix=Common",
            "--no-plugin-update", "--allow-unstable-plugin", "--mod=org/test:v1",
            "--dll=org/plugin:v2", "--add-registry=github:owner/repo", "--", "-log"
        ]);
        Assert.Equal(command == "server", options is ServerOptions);
        Assert.Equal(command == "client", options is ClientOptions);
        var shared = Assert.IsType<SharedLaunchOptions>(options.Shared);
        Assert.Equal(InstallationType.EpicGamesStore, shared.Platform);
        Assert.Equal("Common", shared.SavedDirSuffix);
        Assert.False(shared.UpdatePlugin);
        Assert.True(shared.AllowUnstablePlugin);
        Assert.Single(shared.Mods);
        Assert.Single(shared.Dlls);
        Assert.Single(shared.Registries);
        Assert.Equal(new[] { "-log" }, shared.GameArguments);
        var core = options.ToLaunchOptions();
        Assert.False(core.CheckForDependencyUpdates);
        Assert.True(core.AllowUnstablePluginReleases);
        Assert.Equal(command == "server", core.ServerLaunchOptions.IsSome);
    }

    [Fact]
    public void ServerHasNoClientModeFlagAndClientHasNoServerSettings() {
        Assert.Null(typeof(ServerOptions).GetProperty("Client"));
        Assert.Null(typeof(ClientOptions).GetProperty("Settings"));
        Assert.Null(typeof(ClientOptions).GetMethod("ToServerLaunchOptions"));
        Assert.Null(typeof(ServerOptions).GetProperty("UpdatePlugin"));
        Assert.Null(typeof(ClientOptions).GetProperty("UpdatePlugin"));
        Assert.Equal(typeof(InstallationType), typeof(SharedLaunchOptions).GetProperty("Platform")!.PropertyType);
    }
}