using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using LanguageExt;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class NativeConsoleLaunchTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-console-" + Guid.NewGuid().ToString("N"));
    private static string Probe => Path.Combine(AppContext.BaseDirectory, "argument-probe",
        OperatingSystem.IsWindows() ? "ArgumentProbe.exe" : "ArgumentProbe");
    public NativeConsoleLaunchTests() => Directory.CreateDirectory(Path.Combine(_root, FilePaths.BinDir));
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledPreparationClosesPipesWithoutWaitingForAChild(bool attachStdio) {
        if (!OperatingSystem.IsWindows()) return;
        var options = new LaunchOptions([], Option<string>.None, "", false, false,
            Option<string>.None, Option<ServerLaunchOptions>.None);
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(_ => Option<LaunchOptions>.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => NativeGameLauncher.LaunchAsync(Probe, _root,
            options, preparer, new ProbeInjector(Path.Combine(_root, "not-created")), forwardConsole: true,
            attachStdio: attachStdio).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStructuredLaunchClosesItsPipeAndExcludesUnrelatedHandles(bool throwDuringInjection) {
        if (!OperatingSystem.IsWindows()) return;
        using var unrelated = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var capture = new ProcessOutputCapture(forwardConsole: true);
        using var current = Process.GetCurrentProcess();
        var initialized = false;
        var inherited = false;
        var result = new ProcessLauncher(Probe, null, capture).Launch(_root, "unused", process => {
            initialized = true;
            inherited = DuplicateHandle(process.Handle, unrelated.ClientSafePipeHandle.DangerousGetHandle(),
                current.Handle, out var copy, 0, false, 2);
            if (inherited) CloseHandle(copy);
            if (throwDuringInjection) throw new InvalidOperationException("injection failed");
            return false;
        });
        Assert.True(initialized);
        Assert.False(inherited, "An unrelated inheritable handle leaked into the suspended child.");
        Assert.True(result.IsLeft);
        Assert.Equal(0, await capture.ConsoleOutput!.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task DisposingReceiverUnblocksAnIdleInheritedPipeRead() {
        if (!OperatingSystem.IsWindows()) return;
        using var capture = new ProcessOutputCapture(forwardConsole: true);
        using var console = new ForwardedConsole();
        var read = console.ReadAsync(capture.ConsoleOutput!);
        using var process = new ProcessLauncher(Probe, null, capture).Launch(_root,
            "--console-probe-idle " + ArgumentEscaper.Escape(Path.Combine(_root, "idle.json")), _ => true)
            .Match(Left: error => throw error, Right: value => value);
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!console.Drain().Contains("native console marker")) await Task.Delay(10, timeout.Token);
            await Task.Run(console.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            await read.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(process.HasExited);
        }
        finally {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task AttachStdioCapturesBothStreamsThroughTheSuspendedLaunchWithoutIpc() {
        if (!OperatingSystem.IsWindows()) return;
        var output = Path.Combine(_root, "std-probe.txt");
        var options = new LaunchOptions([], Option<string>.None,
            "--std-probe " + ArgumentEscaper.Escape(output), false, false, Option<string>.None, Option<ServerLaunchOptions>.None);
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(value => Prelude.Some(value));
        using var game = await NativeGameLauncher.LaunchAsync(Probe, _root, options, preparer,
            new ProbeInjector(output), forwardConsole: true, attachStdio: true);
        try {
            List<string> lines = [];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!lines.Contains("stdout marker 世界") || !lines.Contains("stderr marker")) {
                lines.AddRange(game.ConsoleOutput!.Drain());
                if (timeout.IsCancellationRequested) throw new InvalidOperationException("Missing captured standard output: " + string.Join(" | ", lines));
                await Task.Delay(10);
            }
            Assert.DoesNotContain("[plugin console connected]", lines);
        }
        finally { if (!game.HasExited) await game.StopAsync(); }
    }

    [Fact]
    public async Task NativeLaunchForwardsWithoutChangingParentEnvironmentAndDisposesReceiver() {
        if (!OperatingSystem.IsWindows()) return;
        var beforeHandle = Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_HANDLE");
        var beforeEndpoint = Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_ENDPOINT");
        var beforeToken = Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_TOKEN");
        var output = Path.Combine(_root, "environment.json");
        var options = new LaunchOptions([], Option<string>.None,
            "--console-probe " + ArgumentEscaper.Escape(output), false, false, Option<string>.None, Option<ServerLaunchOptions>.None);
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(value => Prelude.Some(value));
        var injector = new ProbeInjector(output);
        var game = await NativeGameLauncher.LaunchAsync(Probe, _root, options, preparer, injector, forwardConsole: true);
        using var observed = Process.GetProcessById(game.Id);
        try {
            Assert.True(injector.Called);
            Assert.NotNull(game.ConsoleOutput);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            List<string> lines = [];
            while (!lines.Contains("native console marker")) {
                lines.AddRange(game.ConsoleOutput!.Drain());
                if (!lines.Contains("native console marker")) await Task.Delay(10, timeout.Token);
            }
            using var environment = JsonDocument.Parse(await File.ReadAllTextAsync(output, timeout.Token));
            Assert.False(string.IsNullOrEmpty(environment.RootElement.GetProperty("handle").GetString()));
            Assert.Null(environment.RootElement.GetProperty("endpoint").GetString());
            Assert.Null(environment.RootElement.GetProperty("token").GetString());
            Assert.DoesNotContain(lines, line => line.Contains("must not enter structured records"));
            Assert.Equal(beforeHandle, Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_HANDLE"));
            Assert.Equal(beforeEndpoint, Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_ENDPOINT"));
            Assert.Equal(beforeToken, Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_TOKEN"));
            game.Dispose(); // A detached receiver read must unblock and close the child's stream.
            await observed.WaitForExitAsync(timeout.Token);
            Assert.True(observed.HasExited);
        }
        finally {
            if (!observed.HasExited) {
                observed.Kill(entireProcessTree: true);
                await observed.WaitForExitAsync();
            }
            game.Dispose();
        }
    }

    [Fact]
    public async Task SuspendedWindowsEnvironmentIsUnicodeAndOverridesCaseInsensitiveWithoutLeaking() {
        if (!OperatingSystem.IsWindows()) return;
        var output = Path.Combine(_root, "unicode.json");
        var inherited = Environment.GetEnvironmentVariable("SystemRoot");
        var parentPath = Environment.GetEnvironmentVariable("PATH");
        var parentTemp = Environment.GetEnvironmentVariable("TEMP");
        var overrides = new Dictionary<string, string?> {
            ["UNCHAINED_ENV_UNICODE"] = "世界 = value",
            ["path"] = "child-only",
            ["TEMP"] = null
        };
        IProcessLauncher launcher = new ProcessLauncher(Probe, overrides);
        overrides["UNCHAINED_ENV_UNICODE"] = "must not change snapshot";
        var arguments = "--environment-probe " + ArgumentEscaper.Escape(output) + " UNCHAINED_ENV_UNICODE SystemRoot PATH TEMP";
        using var process = launcher.Launch(_root, arguments, _ => {
            Assert.False(File.Exists(output));
            return true;
        }).Match(Left: error => throw error, Right: process => process);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(await File.ReadAllTextAsync(output, timeout.Token))!;
        Assert.Equal("世界 = value", values["UNCHAINED_ENV_UNICODE"]);
        Assert.Equal(inherited, values["SystemRoot"]);
        Assert.Equal("child-only", values["PATH"]);
        Assert.Null(values["TEMP"]);
        Assert.Equal(parentPath, Environment.GetEnvironmentVariable("PATH"));
        Assert.Equal(parentTemp, Environment.GetEnvironmentVariable("TEMP"));
    }

    private sealed class ProbeInjector(string output) : IProcessInjector {
        internal bool Called { get; private set; }
        public bool Inject(Process process) => Inject(process, false);
        public bool Inject(Process process, bool server) {
            Called = true;
            if (OperatingSystem.IsWindows()) Assert.False(File.Exists(output));
            return true;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess,
        out IntPtr target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
