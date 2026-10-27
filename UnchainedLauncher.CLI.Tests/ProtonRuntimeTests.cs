using System.Diagnostics;
using Xunit;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ProtonRuntimeTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "proton-tests-" + Guid.NewGuid().ToString("N"));
    private string Steam => Path.Combine(_root, "Steam");
    private string Library => Path.Combine(_root, "Other library");
    private string Game => Path.Combine(Library, "steamapps/common/Chivalry 2");
    private string Helper => Path.Combine(_root, "helper.exe");

    public ProtonRuntimeTests() {
        Directory.CreateDirectory(Game);
        Write(Path.Combine(Steam, "steamapps/libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{Library.Replace("\\", "\\\\")}\" }} }}");
        Write(Path.Combine(Library, "steamapps/appmanifest_1824220.acf"),
            "\"AppState\" { \"appid\" \"1824220\" \"installdir\" \"Chivalry 2\" }");
        Write(Path.Combine(Library, "steamapps/compatdata/1824220/pfx/system.reg"), "");
        Write(Path.Combine(Steam, "steamapps/common/Proton - Experimental/proton"), "");
        Write(Helper, "");
    }

    private CliLaunchOptions Options(params string[] extra) => CliLaunchOptions.Parse(
        ["server", "--chivalry2-path", Game, "--platform", "Steam", "--steam-path", Steam,
         "--proton-helper", Helper, .. extra]);

    [Fact]
    public void DiscoversPrefixInTheGameLibraryRatherThanClientLibrary() {
        var runtime = ProtonRuntime.Discover(Options().Shared, _ => null);
        Assert.Equal(Path.GetFullPath(Path.Combine(Library, "steamapps/compatdata/1824220")), runtime.CompatDataPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(Steam, "steamapps/common/Proton - Experimental/proton")), runtime.Executable);
    }

    [Fact]
    public void HonorsSteamToolSelectionRatherThanPickingAnotherInstalledProton() {
        Write(Path.Combine(Steam, "steamapps/common/Proton 10.0/proton"), "");
        Write(Path.Combine(Steam, "config/config.vdf"), """
            "InstallConfigStore" { "Software" { "Valve" { "Steam" {
              "CompatToolMapping" { "1824220" { "name" "proton_experimental" } }
            } } } }
            """);
        Assert.EndsWith(Path.Combine("Proton - Experimental", "proton"), ProtonRuntime.Discover(Options().Shared, _ => null).Executable);
    }

    [Fact]
    public void RefusesAmbiguousToolSelection() {
        Write(Path.Combine(Steam, "steamapps/common/Proton 10.0/proton"), "");
        Assert.Contains("--proton-path", Assert.Throws<InvalidOperationException>(() =>
            ProtonRuntime.Discover(Options().Shared, _ => null)).Message);
    }

    [Fact]
    public void ExplicitPathsOverrideSteamEnvironment() {
        var compat = Path.Combine(_root, "explicit prefix");
        Write(Path.Combine(compat, "pfx/system.reg"), "");
        var proton = Path.Combine(_root, "custom tool/proton");
        Write(proton, "");
        var options = Options("--compat-data-path", compat, "--proton-path", proton);
        var runtime = ProtonRuntime.Discover(options.Shared, _ => "/nonexistent");
        Assert.Equal(compat, runtime.CompatDataPath);
        Assert.Equal(Path.GetFullPath(proton), runtime.Executable);
        Assert.Equal(Steam, runtime.SteamPath);
    }

    [Fact]
    public void HonorsStandardCompatDataAndToolEnvironment() {
        var compat = Path.Combine(_root, "environment prefix");
        Write(Path.Combine(compat, "pfx/system.reg"), "");
        var tool = Path.Combine(_root, "environment tool");
        Write(Path.Combine(tool, "proton"), "");
        var runtime = ProtonRuntime.Discover(Options().Shared, key => key switch {
            "STEAM_COMPAT_DATA_PATH" => compat,
            "STEAM_COMPAT_TOOL_PATHS" => tool,
            _ => null
        });
        Assert.Equal(compat, runtime.CompatDataPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(tool, "proton")), runtime.Executable);
    }

    [Fact]
    public void ResolvesSteamLibrarySymlinksWithoutChangingPrefixLayout() {
        if (!OperatingSystem.IsLinux()) return;
        var alias = Path.Combine(_root, "library-alias");
        Directory.CreateSymbolicLink(alias, Library);
        var options = CliLaunchOptions.Parse(["server", "--chivalry2-path",
            Path.Combine(alias, "steamapps/common/Chivalry 2"), "--platform", "Steam",
            "--steam-path", Steam, "--proton-helper", Helper]);
        Assert.Equal(Path.Combine(Library, "steamapps/compatdata/1824220"),
            ProtonRuntime.Discover(options.Shared, _ => null).CompatDataPath);
    }

    [Fact]
    public void SteamRootSymlinksDoNotMakeOneProtonToolAmbiguous() {
        if (!OperatingSystem.IsLinux()) return;
        var alias = Path.Combine(_root, "steam-alias");
        Directory.CreateSymbolicLink(alias, Steam);
        var text = $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{Steam}\" }} \"1\" {{ \"path\" \"{Library}\" }} }}";
        Write(Path.Combine(Steam, "steamapps/libraryfolders.vdf"), text);
        var options = CliLaunchOptions.Parse(["server", "--chivalry2-path", Game, "--platform", "Steam",
            "--steam-path", alias, "--proton-helper", Helper]);
        Assert.EndsWith("Proton - Experimental/proton", ProtonRuntime.Discover(options.Shared, _ => null).Executable);
    }

    [Fact]
    public void DiscoversNamedCustomCompatibilityTools() {
        var tool = Path.Combine(Steam, "compatibilitytools.d/Custom Tool");
        Write(Path.Combine(tool, "proton"), "");
        Write(Path.Combine(tool, "compatibilitytool.vdf"), """
            "compatibilitytools" { "compat_tools" { "my_custom_tool" { "display_name" "Custom Tool" } } }
            """);
        Write(Path.Combine(Steam, "config/config.vdf"), """
            "InstallConfigStore" { "Software" { "Valve" { "Steam" {
              "CompatToolMapping" { "1824220" { "name" "my_custom_tool" } }
            } } } }
            """);
        Assert.Equal(Path.GetFullPath(Path.Combine(tool, "proton")), ProtonRuntime.Discover(Options().Shared, _ => null).Executable);
    }

    [Fact]
    public void RequiresInitializedPrefixWithoutCreatingAnArbitraryWineUser() {
        var empty = Path.Combine(_root, "empty");
        var error = Assert.Throws<InvalidOperationException>(() =>
            ProtonRuntime.Discover(Options("--compat-data-path", empty).Shared, _ => null));
        Assert.Contains("Steam once", error.Message);
        Assert.False(Directory.Exists(empty));
    }

    [Fact]
    public void ProcessArgumentsAreNotShellExpandedAndDoNotEnableUeLogging() {
        var runtime = ProtonRuntime.Discover(Options().Shared, _ => null);
        var args = new[] { "run", "/path with spaces/helper.exe", "launch", "$(touch bad);request.json" };
        var info = runtime.StartInfo(args);
        Assert.Equal(args, info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput);
        Assert.Equal(Path.Combine(runtime.CompatDataPath, "pfx"), info.Environment["WINEPREFIX"]);
        Assert.DoesNotContain("-log", info.ArgumentList);
        Assert.DoesNotContain("--log4rs-yaml", info.ArgumentList);
    }

    [Fact]
    public void ParsesSteamQuotedEscapesCommentsAndUnquotedValues() {
        var parsed = SteamKeyValues.Parse("""
            // comment
            "root" { "path" "Z:\\data\"quoted" name proton_experimental nested { value 123 } }
            """);
        Assert.Equal("Z:\\data\"quoted", parsed.At("root", "path")?.Value);
        Assert.Equal("proton_experimental", parsed.At("root", "name")?.Value);
        Assert.Equal("123", parsed.At("ROOT", "nested", "value")?.Value);
    }

    [Theory]
    [InlineData("\"root\" {")]
    [InlineData("\"unterminated")]
    [InlineData("key }")]
    public void RejectsMalformedSteamMetadata(string content) =>
        Assert.Throws<InvalidDataException>(() => SteamKeyValues.Parse(content));

    [Fact]
    public void GameFilePathsUsePortableSeparators() {
        Assert.DoesNotContain('\\', FilePaths.GameBinPath);
        Assert.DoesNotContain('\\', FilePaths.PakDir);
    }

    [Fact]
    public void IpcDirectoryIsPrivateOnLinux() {
        if (!OperatingSystem.IsLinux()) return;
        var directory = ProtonRuntime.CreatePrivateDirectory();
        try {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(directory));
        }
        finally { Directory.Delete(directory); }
    }

    [Fact]
    public async Task GameMetricsAndPidAreNotTakenFromProtonSupervisor() {
        if (!OperatingSystem.IsLinux()) return;
        using var supervisor = Process.Start(new ProcessStartInfo("sleep") { ArgumentList = { "60" } })!;
        using var metrics = Process.GetCurrentProcess();
        using var game = new LaunchedGame(supervisor, metrics, () => {
            supervisor.Kill();
            return Task.CompletedTask;
        });
        Assert.Equal(Environment.ProcessId, game.Id);
        Assert.Same(metrics, game.Metrics);
        await game.StopAsync();
        Assert.True(game.HasExited);
    }

    [Fact]
    public void UnixPidLookupRequiresBothLaunchIdentityAndGameExecutable() {
        if (!OperatingSystem.IsLinux()) return;
        var proc = Path.Combine(_root, "proc");
        var directory = Path.Combine(proc, Environment.ProcessId.ToString());
        Write(Path.Combine(directory, "environ"), "UNCHAINED_LAUNCH_ID=expected\0OTHER=value\0");
        Write(Path.Combine(directory, "cmdline"), "Z:\\game\\NotTheGame.exe\0argument\0");
        Assert.Null(ProtonGameLauncher.FindGame("expected", proc));
        Write(Path.Combine(directory, "cmdline"), "Z:\\game\\Chivalry2-Win64-Shipping.exe\0argument\0");
        Assert.Null(ProtonGameLauncher.FindGame("different", proc));
        using var found = ProtonGameLauncher.FindGame("expected", proc);
        Assert.Equal(Environment.ProcessId, found?.Id);
    }

    [Fact]
    public async Task CancellingWinePathQueryReapsOnlyItsOwnProcess() {
        if (!OperatingSystem.IsLinux()) return;
        var script = Path.Combine(_root, "fake-proton");
        var pidFile = Path.Combine(_root, "pid");
        Write(script, $"#!/bin/sh\necho $$ > '{pidFile}'\nexec sleep 60\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runtime = new ProtonRuntime(script, _root, Steam, Helper);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.WinePathAsync("/some path", true, stop.Token));
        var pid = int.Parse(File.ReadAllText(pidFile));
        Assert.False(Directory.Exists("/proc/" + pid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("/absolute\nextra-output")]
    public async Task RejectsInvalidWinePathReplies(string reply) {
        if (!OperatingSystem.IsLinux()) return;
        var script = Path.Combine(_root, "fake-proton");
        Write(script, "#!/bin/sh\nprintf '%s' '" + reply + "'\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runtime = new ProtonRuntime(script, _root, Steam, Helper);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.WinePathAsync("C:\\path", false, CancellationToken.None));
    }

    [Fact]
    public async Task WinePathInputRemainsOneLiteralArgument() {
        if (!OperatingSystem.IsLinux()) return;
        var script = Path.Combine(_root, "fake-proton");
        Write(script, "#!/bin/sh\nprintf '%s' \"$4\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runtime = new ProtonRuntime(script, _root, Steam, Helper);
        const string path = "/path with spaces/$(not-a-command);literal";
        Assert.Equal(path, await runtime.WinePathAsync(path, false, CancellationToken.None));
    }

    [Fact]
    public void YamlSupportsExplicitProtonPaths() {
        var path = Path.Combine(_root, "launch.yaml");
        Write(path, $"""
            chiv2-path: '{Game}'
            platform: Steam
            proton:
              path: '/tool/proton'
              compat-data-path: '/library/compatdata/1824220'
            server:
              headless: true
            """);
        var options = Assert.IsType<ServerOptions>(YamlConfiguration.Load(path, client: false));
        Assert.Equal(Path.GetFullPath("/tool/proton", Path.GetDirectoryName(path)!), options.Shared.ProtonConfiguration.Path);
        Assert.Equal(Path.GetFullPath("/library/compatdata/1824220", Path.GetDirectoryName(path)!), options.Shared.ProtonConfiguration.CompatDataPath);
    }

    [Fact]
    public void YamlProtonPathsResolveRelativeToTheYamlFile() {
        var path = Path.Combine(_root, "configs/launch.yaml");
        Write(path, $"""
            chiv2-path: '{Game}'
            platform: Steam
            proton:
              path: ../tool/proton
              compat-data-path: ../compat
            server:
              headless: true
            """);
        var options = Assert.IsType<ServerOptions>(YamlConfiguration.Load(path, client: false));
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "tool/proton")), options.Shared.ProtonConfiguration.Path);
        Assert.Equal(Path.Combine(_root, "compat"), options.Shared.ProtonConfiguration.CompatDataPath);
    }

    private static void Write(string path, string text) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
