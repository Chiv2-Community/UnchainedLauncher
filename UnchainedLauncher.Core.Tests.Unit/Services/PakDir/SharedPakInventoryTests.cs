using System.Text.Json;
using System.Threading.Channels;
using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using LauncherPakDir = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir {
    public class SharedPakInventoryTests : IDisposable {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "unchained-inventory-" + Guid.NewGuid());
        private readonly ReleaseCoordinates _a = new("test", "A", "1");
        private readonly ReleaseCoordinates _b = new("test", "B", "1");
        private int _downloads;

        public SharedPakInventoryTests() {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "pakchunk0-WindowsNoEditor.sig"), "signature");
        }

        private LauncherPakDir NewPakDir(IEnumerable<ManagedPak>? legacy = null) =>
            new(_directory, legacy ?? Array.Empty<ManagedPak>());

        private EitherAsync<Error, FileWriter> Write(string path) {
            Interlocked.Increment(ref _downloads);
            return new FileWriter(path, new MemoryStream(new byte[] { 1, 2, 3 }), 3);
        }

        private async Task Install(LauncherPakDir directory, params ReleaseCoordinates[] coordinates) {
            await foreach (var result in directory.InstallModSet(
                coordinates.Select(c => new ModInstallRequest(c, Write)), Option<AccumulatedMemoryProgress>.None)) {
                result.IfLeft(e => throw e);
            }
        }

        private List<ManagedPak> ReadInventory() => JsonSerializer.Deserialize<List<ManagedPak>>(
            File.ReadAllText(Path.Combine(_directory, ".unchained", "inventory.json")))!;

        [Fact]
        public async Task StaleReadersReusePaksAcrossReorderingAndRestart() {
            var server = NewPakDir();
            var client = NewPakDir();
            await Install(server, _a, _b);
            var obsoleteSnapshot = server.ManagedPaks.ToList();
            await Install(client, _b, _a);
            await Install(server, _a, _b);
            await Install(NewPakDir(obsoleteSnapshot), _b, _a);

            Assert.Equal(2, _downloads);
            Assert.Equal(2, Directory.GetFiles(_directory, "*.pak").Length);
            Assert.Equal(new[] { _b, _a }, ReadInventory().OrderBy(p => p.Priority).Select(p => p.Coordinates));
        }

        [Fact]
        public async Task SlowHealthyPreparationWaitsBeyondThirtySecondsAndReusesDownload() {
            var server = NewPakDir();
            var time = new RetryTimeProvider();
            var client = new LauncherPakDir(_directory, Array.Empty<ManagedPak>(), time);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<Either<Error, FileWriter>> DelayedWriter(string path) {
                started.SetResult();
                await release.Task;
                return await Write(path);
            }
            async Task FirstInstall() {
                await foreach (var result in server.InstallModSet(
                    new[] { new ModInstallRequest(_a, path => DelayedWriter(path).ToAsync()) },
                    Option<AccumulatedMemoryProgress>.None)) {
                    result.IfLeft(e => throw e);
                }
            }

            var first = FirstInstall();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = Install(client, _a);
            try {
                // Cross the old timeout without waiting for real downloads or wall-clock time.
                for (var i = 0; i < 3; i++) {
                    await time.AdvanceRetry(TimeSpan.FromSeconds(31));
                    Assert.False(second.IsCompleted);
                }
            }
            finally {
                release.SetResult();
            }
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await time.FireRetry(TimeSpan.Zero);
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, _downloads);
            Assert.Single(Directory.GetFiles(_directory, "*.pak"));
        }

        [Fact]
        public async Task CancelledLockWaitLeavesTheOwnersPendingMarkerAndInventoryUntouched() {
            var owner = new PakInventory(_directory);
            using var cancellation = new CancellationTokenSource();
            using (await owner.Lock()) {
                owner.BeginChange();
                async Task WaitForInstall() {
                    await foreach (var result in NewPakDir().InstallModSet(
                        new[] { new ModInstallRequest(_a, Write) }, Option<AccumulatedMemoryProgress>.None)
                        .WithCancellation(cancellation.Token)) {
                        result.IfLeft(e => throw e);
                    }
                }

                var waiting = WaitForInstall();
                Assert.False(waiting.IsCompleted);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.True(File.Exists(Path.Combine(_directory, ".unchained", "pending")));
                Assert.False(File.Exists(Path.Combine(_directory, ".unchained", "inventory.json")));
                Assert.Equal(0, _downloads);
                owner.Save(Array.Empty<ManagedPak>());
            }
            await Install(NewPakDir(), _a);
            Assert.Equal(1, _downloads);
        }

        [Fact]
        public async Task InvalidLockPathFailsWithoutRetrying() {
            Directory.CreateDirectory(Path.Combine(_directory, ".unchained", "inventory.lock"));
            var inventory = new PakInventory(_directory, new RetryTimeProvider());
            await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(async () => {
                using var handle = await inventory.Lock().WaitAsync(TimeSpan.FromSeconds(5));
            });
        }

        private sealed class RetryTimeProvider : TimeProvider {
            private readonly Channel<RetryTimer> _retries = Channel.CreateUnbounded<RetryTimer>();
            private long _elapsedTicks;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Interlocked.Read(ref _elapsedTicks);
            public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
                var timer = new RetryTimer(callback, state);
                _retries.Writer.TryWrite(timer);
                return timer;
            }

            public async Task AdvanceRetry(TimeSpan elapsed) {
                await FireRetry(elapsed);
                // The waiter must retry, not fail or acquire the still-held lock.
                Assert.True(await _retries.Reader.WaitToReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            }

            public async Task FireRetry(TimeSpan elapsed) {
                var timer = await _retries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Interlocked.Add(ref _elapsedTicks, elapsed.Ticks);
                timer.Fire();
            }

            private sealed class RetryTimer(TimerCallback callback, object? state) : ITimer {
                private bool _disposed;
                public void Fire() { if (!_disposed) callback(state); }
                public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
                public void Dispose() => _disposed = true;
                public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            }
        }

        [Fact]
        public async Task DifferentServerAndClientSetsReuseCommonPaksAndPreserveUnknownFiles() {
            var unknown = Path.Combine(_directory, "q__-__A.pak");
            File.WriteAllText(unknown, "user mod");
            var server = NewPakDir();
            var client = NewPakDir();
            await Install(server, _a, _b);
            await Install(client, _a);
            await Install(server, _a);

            Assert.Equal(2, _downloads);
            Assert.Equal("user mod", File.ReadAllText(unknown));
            Assert.Single(ReadInventory());
            Assert.Equal(2, Directory.GetFiles(_directory, "*.pak").Length);
        }

        [Fact]
        public async Task SuccessfulInstallsArePublishedBeforeEnumeratorDisposal() {
            var server = NewPakDir();
            await using (var results = server.InstallModSet(
                new[] { new ModInstallRequest(_a, Write), new ModInstallRequest(_b, Write) },
                Option<AccumulatedMemoryProgress>.None).GetAsyncEnumerator()) {
                Assert.True(await results.MoveNextAsync());
                results.Current.IfLeft(e => throw e);
                Assert.Single(ReadInventory());
            }
            await Install(NewPakDir(), _a);
            Assert.Equal(1, _downloads);
        }

        [Fact]
        public async Task FailedDownloadDoesNotLoseEarlierInstalls() {
            var results = await NewPakDir().InstallModSet(new[] {
                new ModInstallRequest(_a, Write),
                new ModInstallRequest(_b, _ => EitherAsync<Error, FileWriter>.Left(Error.New("download failed")))
            }, Option<AccumulatedMemoryProgress>.None).ToListAsync();

            Assert.True(results[0].IsRight);
            Assert.True(results[1].IsLeft);
            Assert.Single(ReadInventory());
            await Install(NewPakDir(), _a);
            Assert.Equal(1, _downloads);
        }

        [Fact]
        public async Task StaleUninstallReloadsCurrentFileNameAndPublishesRemoval() {
            var server = NewPakDir();
            var client = NewPakDir();
            await Install(server, _a, _b);
            await Install(client, _b, _a);
            server.Uninstall(_a).IfLeft(e => throw e);

            Assert.DoesNotContain(ReadInventory(), p => p.Coordinates == _a);
            Assert.Single(Directory.GetFiles(_directory, "*.pak"));
            await Install(client, _b);
            Assert.Equal(2, _downloads);
        }

        [Fact]
        public async Task MigratesLegacyOwnershipWithoutAdoptingUnknownPaksOrChangingPreferences() {
            var managed = new ManagedPak(_a, "legacy.pak", 0);
            File.WriteAllText(Path.Combine(_directory, managed.PakFileName), "managed");
            var preferencesPath = Path.Combine(_directory, "mod-manager.json");
            File.WriteAllText(preferencesPath, "launcher preferences");
            var server = new ModManager(null!, NewPakDir(new[] { managed }), new[] { _b });
            var client = new ModManager(null!, NewPakDir(), new[] { _a });
            await Install((LauncherPakDir)server.PakDir, _a);
            await Install((LauncherPakDir)client.PakDir, _a);

            Assert.Equal(0, _downloads);
            Assert.Equal(new[] { _b }, server.EnabledModReleaseCoordinates);
            Assert.Equal(new[] { _a }, client.EnabledModReleaseCoordinates);
            Assert.Equal("launcher preferences", File.ReadAllText(preferencesPath));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("{broken")]
        [InlineData("[{\"Coordinates\":{\"Org\":\"test\",\"ModuleName\":\"A\",\"Version\":\"1\"},\"PakFileName\":\"../unknown.pak\",\"Priority\":0}]")]
        public async Task InvalidInventoryFailsClosed(string inventory) {
            Directory.CreateDirectory(Path.Combine(_directory, ".unchained"));
            File.WriteAllText(Path.Combine(_directory, ".unchained", "inventory.json"), inventory);
            var results = await NewPakDir().InstallModSet(new[] { new ModInstallRequest(_a, Write) },
                Option<AccumulatedMemoryProgress>.None).ToListAsync();

            Assert.True(Assert.Single(results).IsLeft);
            Assert.Equal(0, _downloads);
            Assert.Equal(inventory, File.ReadAllText(Path.Combine(_directory, ".unchained", "inventory.json")));
        }

        [Fact]
        public async Task InterruptedPreparationFailsClosedInsteadOfRedownloadingUntrackedPaks() {
            await Install(NewPakDir(), _a);
            File.Move(Directory.GetFiles(_directory, "*.pak").Single(), Path.Combine(_directory, "interrupted-move.pak"));
            File.WriteAllText(Path.Combine(_directory, ".unchained", "pending"), "");
            var results = await NewPakDir().InstallModSet(new[] { new ModInstallRequest(_a, Write) },
                Option<AccumulatedMemoryProgress>.None).ToListAsync();

            Assert.True(Assert.Single(results).IsLeft);
            Assert.Equal(1, _downloads);
            Assert.Single(Directory.GetFiles(_directory, "*.pak"));
        }

        [Fact]
        public async Task PublicationFailureLeavesAnInterruptionMarkerAndDoesNotReplaceInventory() {
            var results = await NewPakDir().InstallModSet(new[] {
                new ModInstallRequest(_a, path => {
                    // Make atomic publication fail after the pak download succeeds.
                    Directory.CreateDirectory(Path.Combine(_directory, ".unchained", "inventory.tmp"));
                    return Write(path);
                })
            }, Option<AccumulatedMemoryProgress>.None).ToListAsync();

            Assert.True(Assert.Single(results).IsLeft);
            Assert.Empty(ReadInventory());
            Assert.True(File.Exists(Path.Combine(_directory, ".unchained", "pending")));
            var retry = await NewPakDir().InstallModSet(new[] { new ModInstallRequest(_a, Write) },
                Option<AccumulatedMemoryProgress>.None).ToListAsync();
            Assert.True(Assert.Single(retry).IsLeft);
            Assert.Equal(1, _downloads);
        }

        [Fact]
        public async Task FailedStreamDoesNotLeaveAnUnmanagedPartialDownload() {
            var results = await NewPakDir().InstallModSet(new[] {
                new ModInstallRequest(_a, path => new FileWriter(path, new FailedStream(), 3))
            }, Option<AccumulatedMemoryProgress>.None).ToListAsync();

            Assert.True(Assert.Single(results).IsLeft);
            Assert.Empty(Directory.GetFiles(_directory, "*.pak"));
            Assert.Empty(ReadInventory());
            await Install(NewPakDir(), _a);
            Assert.Equal(1, _downloads);
            Assert.Equal("q__-__A.pak", Assert.Single(ReadInventory()).PakFileName);
        }

        [Fact]
        public async Task SignatureCollisionRollsBackPakRenameAndPreservesUnknownSignature() {
            await Install(NewPakDir(), _a);
            var unknownSignature = Path.Combine(_directory, "qz__-__A.sig");
            File.WriteAllText(unknownSignature, "user signature");
            var results = await NewPakDir().InstallModSet(new[] {
                new ModInstallRequest(_a, Write), new ModInstallRequest(_b, Write)
            }, Option<AccumulatedMemoryProgress>.None).ToListAsync();

            Assert.Contains(results, r => r.IsLeft);
            Assert.Equal("user signature", File.ReadAllText(unknownSignature));
            Assert.All(ReadInventory(), p => Assert.True(File.Exists(Path.Combine(_directory, p.PakFileName))));
            Assert.Equal("q__-__A.pak", ReadInventory().Single(p => p.Coordinates == _a).PakFileName);
        }

        private sealed class FailedStream : MemoryStream {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                ValueTask.FromException<int>(new IOException("stream failed"));
        }

        [Fact]
        public async Task ResetPublishesEmptyOwnershipWithoutRemovingTheLockFile() {
            var stale = NewPakDir();
            await Install(NewPakDir(), _a);
            stale.Reset().IfLeft(errors => throw Error.Many(errors.ToSeq()));
            Assert.Empty(ReadInventory());
            Assert.True(File.Exists(Path.Combine(_directory, ".unchained", "inventory.lock")));
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
