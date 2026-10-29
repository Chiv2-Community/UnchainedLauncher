using System.Diagnostics;
using System.Text.Json;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using Xunit;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ArgumentBoundaryTests : IDisposable {
    private readonly string _directory = Path.Combine(
        Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "argument-tests-" + Guid.NewGuid().ToString("N"));
    private static string Probe => Path.Combine(AppContext.BaseDirectory, "argument-probe",
        OperatingSystem.IsWindows() ? "ArgumentProbe.exe" : "ArgumentProbe");

    public ArgumentBoundaryTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    public static IEnumerable<object[]> ForwardedArguments() {
        string[] arguments = ["", "\"", "a\"b", "a\\\"b", "a\\\\\"b", "\\", "tail\\\\", "space tail\\",
            "-UserDir=C:\\Server Profiles\\", "tab\tvalue", "line\nbreak", "line\r\nbreak", " \t\n ",
            "--", "-AUTH_PASSWORD=secret \"quoted\"\\", "& echo injected; $(echo injected) | >file"];
        foreach (var command in new[] { "client", "server" })
            foreach (var yaml in new[] { false, true })
                foreach (var argument in arguments)
                    yield return [command, yaml, argument];
    }

    [Theory]
    [MemberData(nameof(ForwardedArguments))]
    public async Task ForwardingPreservesExactProcessArguments(string command, bool yaml, string argument) {
        // Repeat the value so YAML exercises both shared and command-specific forwarding.
        var options = Parse(command, yaml, [argument, argument, "sentinel-after-forwarded"]);
        var expected = new List<string> { "TBL" };
        if (command == "server") expected.AddRange(["-server", "--attach-stdio", "--log4rs-yaml", options.InstancePaths.Logging]);
        if (command == "server") expected.AddRange(FirstRunArguments);
        expected.AddRange([argument, argument, "sentinel-after-forwarded", "-saveddirsuffix=" + options.Shared.SavedDirSuffix, "-unchained"]);
        if (command == "server") expected.AddRange(DefaultServerArguments());
        expected.AddRange(["--server-browser-backend", "https://servers.polehammer.net"]);

        // Independent .NET ArgumentList control: no local escaping or parsing is used to form expected argv.
        Assert.Equal(expected, await CaptureControl(expected));
        Assert.Equal(expected, await CaptureLaunch(options.ToLaunchOptions()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedServerValuesAlsoPreserveBoundaries(bool yaml) {
        const string value = "quoted \"name\"\\\tline\nend\\";
        var options = Assert.IsType<ServerOptions>(Parse("server", yaml, [], value));
        var expected = new List<string> { "TBL", "-server", "--attach-stdio", "--log4rs-yaml", options.InstancePaths.Logging };
        expected.AddRange(FirstRunArguments);
        expected.AddRange(["-saveddirsuffix=UnchainedCLI", "-unchained"]);
        expected.AddRange(DefaultServerArguments(value, value, value));
        // Password follows the headless/backend flags, before local IP and desync options.
        expected.Insert(expected.IndexOf("--local-ip"), "ServerPassword=" + value);
        expected.AddRange(["--server-browser-backend", "https://servers.polehammer.net"]);
        Assert.Equal(expected, await CaptureControl(expected));
        Assert.Equal(expected, await CaptureLaunch(options.ToLaunchOptions()));
    }

    [Fact]
    public async Task NativePakDirectoryDoesNotRequireAProcessArgument() {
        var installation = Path.Combine(_directory, "Game With Spaces");
        var options = CliLaunchOptions.Parse(["server", "--chivalry2-path", installation, "--platform", "Steam", "--local-ip", "127.0.0.1"]);
        var expected = new List<string> { "TBL", "-server", "--attach-stdio", "--log4rs-yaml", options.InstancePaths.Logging };
        expected.AddRange(FirstRunArguments);
        expected.AddRange(["-saveddirsuffix=UnchainedCLI", "-unchained"]);
        expected.AddRange(DefaultServerArguments());
        expected.AddRange(["--server-browser-backend", "https://servers.polehammer.net"]);
        Assert.Equal(expected, await CaptureLaunch(options.ToLaunchOptions()));
    }

    [Theory]
    [InlineData("  SomeStructuredThing(foo, bar, 10, baz, false)  ")]
    [InlineData("quoted \"value\" and trailing slash\\")]
    [InlineData("line\nbreak\tand & | > shell text")]
    public async Task RawIniOverridesPreserveExactContentsAsOneArgument(string value) {
        var path = Path.Combine(_directory, "raw.yaml");
        File.WriteAllText(path, $"chiv2-path: {JsonSerializer.Serialize(_directory)}\nplatform: Steam\nserver:\n" +
            "  local-ip: 127.0.0.1\n  ini:\n    Game:\n      /Script/TBL.TBLGameMode:\n" +
            $"        Custom: {{type: raw, value: {JsonSerializer.Serialize(value)}}}\n");
        var options = CliLaunchOptions.Parse(["server", "--config-path", path]);
        var expected = new List<string> { "TBL", "-server", "--attach-stdio", "--log4rs-yaml", options.InstancePaths.Logging };
        expected.AddRange(FirstRunArguments);
        expected.Add("-ini:Game:[/Script/TBL.TBLGameMode]:Custom=" + value);
        expected.AddRange(["-saveddirsuffix=UnchainedCLI", "-unchained"]);
        expected.AddRange(DefaultServerArguments());
        expected.AddRange(["--server-browser-backend", "https://servers.polehammer.net"]);
        Assert.Equal(expected, await CaptureControl(expected));
        Assert.Equal(expected, await CaptureLaunch(options.ToLaunchOptions()));
    }

    private CliLaunchOptions Parse(string command, bool yaml, string[] arguments, string? serverValue = null) {
        if (!yaml) return CliLaunchOptions.Parse([command, "--chivalry2-path", _directory, "--platform", "Steam",
            .. command == "server" ? new[] { "--local-ip", "127.0.0.1" } : [],
            .. serverValue is null ? System.Array.Empty<string>() : ["--name", serverValue, "--description", serverValue,
                "--starting-map", serverValue, "--password", serverValue], "--", .. arguments]);
        var path = Path.Combine(_directory, "launch.yaml");
        // JSON-quoted strings are YAML scalars; cover both shared and command-specific game-args lists.
        File.WriteAllText(path, $"chiv2-path: {JsonSerializer.Serialize(_directory)}\nplatform: Steam\n" +
            $"game-args: {JsonSerializer.Serialize(arguments.Take(1))}\n{command}:\n" +
            (command == "server" ? "  local-ip: 127.0.0.1\n" : "") +
            (serverValue is null ? "" : string.Concat(new[] { "name", "description", "starting-map", "password" }
                .Select(key => $"  {key}: {JsonSerializer.Serialize(serverValue)}\n"))) +
            $"  game-args: {JsonSerializer.Serialize(arguments.Skip(1))}\n");
        return CliLaunchOptions.Parse([command, "--config-path", path]);
    }

    private static readonly string[] FirstRunArguments = [
        "-ini:Game:[/Script/TBL.TBLTitleScreen]:bSavedHasAgreedToTOS=True",
        "-ini:Game:[/Script/TBL.TBLGameInstance]:FirstLoadCompleted=True"
    ];

    private static IEnumerable<string> DefaultServerArguments(string name = "Unchained Server",
        string description = "", string map = "FFA_Courtyard") => [
        "-ini:Game:[/Script/TBL.TBLGameMode]:ServerName=" + name, "--next-map-name", map,
        "Port=7777", "GameServerPingPort=3075", "GameServerQueryPort=7071", "-rcon", "9001",
        "--server-browser-description", description, "--motd", description, "--censor-mode", "none",
        "-nullrhi", "-unattended", "-nosound", "--use-backend-banlist", "--local-ip", "127.0.0.1", "--desync-patch"
    ];

    private async Task<string[]> CaptureLaunch(LaunchOptions options) {
        var output = Path.Combine(_directory, "launch-result.json");
        Directory.CreateDirectory(Path.Combine(_directory, FilePaths.BinDir));
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(opts => Some(opts with {
            LaunchArgs = ArgumentEscaper.Escape(output) + " " + opts.LaunchArgs
        }));
        // Use the actual Core launcher and ProcessLauncher (not a string-capturing mock).
        var launcher = new UnchainedChivalry2Launcher(preparer, new ProcessLauncher(Probe),
            _directory, new NoOpInjector());
        var result = await launcher.TryLaunch(options);
        using var process = result.Match(Left: error => throw new InvalidOperationException(error.Message), Right: process => process);
        return await ReadResult(process, output);
    }

    private async Task<string[]> CaptureControl(IEnumerable<string> arguments) {
        var output = Path.Combine(_directory, "control-result.json");
        var start = new ProcessStartInfo(Probe) { UseShellExecute = false };
        start.ArgumentList.Add(output);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        return await ReadResult(process, output);
    }

    private static async Task<string[]> ReadResult(Process process, string output) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.Equal(0, process.ExitCode);
        return JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(output))!;
    }

    private sealed class NoOpInjector : IProcessInjector {
        public bool Inject(Process process) => true;
    }
}