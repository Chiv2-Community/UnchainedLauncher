using Semver;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Utilities;
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
        Assert.True(InstallationInstanceGuard.TryAcquire(_root, out var recovered));
        recovered!.Dispose();
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
        await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, holder.ExitCode);
        using var replacement = await StartHolder(_root);
    }

    [WindowsFact]
    public async Task ConcurrentProcessesHaveOneWinner() {
        var contenders = Enumerable.Range(0, 6).Select(_ => Start(_root)).ToArray();
        try {
            var replies = await Task.WhenAll(contenders.Select(ReadLine));
            Assert.Equal(1, replies.Count(r => r == "READY"));
        }
        finally {
            foreach (var process in contenders) Stop(process);
        }
    }

    [WindowsFact]
    public async Task DisposalAcrossThreadsAndDuplicateHandlesDoNotRetainPresence() {
        Assert.True(InstallationInstanceGuard.TryAcquire(_root, out var guard));
        await Task.Run(() => {
            for (var i = 0; i < 100; i++) Assert.False(InstallationInstanceGuard.TryAcquire(_root, out _));
            guard!.Dispose();
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
            await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, first.ExitCode);
            Assert.True(File.Exists(Path.Combine(_root.FullName, "initialized")));
            File.Delete(Path.Combine(_root.FullName, "initialized"));
        }
        var original = Snapshot();
        using var holder = await StartHolder(_root);
        foreach (var mode in new[] { "", "--no-install", "--startvanilla", "--startunchained" }) {
            using var duplicate = Start(_root, "--initialize", mode);
            await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
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
    public async Task DuplicateCannotRunTheRealOwnershipConverter() {
        var target = _root.CreateSubdirectory(Path.Combine("TBL", "Content", "Paks"));
        File.WriteAllText(Path.Combine(target.FullName, "owned.pak"), "owned");
        var config = Path.Combine(_root.FullName, ".mod_cache", "mod_manager_config.json");
        var inventory = Path.Combine(target.FullName, ".unchained", "inventory.json");
        Directory.CreateDirectory(Path.GetDirectoryName(inventory)!);
        var registry = new AggregateModRegistry([]);
        var codec = new ModManagerCodec(registry);
        void WriteLegacyState() {
            codec.SerializeFile(config, new ModManager(registry, new PakDir(target.FullName,
                [new(new("Org", "Mod", "v1"), "owned.pak", 0)]), []));
            File.WriteAllText(inventory, "[]");
        }
        WriteLegacyState();
        using (var first = Start(_root, "--convert-ownership")) {
            await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, first.ExitCode);
            Assert.False(File.Exists(inventory));
            Assert.Empty(((PakDir)codec.Deserialize(File.ReadAllText(config)).Result!.PakDir).ManagedPaks);
        }
        WriteLegacyState();
        using var holder = await StartHolder(_root);
        var before = Snapshot();
        using var duplicate = Start(_root, "--convert-ownership");
        await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, duplicate.ExitCode);
        Assert.Equal(before, Snapshot());
    }

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

    private static Process Start(DirectoryInfo root, params string[] args) {
        var start = new ProcessStartInfo(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe")) {
            WorkingDirectory = root.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Probe", "UnchainedLauncher.InstanceProbe.dll"));
        foreach (var arg in args.Where(a => a.Length != 0)) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }

    private static async Task<ProcessLease> StartHolder(DirectoryInfo root) {
        var process = Start(root);
        try {
            Assert.Equal("READY", await ReadLine(process));
            return new ProcessLease(process);
        }
        catch {
            Stop(process);
            throw;
        }
    }

    private static async Task<string?> ReadLine(Process process) =>
        await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));

    private static void Stop(Process process) {
        if (!process.HasExited) {
            process.Kill();
            process.WaitForExit(15000);
        }
        process.Dispose();
    }

    private sealed class ProcessLease(Process process) : IDisposable {
        public StreamWriter StandardInput => process.StandardInput;
        public int ExitCode => process.ExitCode;
        public void Kill() => process.Kill();
        public Task WaitForExitAsync() => process.WaitForExitAsync();
        public static implicit operator Process(ProcessLease lease) => lease.Process;
        private Process Process => process;
        public void Dispose() => Stop(process);
    }

    public void Dispose() => _root.Delete(true);
}