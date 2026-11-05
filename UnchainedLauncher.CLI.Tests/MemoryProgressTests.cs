using UnchainedLauncher.Core.Utilities;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class MemoryProgressTests {
    [Fact]
    public async Task ConsoleProgressSupportsConcurrentTrackingAndReports() {
        await Task.Run(() => {
            Assert.Null(SynchronizationContext.Current);
            var progress = new AccumulatedMemoryProgress();
            Parallel.For(0, 128, _ => {
                var child = new MemoryProgress();
                progress.AlsoTrack(child);
                for (var value = 1; value <= 100; value++) child.Report(value);
            });
            Assert.Equal(128, progress.Progresses.Count);
            Assert.Equal(100, progress.ProgressPercentage);
        });
    }
}