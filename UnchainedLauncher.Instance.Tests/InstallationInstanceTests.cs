using Semver;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.GUI.Services;
using Xunit;
using Xunit.Abstractions;

namespace UnchainedLauncher.Instance.Tests;

public sealed class WindowsFactAttribute : FactAttribute {
    public WindowsFactAttribute() {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows kernel objects (or one Wine prefix).";
    }
}

public sealed class InstallationInstanceTests : IDisposable {
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "launcher-instance-" + Guid.NewGuid()));
    private readonly ITestOutputHelper _output;

    public InstallationInstanceTests(ITestOutputHelper output) {
        _output = output;
    }

    [Fact]
    public void IdentityFailureRefusesWithoutMutation() {
        var missing = new DirectoryInfo(Path.Combine(_root.FullName, "missing"));
        Assert.False(InstallationStartup.TryAcquire(out var guard, out var error, missing));
        Assert.Null(guard);
        Assert.NotNull(error);
        Assert.Empty(_root.GetFileSystemInfos());
    }

    [WindowsFact]
    public async Task SameRootRefusedDifferentRootCoexistsAndKillRecovers() {
        using var holder = await StartHolder(_root);
        for (var i = 0; i < 100; i++) Assert.False(InstallationInstanceGuard.TryAcquire(_root, out _));
        var other = _root.CreateSubdirectory("other");
        using var independent = await StartHolder(other);
        Assert.False(InstallationInstanceGuard.TryAcquire(other, out _));
        holder.Kill();
        await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var signaledAfterAsyncWait = holder.WaitForExit(0);
        var cleanup = Stopwatch.StartNew();
        // HasExited/WaitForExitAsync can observe the exit code before kernel handle cleanup.
        // WaitForExit(int) waits for the process handle to be signaled, not just its exit code.
        Assert.True(holder.WaitForExit(15000), "Killed probe did not finish kernel cleanup.");
        _output.WriteLine($"Runtime {Environment.Version}; process signaled after async wait: {signaledAfterAsyncWait}; cleanup wait: {cleanup.Elapsed}.");
        Assert.True(InstallationInstanceGuard.TryAcquire(_root, out var recovered), "Presence remained after the process handle was signaled.");
        using (recovered)
            Assert.False(InstallationInstanceGuard.TryAcquire(_root, out _));
        Assert.False(InstallationInstanceGuard.TryAcquire(other, out _));
    }

    [WindowsFact]
    public async Task LateSaveStaysProtectedUntilCleanExitAndReplacementAcquires() {
        using var holder = await StartHolder(_root);
        await holder.StandardInput.WriteLineAsync("save");
        await holder.StandardInput.FlushAsync();
        Assert.Equal("SAVED", await ReadLine(holder));
        Assert.True(File.Exists(Path.Combine(_root.FullName, "late-save")));
        Assert.False(InstallationInstanceGuard.TryAcquire(_root, out _));
        await holder.StandardInput.WriteLineAsync("exit");
        await holder.StandardInput.FlushAsync();
        Assert.True(holder.WaitForExit(15000));
        Assert.Equal(0, holder.ExitCode);
        using var replacement = await StartHolder(_root);
    }

    [WindowsFact]
    public async Task ConcurrentProcessesHaveOneWinner() {
        var contenders = new List<ProcessLease>();
        try {
            for (var i = 0; i < 6; i++) contenders.Add(Start(_root));
            var replies = await Task.WhenAll(contenders.Select(ReadLine));
            Assert.Equal(1, replies.Count(r => r == "READY"));
        }
        finally {
            foreach (var process in contenders) process.Dispose();
        }
    }

    [WindowsFact]
    public async Task DisposalAcrossThreadsAndDuplicateOrChildHandlesDoNotRetainPresence() {
        Assert.True(InstallationInstanceGuard.TryAcquire(_root, out var guard));
        using var original = guard;
        using var independent = await StartHolder(_root.CreateSubdirectory("other"));
        await Task.Run(() => {
            for (var i = 0; i < 100; i++) Assert.False(InstallationInstanceGuard.TryAcquire(_root, out _));
            original!.Dispose();
        });
        Assert.True(InstallationInstanceGuard.TryAcquire(_root, out var next));
        next!.Dispose();
    }

    [WindowsFact]
    public void PhysicalAliasesContend() {
        Assert.True(InstallationInstanceGuard.TryAcquire(_root, out var guard));
        using (guard) {
            foreach (var path in new[] { _root.FullName.ToUpperInvariant(), _root.FullName + "\\", Path.Combine(_root.FullName, ".", "..", _root.Name) })
                Assert.False(InstallationInstanceGuard.TryAcquire(new DirectoryInfo(path), out _));
            Assert.False(InstallationInstanceGuard.TryAcquire(new DirectoryInfo(Path.GetRelativePath(Environment.CurrentDirectory, _root.FullName)), out _));

            var shortPath = new System.Text.StringBuilder(32768);
            Assert.NotEqual(0u, GetShortPathNameW(_root.FullName, shortPath, (uint)shortPath.Capacity));
            _output.WriteLine(shortPath.ToString() == _root.FullName
                ? "Short-name alias unavailable: filesystem returned the long path."
                : $"Short-name alias exercised: {shortPath}");
            Assert.False(InstallationInstanceGuard.TryAcquire(new DirectoryInfo(shortPath.ToString()), out _));

            var alias = _root.FullName + "-junction";
            try {
                using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{alias}\" \"{_root.FullName}\"") {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                })!;
                Assert.True(mklink.WaitForExit(15000));
                Assert.Equal(0, mklink.ExitCode);
                var child = _root.CreateSubdirectory("child");
                Assert.True(InstallationInstanceGuard.TryAcquire(child, out var childGuard));
                using (childGuard)
                    Assert.False(InstallationInstanceGuard.TryAcquire(new DirectoryInfo(Path.Combine(alias, "child")), out _));
                Assert.False(InstallationInstanceGuard.TryAcquire(new DirectoryInfo(alias), out _));
            }
            finally {
                if (Directory.Exists(alias)) Directory.Delete(alias);
            }
        }
    }

    [WindowsFact]
    public async Task DuplicateStartupDoesNotInitializeOrChangeFilesInAnyMode() {
        _root.CreateSubdirectory(".mod_cache");
        foreach (var name in new[] { "config.json", "launcher.log", "crash.log", ".mod_cache/state.json" })
            File.WriteAllText(Path.Combine(_root.FullName, name), "original");
        using (var first = Start(_root, "--initialize")) {
            Assert.True(first.WaitForExit(15000));
            Assert.Equal(0, first.ExitCode);
            Assert.True(File.Exists(Path.Combine(_root.FullName, "initialized")));
            File.Delete(Path.Combine(_root.FullName, "initialized"));
        }
        var original = Snapshot();
        using var holder = await StartHolder(_root);
        foreach (var mode in new[] { "", "--no-install", "--startvanilla", "--startunchained" }) {
            using var duplicate = Start(_root, "--initialize", mode);
            Assert.True(duplicate.WaitForExit(15000));
            Assert.Equal(2, duplicate.ExitCode);
            Assert.Equal(original, Snapshot());
        }
    }

    private string[] Snapshot() => _root.GetFileSystemInfos("*", SearchOption.AllDirectories)
        .OrderBy(entry => entry.FullName)
        .Select(entry => entry is FileInfo file
            ? $"{entry.FullName}:{file.LastWriteTimeUtc.Ticks}:{File.ReadAllText(file.FullName)}"
            : entry.FullName)
        .ToArray();

    [WindowsFact]
    public async Task InstallerOccupiedTargetRefusesBeforeVersionOrWritesEvenWithReplaceCurrent() {
        using var holder = await StartHolder(_root);
        var version = new VersionExtractor();
        var installer = new UnchainedLauncherInstaller(_ => throw new Exception("Must not exit"), version);
        Assert.False(await installer.Install(_root, Release(), false));
        Assert.False(await installer.Install(_root, Release(), true));
        Assert.Equal(0, version.Calls);
        Assert.Empty(_root.GetFiles());
    }

    [WindowsFact]
    public async Task InstallerExternalLeaseReleasesBeforeTargetLaunchWhileSourceLives() {
        var source = _root.CreateSubdirectory("source");
        Assert.True(InstallationInstanceGuard.TryAcquire(source, out var sourceGuard));
        using (sourceGuard) {
            var installer = new UnchainedLauncherInstaller(_ => { }, new VersionExtractor(), sourceGuard);
            Assert.True(await installer.Install(_root, Release(), false));
            Assert.True(File.Exists(Path.Combine(_root.FullName, "Chivalry2Launcher.exe")));
            using var target = await StartHolder(_root);
            Assert.False(InstallationInstanceGuard.TryAcquire(source, out _));
        }
    }

    [WindowsFact]
    public async Task InstallerFailureReleasesExternalTargetAndSelfIdentityIsReused() {
        Assert.True(InstallationInstanceGuard.TryAcquire(_root, out var own));
        using (own) {
            var version = new VersionExtractor();
            var installer = new UnchainedLauncherInstaller(_ => { }, version, own);
            // Throw after guard selection to exercise replaceCurrent without launching PowerShell.
            version.Fail = true;
            Assert.False(await installer.Install(_root, Release(), true));
            Assert.Equal(1, version.Calls);
            version.Fail = false;
            Assert.True(await installer.Install(_root, Release(), false));
            Assert.Equal(2, version.Calls);
            Assert.False(InstallationInstanceGuard.TryAcquire(_root, out _));
            var target = _root.CreateSubdirectory("failure");
            Assert.False(await installer.Install(target, Release() with { Assets = [] }, false));
            Assert.True(InstallationInstanceGuard.TryAcquire(target, out var released));
            released!.Dispose();
            Assert.True(InstallationInstanceGuard.TryAcquire(target, out var occupied));
            using (occupied)
                Assert.False(await installer.Install(target, Release(), true));
            Assert.Equal(2, version.Calls);
        }
    }

    [WindowsFact]
    public async Task InstallerHoldsExternalTargetAcrossAsyncContinuation() {
        var installer = new UnchainedLauncherInstaller(_ => { }, new VersionExtractor());
        Assert.True(await installer.Install(_root, Release(), false, async message => {
            await Task.Yield();
            Assert.False(InstallationInstanceGuard.TryAcquire(_root, out _));
        }));
        Assert.True(InstallationInstanceGuard.TryAcquire(_root, out var released));
        released!.Dispose();
    }

    private static ReleaseTarget Release() => new("", "", new SemVersion(1, 2, 3),
        [new ReleaseAsset("Launcher.exe", "https://invalid.example/not-used")], DateTimeOffset.UtcNow, true, false);

    private sealed class VersionExtractor : IVersionExtractor {
        public int Calls;
        public bool Fail;
        public SemVersion GetVersion(string input) {
            Calls++;
            if (Fail) throw new IOException("Test failure after guard selection.");
            return new SemVersion(1, 2, 3);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string path, System.Text.StringBuilder shortPath, uint capacity);

    private static ProcessLease Start(DirectoryInfo root, params string[] args) {
        var start = new ProcessStartInfo(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe")) {
            WorkingDirectory = root.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Probe", "UnchainedLauncher.InstanceProbe.dll"));
        foreach (var arg in args.Where(a => a.Length != 0)) start.ArgumentList.Add(arg);
        return new ProcessLease(Process.Start(start)!);
    }

    private static async Task<ProcessLease> StartHolder(DirectoryInfo root) {
        var process = Start(root);
        try {
            Assert.Equal("READY", await ReadLine(process));
            return process;
        }
        catch {
            process.Dispose();
            throw;
        }
    }

    private static async Task<string?> ReadLine(ProcessLease process) =>
        await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));

    private static void Stop(Process process) {
        // Process.Dispose does not close redirected streams already accessed by the caller.
        using (process)
        using (process.StandardInput)
        using (process.StandardOutput)
        using (process.StandardError) {
            if (!process.WaitForExit(0)) process.Kill();
            Assert.True(process.WaitForExit(15000), "Probe did not finish kernel cleanup.");
        }
    }

    private sealed class ProcessLease(Process process) : IDisposable {
        public StreamWriter StandardInput => process.StandardInput;
        public StreamReader StandardOutput => process.StandardOutput;
        public int ExitCode => process.ExitCode;
        public void Kill() => process.Kill();
        public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
        public Task WaitForExitAsync() => process.WaitForExitAsync();
        public void Dispose() => Stop(process);
    }

    public void Dispose() => _root.Delete(true);
}