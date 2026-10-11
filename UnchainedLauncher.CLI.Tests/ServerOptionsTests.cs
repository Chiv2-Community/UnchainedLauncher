using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Mods.Registry;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class ServerOptionsTests {
    private static string[] Args(params string[] extra) =>
        new[] { "server", "--chivalry2-path", ".", "--platform", "Steam" }.Concat(extra).ToArray();

    [Fact]
    public void DefaultsProduceHeadlessServerLaunch() {
        var options = Assert.IsType<ServerOptions>(CliLaunchOptions.Parse(Args()));
        var server = options.ToServerLaunchOptions();
        Assert.True(server.Headless);
        Assert.False(server.RegisterWithBackend);
        Assert.True(server.DesyncPatch);
        Assert.True(server.UseBackendBanlist);
        Assert.NotNull(options.Settings.LocalIp);
        Assert.Equal(options.Settings.LocalIp, server.LocalIp.IfNone(""));
        Assert.Equal(7777, server.GamePort);
        Assert.Equal(7071, server.QueryPort);
        Assert.Equal("FFA_Courtyard", server.Map);
        Assert.Equal("TBL -server", options.ToLaunchOptions().LaunchArgs);
        Assert.Equal("UnchainedCLI", options.ToLaunchOptions().SavedDirSuffix.IfNone(""));
        Assert.Contains(server.ToCLIArgs(), arg => arg.Rendered == "-nullrhi");
        Assert.Contains(server.ToCLIArgs(), arg => arg.Rendered == "--desync-patch");
    }

    [Theory]
    [InlineData("Steam", InstallationType.Steam)]
    [InlineData("steam", InstallationType.Steam)]
    [InlineData("EGS", InstallationType.EpicGamesStore)]
    [InlineData("EpicGamesStore", InstallationType.EpicGamesStore)]
    public void AcceptsPlatforms(string platform, InstallationType expected) {
        Assert.Equal(expected, CliLaunchOptions.Parse(["server", "--chivalry2-path=.", $"--platform={platform}"]).Shared.Platform);
    }

    [Fact]
    public void RepeatedCoordinatesRegistriesAndActorsAreAccepted() {
        var options = CliLaunchOptions.Parse(Args("--mod", "org/one:v1.2.3", "--mod=org/two:release-tag",
            "--dll", "org/plugin:v2", "--add-registry", "github:owner/repo",
            "--add-registry", "local:my registry", "--add-registry", "config:registries.json",
            "--server-mod", "/Game/Mods/Test.Test_C", "--server-mod", "/Game/Mods/Other.Other_C"));
        Assert.Equal(2, options.Shared.Mods.Count);
        Assert.Single(options.Shared.Dlls);
        Assert.Equal(3, options.Shared.Registries.Count);
        Assert.Equal(2, Assert.IsType<ServerOptions>(options).ToServerLaunchOptions().ServerMods.Count());
        Assert.Equal("v1.2.3", options.Shared.Mods[0].Version);
        Assert.True(Path.IsPathFullyQualified(options.Shared.Registries[1].Location));
    }

    [Fact]
    public void GameArgumentsAreEscapedAndNotParsedAsLauncherOptions() {
        var options = CliLaunchOptions.Parse(Args("--", "-AUTH_PASSWORD=secret with spaces", "--mod", "game value"));
        Assert.Empty(options.Shared.Mods);
        Assert.Equal(3, options.GameArguments.Count);
        Assert.Contains("\"-AUTH_PASSWORD=secret with spaces\"", options.ToLaunchOptions().LaunchArgs);
        Assert.Contains("\"game value\"", options.ToLaunchOptions().LaunchArgs);
    }

    [Fact]
    public void ServerFieldsAreMappedToCoreOptions() {
        var options = CliLaunchOptions.Parse(Args("--name", "Test Server", "--description", "Test MOTD", "--map", "TO_Test",
            "--password", "secret", "--local-ip", "127.0.0.2", "--game-port", "10001", "--ping-port", "10002",
            "--query-port", "10003", "--rcon-port", "10004", "--register", "--no-tui", "--no-plugin-update",
            "--allow-unstable-plugin", "--server-browser-backend", "https://example.test", "--saved-dir-suffix", "Test_1"));
        var server = Assert.IsType<ServerOptions>(options).ToServerLaunchOptions();
        Assert.Equal("Test Server", server.Name);
        Assert.Equal("Test MOTD", server.Description);
        Assert.Equal("TO_Test", server.Map);
        Assert.Equal("secret", server.Password.IfNone(""));
        Assert.Equal("127.0.0.2", server.LocalIp.IfNone(""));
        Assert.Equal((10001, 10002, 10003, 10004), (server.GamePort, server.BeaconPort, server.QueryPort, server.RconPort));
        Assert.True(server.RegisterWithBackend);
        Assert.True(Assert.IsType<ServerOptions>(options).NoTui);
        Assert.False(options.ToLaunchOptions().CheckForDependencyUpdates);
        Assert.True(options.ToLaunchOptions().AllowUnstablePluginReleases);
    }

    [Theory]
    [InlineData("--mod", "missing-version")]
    [InlineData("--mod", "org/mod:")]
    [InlineData("--dll", "plugin:v1")]
    [InlineData("--platform", "42")]
    [InlineData("--game-port", "0")]
    [InlineData("--game-port", "65536")]
    [InlineData("--query-port", "7777")]
    [InlineData("--saved-dir-suffix", "../profile")]
    [InlineData("--local-ip", "not-an-ip")]
    [InlineData("--server-browser-backend", "file:///server")]
    [InlineData("--add-registry", "github:bad")]
    [InlineData("--add-registry", "http:unsupported")]
    [InlineData("--name", " ")]
    [InlineData("--headless", "yes")]
    [InlineData("--censor-mode", "invalid")]
    [InlineData("--discord-mention-admins", "maybe")]
    public void RejectsInvalidValues(string key, string value) {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(Args(key, value)));
    }

    [Fact]
    public void RejectsMissingRequiredOptionsAndUnknownFlags() {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server"]));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--chivalry2-path", "."]));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(Args("--typo")));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(Args("--map")));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(Args("--no-tui=true")));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["client"]));
    }

    [Fact]
    public void RejectsConflictingModVersionsAndDuplicateScalarOptions() {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(Args("--mod", "org/mod:v1", "--mod", "org/mod:v2")));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(Args("--map", "one", "--map", "two")));
    }

    [Fact]
    public void HelpDoesNotRequireInstallation() {
        Assert.True(CliLaunchOptions.Parse([]).Help);
        Assert.True(CliLaunchOptions.Parse(["--help"]).Help);
        Assert.True(CliLaunchOptions.Parse(["server", "--help"]).Help);
    }

    [Fact]
    public void GitHubRegistryCreatesCoreImplementation() {
        var registry = Assert.IsType<GithubModRegistry>(RegistrySpec.Parse("github:owner/repo").Create());
        Assert.Equal("owner", registry.Organization);
        Assert.Equal("repo", registry.RepoName);
    }

    [Fact]
    public void ExtendedServerArgumentsAreMappedToCore() {
        var options = CliLaunchOptions.Parse(Args("--headless=false", "--no-desync-patch",
            "--censor-mode=standard", "--discord-bot-token=token",
            "--discord-general-channel-id=123", "--discord-mention-admins=false"));
        var server = Assert.IsType<ServerOptions>(options).ToServerLaunchOptions();
        Assert.False(server.Headless);
        Assert.False(server.DesyncPatch);
        Assert.True(server.UseBackendBanlist);
        Assert.Equal(Core.Services.Processes.Chivalry.CensorArg.Standard, server.CensorMode);
        Assert.Null(server.FFATimeLimit);
        Assert.Null(server.PlayerBotCount);
        Assert.True(server.DiscordIntegration.IsSome);
        Assert.Contains("--discord-mention-admins false", server.ToCLIArgs().Select(arg => arg.Rendered));
        Assert.DoesNotContain("-nullrhi", server.ToCLIArgs().Select(arg => arg.Rendered));
        Assert.DoesNotContain("--desync-patch", server.ToCLIArgs().Select(arg => arg.Rendered));
    }

    [Theory]
    [InlineData("--headless=false")]
    [InlineData("--headless", "false")]
    public void RenderingRequiresExplicitlyDisablingDesyncPatch(params string[] arguments) {
        var error = Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(Args(arguments)));
        Assert.Contains("headless", error.Message);
        Assert.Contains("--no-desync-patch", error.Message);
        Assert.Contains("server.desync-patch: false", error.Message);
        var options = CliLaunchOptions.Parse(Args(arguments.Concat(["--no-desync-patch"]).ToArray()));
        var server = Assert.IsType<ServerOptions>(options).ToServerLaunchOptions();
        Assert.False(server.Headless);
        Assert.False(server.DesyncPatch);
        Assert.True(server.UseBackendBanlist);
        Assert.DoesNotContain("-nullrhi", server.ToCLIArgs().Select(arg => arg.Rendered));
        Assert.DoesNotContain("--desync-patch", server.ToCLIArgs().Select(arg => arg.Rendered));
        Assert.True(options.ToLaunchOptions().ServerLaunchOptions.IsSome);
    }

    [Fact]
    public void DefaultEnabledServerFlagsCanBeDisabledOnCommandLine() {
        var options = CliLaunchOptions.Parse(Args("--no-desync-patch", "--no-backend-banlist"));
        Assert.False(Assert.IsType<ServerOptions>(options).ToServerLaunchOptions().DesyncPatch);
        Assert.False(Assert.IsType<ServerOptions>(options).ToServerLaunchOptions().UseBackendBanlist);
    }

    [Fact]
    public void ClientLaunchDoesNotApplyServerDesyncCompatibilityPolicy() {
        var options = Assert.IsType<ClientOptions>(CliLaunchOptions.Parse([
            "client", "--chivalry2-path", ".", "--platform", "Steam"
        ]));
        var launch = options.ToLaunchOptions();
        Assert.True(launch.ServerLaunchOptions.IsNone);
        Assert.Equal("TBL", launch.LaunchArgs);
    }

    [Theory]
    [InlineData("--no-desync-patch=false")]
    [InlineData("--no-backend-banlist=true")]
    [InlineData("--desync-patch")]
    [InlineData("--use-backend-banlist")]
    public void DisableOptionsAreValueLessFlags(string flag) {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(Args(flag)));
    }
}