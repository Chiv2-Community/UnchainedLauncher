using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace UnchainedLauncher.CLI.Tests;

public sealed class PluginLoggingTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "plugin logging " + Guid.NewGuid().ToString("N"));

    private CliLaunchOptions Request(string command = "server", params string[] arguments) {
        var request = CliLaunchOptions.Parse([command, "--chivalry2-path", ".", "--platform", "Steam",
            "--saved-dir-suffix", "test-profile", "--", .. arguments]);
        request.Shared.Assets = new ProfileAssetPaths(request.Shared.SavedDirSuffix, _directory);
        return request;
    }

    [Fact]
    public async Task CreatesProfileJsonConfigurationAndPreservesEdits() {
        var request = Request();
        await PluginLogging.PrepareAsync(request, CancellationToken.None);
        var path = Path.Combine(_directory, "Chivalry 2", "Saved_testprofile", "unchained", "logging.yaml");
        Assert.Equal(path, request.InstancePaths.Logging);
        Assert.Contains("--log4rs-yaml " + ArgumentEscaper.Escape(path), request.ToLaunchOptions().LaunchArgs);
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var appenders = (YamlMappingNode)root["appenders"];
        Assert.Equal("json", ((YamlScalarNode)appenders["console"]["encoder"]["kind"]).Value);
        Assert.Equal("stdout", ((YamlScalarNode)appenders["console"]["target"]).Value);
        Assert.Equal("__UNCHAINED_LOG_PATH__", ((YamlScalarNode)appenders["file"]["path"]).Value);
        Assert.Equal("__KISMET_LOG_PATH__", ((YamlScalarNode)appenders["kismet"]["path"]).Value);
        await File.WriteAllTextAsync(path, "custom configuration\n");
        await PluginLogging.PrepareAsync(request, CancellationToken.None);
        Assert.Equal("custom configuration\n", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(request.InstancePaths.Root));
    }

    [Theory]
    [InlineData("--log4rs-yaml", false)]
    [InlineData("-log4rs-yaml", false)]
    [InlineData("--log4rs-yaml", true)]
    [InlineData("log4rs-yaml", true)]
    public async Task ExplicitConfigurationWinsWithoutCreatingOrDuplicatingDefaults(string flag, bool equals) {
        var request = Request("server", equals ? [flag + "=custom.yaml"] : [flag, "custom.yaml"]);
        await PluginLogging.PrepareAsync(request, CancellationToken.None);
        Assert.DoesNotContain(request.InstancePaths.Logging, request.ToLaunchOptions().LaunchArgs);
        Assert.Equal(1, request.ToLaunchOptions().LaunchArgs.Split("log4rs-yaml").Length - 1);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task ClientDoesNotCreateOrSelectCapturedLogging() {
        var request = Request("client");
        await PluginLogging.PrepareAsync(request, CancellationToken.None);
        Assert.DoesNotContain("log4rs-yaml", request.ToLaunchOptions().LaunchArgs);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task UnwritableConfigurationStopsPreparation() {
        var request = Request();
        Directory.CreateDirectory(request.InstancePaths.Root);
        Directory.CreateDirectory(request.InstancePaths.Logging);
        await Assert.ThrowsAnyAsync<IOException>(() => PluginLogging.PrepareAsync(request, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(request.InstancePaths.Root));
        Assert.Null(((ServerOptions)request).PluginLoggingPath);
    }

    [Fact]
    public async Task ProtonMapsTheGeneratedPathBeforeBuildingGameArguments() {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(_directory);
        var runner = Path.Combine(_directory, "proton");
        await File.WriteAllTextAsync(runner, """
            #!/bin/sh
            [ "$1" = runinprefix ] && [ "$2" = winepath ] && [ "$3" = -w ] || exit 1
            [ -f "$4" ] || exit 2
            printf '%s\n' 'C:\users\steamuser\AppData\Local\Chivalry 2\Saved_testprofile\unchained\logging.yaml'
            """ + "\n");
        File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var request = Request();
        request.Shared.Proton = new ProtonRuntime(runner, _directory, null, "unused");
        await PluginLogging.PrepareAsync(request, CancellationToken.None);
        Assert.Contains("--log4rs-yaml " + ArgumentEscaper.Escape(@"C:\users\steamuser\AppData\Local\Chivalry 2\Saved_testprofile\unchained\logging.yaml"),
            request.ToLaunchOptions().LaunchArgs);
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
