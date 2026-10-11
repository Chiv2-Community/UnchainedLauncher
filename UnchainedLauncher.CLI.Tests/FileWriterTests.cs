using LanguageExt;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class FileWriterTests : IDisposable {
    private readonly string _directory = Path.Combine(
        OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "writer-tests-" + Guid.NewGuid().ToString("N"));

    public FileWriterTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("success")]
    [InlineData("output-failure")]
    [InlineData("cancelled")]
    [InlineData("abandoned")]
    public async Task RegistryInputIsClosedDeterministically(string outcome) {
        var source = Path.Combine(_directory, "source.pak");
        File.WriteAllText(source, "registry payload");
        var info = new ModInfo("https://github.com/org/mod", "mod", "test", null, null, [], [], []);
        var release = new Release("v1.0.0", "hash", "test.pak", DateTime.UtcNow, info,
            new AssetCollections([], [], [], [], []), null);
        var registry = new LocalModRegistry(Path.Combine(_directory, "registry"));
        await registry.AddRelease(release, source);
        var output = outcome == "output-failure" ? _directory : Path.Combine(_directory, "output.pak");
        using var writer = await registry.DownloadPak(ReleaseCoordinates.FromRelease(release), output)
            .Match(Right: value => value, Left: error => throw new Exception(error.Message));
        Assert.True(writer.InputStream.CanRead);
        if (outcome == "abandoned") {
            writer.Dispose();
        }
        else {
            using var cancellation = new CancellationTokenSource();
            if (outcome == "cancelled") cancellation.Cancel();
            var result = await writer.WriteAsync(Option<IProgress<double>>.None, cancellation.Token);
            Assert.Equal(outcome == "success", result.IsRight);
        }
        // Check before fixture/writer disposal: Linux must detect the same leak as Windows.
        Assert.False(writer.InputStream.CanRead);
        var registryPak = Path.Combine(registry.RegistryPath, info.Organization, info.RepoName, release.Tag, release.PakFileName);
        using var exclusive = File.Open(registryPak, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (outcome == "success") Assert.Equal("registry payload", File.ReadAllText(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InputIsClosedAfterReadFailureOrMidCopyCancellation(bool cancel) {
        using var cancellation = new CancellationTokenSource();
        using var input = new InterruptingStream(cancellation, cancel);
        using var writer = new FileWriter(Path.Combine(_directory, "output.pak"), input, input.Length);
        var result = await writer.WriteAsync(Option<IProgress<double>>.None, cancellation.Token);
        Assert.True(result.IsLeft);
        Assert.False(input.CanRead);
        Assert.Equal(cancel, cancellation.IsCancellationRequested);
    }

    private sealed class InterruptingStream(CancellationTokenSource cancellation, bool cancel) : MemoryStream(new byte[16]) {
        private bool _firstRead = true;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            if (_firstRead) {
                _firstRead = false;
                buffer.Span[0] = 1;
                return ValueTask.FromResult(1);
            }
            if (!cancel) throw new IOException("Input read failed.");
            cancellation.Cancel();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}