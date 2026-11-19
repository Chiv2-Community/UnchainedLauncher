using LanguageExt;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
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

    [Fact]
    public async Task CancelledPreparationClosesStdioWithoutWaitingForAChild() {
        if (!OperatingSystem.IsWindows()) return;
        var options = new LaunchOptions([], Option<string>.None, "", false, false,
            Option<string>.None, Option<ServerLaunchOptions>.None);
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(_ => Option<LaunchOptions>.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => NativeGameLauncher.LaunchAsync(Probe, _root,
            options, preparer, new ProbeInjector(Path.Combine(_root, "not-created")), forwardConsole: true).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLaunchClosesStdioAndExcludesUnrelatedHandles(bool throwDuringInjection) {
        if (!OperatingSystem.IsWindows()) return;
        using var unrelated = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var capture = new ProcessOutputCapture();
        using var current = Process.GetCurrentProcess();
        var initialized = false;
        var inherited = false;
        var result = new ProcessLauncher(Probe, capture).Launch(_root, "unused", process => {
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
        Assert.Equal(0, await capture.StandardOutput.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task DisposingReceiverUnblocksAnIdleInheritedPipeRead() {
        if (!OperatingSystem.IsWindows()) return;
        using var capture = new ProcessOutputCapture();
        using var console = new ForwardedConsole();
        var read = console.CaptureAsync(capture.StandardOutput);
        using var process = new ProcessLauncher(Probe, capture).Launch(_root,
            "--std-probe " + ArgumentEscaper.Escape(Path.Combine(_root, "idle.txt")), _ => true)
            .Match(Left: error => throw error, Right: value => value);
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!console.Drain().Contains("stdout marker 世界")) await Task.Delay(10, timeout.Token);
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
            new ProbeInjector(output), forwardConsole: true);
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
    public async Task SuspendedWindowsLaunchInheritsUnicodeParentEnvironment() {
        if (!OperatingSystem.IsWindows()) return;
        var output = Path.Combine(_root, "unicode.json");
        var name = "UNCHAINED_ENV_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "世界 = value");
        try {
            using var capture = new ProcessOutputCapture();
            IProcessLauncher launcher = new ProcessLauncher(Probe, capture);
            var arguments = "--environment-probe " + ArgumentEscaper.Escape(output) + " " + name + " SystemRoot PATH UNCHAINED_LAUNCH_ID";
            using var process = launcher.Launch(_root, arguments, _ => {
                Assert.False(File.Exists(output));
                return true;
            }).Match(Left: error => throw error, Right: process => process);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(await File.ReadAllTextAsync(output, timeout.Token))!;
            Assert.Equal("世界 = value", values[name]);
            foreach (var key in new[] { "SystemRoot", "PATH", "UNCHAINED_LAUNCH_ID" })
                Assert.Equal(Environment.GetEnvironmentVariable(key), values[key]);
            Assert.Equal("世界 = value", Environment.GetEnvironmentVariable(name));
            Assert.Equal(0, await capture.StandardOutput.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { Environment.SetEnvironmentVariable(name, null); }
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