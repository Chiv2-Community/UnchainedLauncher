using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class CommandLineOptionsTests {
    [Theory]
    [InlineData("server")]
    [InlineData("client")]
    public void GeneratedHelpIsSpecificToTheCommand(string command) {
        var options = CliLaunchOptions.Parse([command, "--help"]);
        Assert.True(options.Help);
        Assert.Contains("--chivalry2-path", options.HelpText);
        Assert.Contains("--config-path", options.HelpText);
        Assert.Equal(command == "server", options.HelpText.Contains("--no-desync-patch"));
        Assert.Equal(command == "server", options.HelpText.Contains("--discord-bot-token"));
    }

    [Fact]
    public void RootHelpListsBothCommands() {
        var options = CliLaunchOptions.Parse([]);
        Assert.True(options.Help);
        Assert.Contains("server", options.HelpText);
        Assert.Contains("client", options.HelpText);
    }

    [Theory]
    [InlineData("--no-desync-patch")]
    [InlineData("--name=test")]
    [InlineData("--game-port=7778")]
    [InlineData("--no-tui")]
    public void ServerSpecificOptionsAreRejectedByClient(string flag) {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse([
            "client", "--chivalry2-path", ".", "--platform", "Steam", flag
        ]));
    }

    [Theory]
    [InlineData("--no-desync-patch")]
    [InlineData("--no-plugin-update")]
    [InlineData("--name=test")]
    public void SingletonOptionsCannotBeRepeated(string flag) {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse([
            "server", "--chivalry2-path", ".", "--platform", "Steam", flag, flag
        ]));
    }

    [Fact]
    public void HelpAndInvalidCliAlsoUseOneLibraryParse() {
        foreach (var args in new string[][] { [], ["client", "--help"], ["server", "--platform=Invalid"] }) {
            var calls = 0;
            CliLaunchOptions Parse() => CommandLineOptions.Parse(args, (root, tokens, configuration) => {
                calls++;
                return root.Parse(tokens, configuration);
            });
            if (args.Contains("--platform=Invalid")) Assert.Throws<ArgumentException>(Parse);
            else Assert.True(Parse().Help);
            Assert.Equal(1, calls);
        }
    }

    [Fact]
    public void GameArgumentsMustFollowSeparatorAndArePreservedExactly() {
        string[] prefix = ["server", "--chivalry2-path", ".", "--platform", "Steam"];
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse([.. prefix, "unexpected"]));
        string[] forwarded = ["--help", "--config-path", "not-a-launcher-config", "-AUTH_PASSWORD=with spaces", "", "@game-file"];
        var options = CliLaunchOptions.Parse([.. prefix, "--", .. forwarded]);
        Assert.False(options.Help);
        Assert.Equal(forwarded, options.GameArguments);
    }
}