using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class BaseCliScopeTests {
    [Theory]
    [InlineData("client")]
    [InlineData("server")]
    public void LaunchConfigurationFilesAreNotAcceptedByTheBaseCli(string command) {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse([
            command, "--chivalry2-path", ".", "--platform", "Steam", "--config-path", "launch.yaml"]));
        var help = CommandLineOptions.Parse([command, "--help"]);
        Assert.DoesNotContain("--config-path", help.HelpText);
    }

    [Theory]
    [InlineData("Steam")]
    [InlineData("steam")]
    [InlineData("EGS")]
    [InlineData("EpicGamesStore")]
    public void PlatformAliasesDoNotRequireYamlParsing(string platform) {
        var options = CliLaunchOptions.Parse(["client", "--chivalry2-path", ".", "--platform", platform]);
        Assert.Equal(platform.Equals("steam", StringComparison.OrdinalIgnoreCase)
            ? Core.JsonModels.InstallationType.Steam : Core.JsonModels.InstallationType.EpicGamesStore, options.Shared.Platform);
    }
}
