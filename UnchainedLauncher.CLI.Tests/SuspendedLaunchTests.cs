using System.Diagnostics;
using System.Text.Json;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Utilities;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class SuspendedLaunchTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "suspended-launch-" + Guid.NewGuid().ToString("N"));
    private static string Probe => Path.Combine(AppContext.BaseDirectory, "argument-probe", "ArgumentProbe.exe");

    public SuspendedLaunchTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("success")]
    [InlineData("false")]
    [InlineData("throw")]
    public async Task NativeMainThreadRunsOnlyAfterSuccessfulInitialization(string outcome) {
        if (!OperatingSystem.IsWindows()) return;
        var output = Path.Combine(_directory, "arguments.json");
        Process? observed = null;
        try {
            IProcessLauncher launcher = new ProcessLauncher(Probe);
            var calls = 0;
            var result = launcher.Launch(_directory, ArgumentEscaper.Escape(output) + " \"space value\"", process => {
                calls++;
                observed = Process.GetProcessById(process.Id);
                Thread.Sleep(250);
                Assert.False(File.Exists(output));
                if (outcome == "throw") throw new InvalidOperationException("test injection failure");
                return outcome == "success";
            });
            Assert.Equal(1, calls);
            Assert.NotNull(observed);
            if (outcome == "success") {
                using var process = result.Match(Left: error => throw error, Right: process => process);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await process.WaitForExitAsync(timeout.Token);
                Assert.Equal(0, process.ExitCode);
                Assert.Equal(new[] { "space value" }, JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(output)));
            }
            else {
                Assert.True(result.IsLeft);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await observed!.WaitForExitAsync(timeout.Token);
                Assert.False(File.Exists(output));
            }
        }
        finally {
            if (observed is not null) {
                if (!observed.HasExited) observed.Kill(entireProcessTree: true);
                observed.Dispose();
            }
        }
    }

    [Fact]
    public void FailedCreationDoesNotInitialize() {
        IProcessLauncher launcher = new ProcessLauncher(Path.Combine(_directory, "missing.exe"));
        var called = false;
        var result = launcher.Launch(_directory, "", _ => { called = true; return true; });
        Assert.True(result.IsLeft);
        Assert.False(called);
    }

    [Theory]
    [InlineData("", "value")]
    [InlineData("BAD=NAME", "value")]
    [InlineData("BAD\0NAME", "value")]
    [InlineData("VALID_NAME", "bad\0value")]
    public void InvalidWindowsEnvironmentDoesNotCreateOrInitializeAChild(string name, string value) {
        if (!OperatingSystem.IsWindows()) return;
        var launcher = new ProcessLauncher(Probe, new Dictionary<string, string?> { [name] = value });
        var called = false;
        var result = launcher.Launch(_directory, "", _ => { called = true; return true; });
        Assert.True(result.IsLeft);
        Assert.False(called);
    }
}
