using Moq;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir;

[Collection("Home launch UI")]
public class UnmanagedPakScanProgressTests : IDisposable {
    private readonly string directory = Path.Combine("TestDirectories", nameof(UnmanagedPakScanProgressTests), Guid.NewGuid().ToString());

    public UnmanagedPakScanProgressTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private IPakDir Paks() {
        var paks = new Mock<IPakDir>(MockBehavior.Strict);
        paks.SetupGet(value => value.DirPath).Returns(directory);
        paks.SetupGet(value => value.ManagedPaks).Returns([]);
        return paks.Object;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowsCurrentPakAndOverallProgressThenClosesBeforeConfirmation(bool cached) {
        File.WriteAllText(Path.Combine(directory, "first.pak"), "first");
        File.WriteAllText(Path.Combine(directory, "second.pak"), "second");
        if (cached) {
            using var prime = new PakReadCache(directory, _ => Task.FromResult<string?>("1.2.3"));
            foreach (var path in Directory.GetFiles(directory, "*.pak"))
                await prime.Version(path, await prime.Hash(path));
        }
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        MemoryProgress? progress = null;
        var closed = false;
        var starts = new List<(string Name, double Percentage)>();
        dialogue.Setup(value => value.DisplayProgress(It.IsAny<MemoryProgress>()))
            .Callback<MemoryProgress>(value => {
                progress = value;
                value.PropertyChanged += (_, change) => {
                    if (change.PropertyName == nameof(MemoryProgress.TaskName))
                        starts.Add((value.TaskName, value.ProgressPercentage));
                };
            }).Returns(() => closed = true);
        dialogue.Setup(value => value.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), "Keep", "Remove", "Cancel", It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback(() => {
                Assert.True(closed);
                Assert.Equal(100, progress!.ProgressPercentage);
            }).Returns(UserDialogueChoice.Cancel);
        var reads = 0;
        using var cache = new PakReadCache(directory, async path => {
            reads++;
            Assert.False(closed);
            Assert.Contains(Path.GetFileName(path), progress!.TaskName);
            Assert.Equal((reads - 1) * 50, progress.ProgressPercentage);
            await Task.Yield();
            return "1.2.3";
        });

        Assert.False(await UnmanagedPakRecovery.Recover(Paks(), [], dialogue.Object, [], false, cache));

        Assert.Equal(cached ? 0 : 2, reads);
        Assert.Equal(2, starts.Count);
        Assert.StartsWith("Scanning pak 1 of 2: ", starts[0].Name);
        Assert.StartsWith("Scanning pak 2 of 2: ", starts[1].Name);
        Assert.Equal(0, starts[0].Percentage);
        Assert.Equal(50, starts[1].Percentage);
        Assert.Contains(starts, value => value.Name.EndsWith("first.pak"));
        Assert.Contains(starts, value => value.Name.EndsWith("second.pak"));
        dialogue.Verify(value => value.DisplayProgress(It.IsAny<MemoryProgress>()), Times.Once);
        Assert.Equal(2, Directory.GetFiles(directory, "*.pak").Length);
    }

    [Fact]
    public async Task ScanFailureClosesProgressAndPreservesPak() {
        var path = Path.Combine(directory, "changing.pak");
        File.WriteAllText(path, "original");
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        var closed = false;
        dialogue.Setup(value => value.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => closed = true);
        using var cache = new PakReadCache(directory, _ => {
            File.AppendAllText(path, " changed");
            return Task.FromResult<string?>("1.2.3");
        });

        await Assert.ThrowsAsync<IOException>(() => UnmanagedPakRecovery.Recover(Paks(), [], dialogue.Object, [], false, cache));

        Assert.True(closed);
        Assert.True(File.Exists(path));
        dialogue.Verify(value => value.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DependencyUpdate>>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkippedScanningDoesNotOpenProgress(bool alwaysKeep) {
        if (alwaysKeep) File.WriteAllText(Path.Combine(directory, "manual.pak"), "content");
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        using var cache = new PakReadCache(directory, _ => throw new Exception("Must not scan"));

        Assert.True(await UnmanagedPakRecovery.Recover(Paks(), [], dialogue.Object, [], alwaysKeep, cache));

        dialogue.VerifyNoOtherCalls();
    }
}