using LanguageExt;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using UnchainedLauncher.CLI.Launching;
using UnchainedLauncher.CLI.Terminal;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace UnchainedLauncher.CLI.Tests;

public sealed class NativeConsoleLaunchTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-console-" + Guid.NewGuid().ToString("N"));
    private bool _rootCreated;
    private int? _probeId;
    private string _stopState = "not stopped";
    private readonly ITestOutputHelper _log;
    private static string Probe => Path.Combine(AppContext.BaseDirectory, "argument-probe",
        OperatingSystem.IsWindows() ? "ArgumentProbe.exe" : "ArgumentProbe");
    public NativeConsoleLaunchTests(ITestOutputHelper log) {
        _log = log;
    }
    public void Dispose() {
        if (!_rootCreated) return;
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException error) {
            throw new IOException($"Fixture deletion failed. Probe PID={_probeId}; {_stopState}; " +
                $"testhost PID={Environment.ProcessId}, cwd={Environment.CurrentDirectory}; " +
                $"root holders: {DirectoryHolders(_root)}; " +
                $"Win64 holders: {DirectoryHolders(Path.Combine(_root, FilePaths.BinDir))}", error);
        }
    }

    private void CreateRoot() {
        Directory.CreateDirectory(Path.Combine(_root, FilePaths.BinDir));
        _rootCreated = true;
    }

    [Fact]
    public async Task CancelledPreparationClosesStdioWithoutWaitingForAChild() {
        Assert.False(Directory.Exists(_root));
        if (!OperatingSystem.IsWindows()) return;
        var options = new LaunchOptions([], Option<string>.None, "", false, false,
            Option<string>.None, Option<ServerLaunchOptions>.None);
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(_ => Option<LaunchOptions>.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => NativeGameLauncher.LaunchAsync(Probe, _root,
            options, preparer, new ProbeInjector(Path.Combine(_root, "not-created")), forwardConsole: true).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLaunchClosesStdioAndExcludesUnrelatedHandles(bool throwDuringInjection) {
        if (!OperatingSystem.IsWindows()) return;
        CreateRoot();
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
        CreateRoot();
        using var capture = new ProcessOutputCapture();
        using var console = new ForwardedConsole();
        var read = console.CaptureAsync(capture.StandardOutput);
        using var process = new ProcessLauncher(Probe, capture).Launch(_root,
            "--std-probe " + ArgumentEscaper.Escape(Path.Combine(_root, "idle.txt")), _ => true)
            .Match(Left: error => throw error, Right: value => value);
        _probeId = process.Id;
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!console.Drain().Contains("stdout marker 世界")) await Task.Delay(10, timeout.Token);
            await Task.Run(console.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            await read.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(process.HasExited);
        }
        finally {
            // This direct launch uses the fixture root, not the game's Win64 directory.
            _stopState = $"before kill: root holders={DirectoryHolders(_root)}";
            _log.WriteLine($"Probe PID={_probeId}; {_stopState}");
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            _stopState += $"; WaitForExitAsync completed; HasExited={process.HasExited}; ExitCode={process.ExitCode}; " +
                $"after exit: root holders={DirectoryHolders(_root)}";
            _log.WriteLine(_stopState);
        }
    }

    [Fact]
    public async Task AttachStdioCapturesBothStreamsThroughTheSuspendedLaunchWithoutIpc() {
        if (!OperatingSystem.IsWindows()) return;
        CreateRoot();
        var output = Path.Combine(_root, "std-probe.txt");
        var options = new LaunchOptions([], Option<string>.None,
            "--std-probe " + ArgumentEscaper.Escape(output), false, false, Option<string>.None, Option<ServerLaunchOptions>.None);
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(value => Prelude.Some(value));
        var injector = new ProbeInjector(output);
        using var game = await NativeGameLauncher.LaunchAsync(Probe, _root, options, preparer,
            injector, forwardConsole: true);
        _probeId = game.Id;
        try {
            List<string> lines = [];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!lines.Contains("stdout marker 世界") || !lines.Contains("stderr marker")) {
                lines.AddRange(game.ConsoleOutput!.Drain());
                if (timeout.IsCancellationRequested) throw new InvalidOperationException("Missing captured standard output: " + string.Join(" | ", lines));
                await Task.Delay(10);
            }
            Assert.DoesNotContain("[plugin console connected]", lines);
            Assert.True(injector.Called);
            using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(output, timeout.Token));
            Assert.Equal(game.Id, ready.RootElement.GetProperty("ProcessId").GetInt32());
            Assert.Equal(Path.GetFullPath(Path.Combine(_root, FilePaths.BinDir)),
                ready.RootElement.GetProperty("WorkingDirectory").GetString());
            Assert.True(ready.RootElement.GetProperty("StandardInputEof").GetBoolean());
            Assert.False(game.HasExited);
        }
        finally {
            _stopState = $"before stop: Win64 holders={DirectoryHolders(Path.Combine(_root, FilePaths.BinDir))}";
            _log.WriteLine($"Probe PID={_probeId}; {_stopState}");
            if (!game.HasExited) await game.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            _stopState += $"; StopAsync completed; HasExited={game.HasExited}; ExitCode={game.ExitCode}; " +
                $"after stop: Win64 holders={DirectoryHolders(Path.Combine(_root, FilePaths.BinDir))}";
            _log.WriteLine(_stopState);
        }
        Assert.True(game.HasExited);
        var console = game.ConsoleOutput!;
        game.Dispose();
        Assert.Null(game.CleanupOutput);
        Assert.Throws<ObjectDisposedException>(() => { _ = console.CaptureAsync(Stream.Null); });
    }

    [Fact]
    public async Task SuspendedWindowsLaunchInheritsUnicodeParentEnvironment() {
        if (!OperatingSystem.IsWindows()) return;
        CreateRoot();
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

    // Includes current-directory handles and this diagnostic query's own testhost handle.
    private static string DirectoryHolders(string path) {
        if (!OperatingSystem.IsWindows()) return "Windows only";
        using var directory = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (directory.IsInvalid) return $"open failed: Win32 {Marshal.GetLastWin32Error()}";
        var buffer = Marshal.AllocHGlobal(65536);
        try {
            var status = NtQueryInformationFile(directory, out _, buffer, 65536, 47);
            if (status != 0) return $"query failed: NTSTATUS 0x{status:X8}";
            var count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > (65536 - IntPtr.Size) / IntPtr.Size) return $"invalid count {count}";
            List<string> holders = [];
            for (var i = 0; i < count; i++) {
                var pid = checked((int)Marshal.ReadIntPtr(buffer, IntPtr.Size * (i + 1)));
                try {
                    using var process = Process.GetProcessById(pid);
                    holders.Add($"{pid}:{process.ProcessName}:exited={process.HasExited}");
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) {
                    holders.Add($"{pid}:{error.GetType().Name}");
                }
            }
            return holders.Count == 0 ? "none" : string.Join(", ", holders);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationFile(SafeFileHandle file, out IoStatusBlock ioStatus,
        IntPtr information, uint length, int informationClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess,
        out IntPtr target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}