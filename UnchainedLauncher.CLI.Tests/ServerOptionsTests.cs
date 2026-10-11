using UnchainedLauncher.Core.Services.Mods.Registry;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class ServerOptionsTests {
    private static string[] Args(params string[] extra) =>
        new[] { "server", "--chivalry2-path", ".", "--platform", "Steam" }.Concat(extra).ToArray();

    [Fact]
    public void DefaultsProduceHeadlessServerLaunch() {
        var options = ServerOptions.Parse(Args());
        var server = options.ToServerLaunchOptions();
        Assert.True(server.Headless);
        Assert.False(server.RegisterWithBackend);
        Assert.Equal(7777, server.GamePort);
        Assert.Equal(7071, server.QueryPort);
        Assert.Equal("FFA_Courtyard", server.Map);
        Assert.Equal("TBL -server", options.ToLaunchOptions().LaunchArgs);
        Assert.Equal("UnchainedCLI", options.ToLaunchOptions().SavedDirSuffix.IfNone(""));
        Assert.Contains(server.ToCLIArgs(), arg => arg.Rendered == "-nullrhi");
    }

    [Theory]
    [InlineData("Steam", GamePlatform.Steam)]
    [InlineData("steam", GamePlatform.Steam)]
    [InlineData("EGS", GamePlatform.EGS)]
    public void AcceptsPlatforms(string platform, GamePlatform expected) {
        Assert.Equal(expected, ServerOptions.Parse(["server", "--chivalry2-path=.", $"--platform={platform}"]).Platform);
    }

    [Fact]
    public void RepeatedCoordinatesRegistriesAndActorsAreAccepted() {
        var options = ServerOptions.Parse(Args("--mod", "org/one:v1.2.3", "--mod=org/two:release-tag",
            "--dll", "org/plugin:v2", "--add-registry", "github:owner/repo",
            "--add-registry", "local:my registry", "--add-registry", "config:registries.json",
            "--server-mod", "/Game/Mods/Test.Test_C", "--server-mod", "/Game/Mods/Other.Other_C"));
        Assert.Equal(2, options.Mods.Count);
        Assert.Single(options.Dlls);
        Assert.Equal(3, options.Registries.Count);
        Assert.Equal(2, options.ToServerLaunchOptions().ServerMods.Count());
        Assert.Equal("v1.2.3", options.Mods[0].Version);
        Assert.True(Path.IsPathFullyQualified(options.Registries[1].Location));
    }

    [Fact]
    public void GameArgumentsAreEscapedAndNotParsedAsLauncherOptions() {
        var options = ServerOptions.Parse(Args("--", "-AUTH_PASSWORD=secret with spaces", "--mod", "game value"));
        Assert.Empty(options.Mods);
        Assert.Equal(3, options.GameArguments.Count);
        Assert.Contains("\"-AUTH_PASSWORD=secret with spaces\"", options.ToLaunchOptions().LaunchArgs);
        Assert.Contains("\"game value\"", options.ToLaunchOptions().LaunchArgs);
    }

    [Fact]
    public void ServerFieldsAreMappedToCoreOptions() {
        var options = ServerOptions.Parse(Args("--name", "Test Server", "--description", "Test MOTD", "--map", "TO_Test",
            "--password", "secret", "--local-ip", "127.0.0.2", "--game-port", "10001", "--ping-port", "10002",
            "--query-port", "10003", "--rcon-port", "10004", "--register", "--no-tui", "--no-plugin-update",
            "--allow-unstable-plugin", "--server-browser-backend", "https://example.test", "--saved-dir-suffix", "Test_1"));
        var server = options.ToServerLaunchOptions();
        Assert.Equal("Test Server", server.Name);
        Assert.Equal("Test MOTD", server.Description);
        Assert.Equal("TO_Test", server.Map);
        Assert.Equal("secret", server.Password.IfNone(""));
        Assert.Equal("127.0.0.2", server.LocalIp.IfNone(""));
        Assert.Equal((10001, 10002, 10003, 10004), (server.GamePort, server.BeaconPort, server.QueryPort, server.RconPort));
        Assert.True(server.RegisterWithBackend);
        Assert.True(options.NoTui);
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
    public void RejectsInvalidValues(string key, string value) {
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(Args(key, value)));
    }

    [Fact]
    public void RejectsMissingRequiredOptionsAndUnknownFlags() {
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["server"]));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["server", "--chivalry2-path", "."]));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(Args("--typo")));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(Args("--map")));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(Args("--no-tui=true")));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["client"]));
    }

    [Fact]
    public void RejectsConflictingModVersionsAndDuplicateScalarOptions() {
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(Args("--mod", "org/mod:v1", "--mod", "org/mod:v2")));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(Args("--map", "one", "--map", "two")));
    }

    [Fact]
    public void HelpDoesNotRequireInstallation() {
        Assert.True(ServerOptions.Parse([]).Help);
        Assert.True(ServerOptions.Parse(["--help"]).Help);
        Assert.True(ServerOptions.Parse(["server", "--help"]).Help);
    }

    [Fact]
    public void GitHubRegistryCreatesCoreImplementation() {
        var registry = Assert.IsType<GithubModRegistry>(RegistrySpec.Parse("github:owner/repo").Create());
        Assert.Equal("owner", registry.Organization);
        Assert.Equal("repo", registry.RepoName);
    }
}