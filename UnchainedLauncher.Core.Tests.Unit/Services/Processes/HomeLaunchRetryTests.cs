using LanguageExt;
using Moq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes {
    [CollectionDefinition("Home launch UI", DisableParallelization = true)]
    public class HomeLaunchCollection : ICollectionFixture<HomeLaunchUi> { }

    public sealed class HomeLaunchUi : IDisposable {
        private readonly Thread _thread;
        private readonly TaskCompletionSource<Dispatcher> _ready = new();

        public HomeLaunchUi() {
            _thread = new Thread(() => {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                _ready.SetResult(app.Dispatcher);
                app.Run();
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public void Run(Action test) => _ready.Task.GetAwaiter().GetResult().Invoke(test);

        public Task RunAsync(Func<Task> test) => _ready.Task.GetAwaiter().GetResult().InvokeAsync(test).Task.Unwrap();

        public void Dispose() {
            _ready.Task.GetAwaiter().GetResult().Invoke(() => Application.Current.Shutdown());
            Assert.True(_thread.Join(TimeSpan.FromSeconds(10)), "Home test application did not shut down.");
        }
    }

    [Collection("Home launch UI")]
    public class HomeLaunchRetryTests(HomeLaunchUi ui) {
        private sealed class LaunchHarness : IDisposable {
            public Mock<IChivalry2Launcher> Vanilla { get; } = new();
            public Mock<IChivalry2Launcher> Modded { get; } = new();
            public Mock<IUserDialogueSpawner> Dialogue { get; } = new();
            public List<Action<int, bool>> Exits { get; } = [];
            public List<LaunchOptions> Options { get; } = [];
            public Action? OnAttached { get; set; }
            private readonly List<Process> _processes = [];
            public HomeVM Home { get; }

            public LaunchHarness() {
                var settings = new SettingsVM(null!, null!, InstallationType.Steam, true, false, "", "https://example.test", false, false, "-test");
                var mods = new Mock<IModManager>();
                var info = new ModInfo("https://github.com/Chiv2-Community/Unchained-Mods", "Unchained-Mods", "", null, null, [], [], []);
                var release = new Release("v1", "hash", "mod.pak", DateTime.UtcNow, info, new AssetCollections([], [], [], [], []), null);
                var mod = new Mod(info, [release]);
                mods.SetupGet(m => m.Mods).Returns([mod]);
                mods.SetupGet(m => m.EnabledModReleaseCoordinates).Returns([]);
                mods.Setup(m => m.UpdateModsList()).ReturnsAsync(new GetAllModsResult([], [mod]));
                foreach (var launcher in new[] { Vanilla, Modded }) {
                    launcher.Setup(l => l.Launch(It.IsAny<LaunchOptions>()))
                        .Returns((LaunchOptions options) => {
                            Options.Add(options);
                            var process = new Process();
                            _processes.Add(process);
                            return Task.FromResult<Either<LaunchFailed, Process>>(process);
                        });
                }
                var watcher = new Mock<IChivalryProcessWatcher>();
                watcher.Setup(w => w.OnExit(It.IsAny<Process>(), It.IsAny<Action<int, bool>>()))
                    .Callback<Process, Action<int, bool>>((_, exit) => {
                        Exits.Add(exit);
                        OnAttached?.Invoke();
                    })
                    .ReturnsAsync(true);
                Home = new HomeVM(settings, mods.Object, Vanilla.Object, Modded.Object, Dialogue.Object, watcher.Object);
            }

            public void Dispose() {
                foreach (var process in _processes) process.Dispose();
            }
        }

        [Fact]
        public void RepeatedPatchFailureRetriesOnceThenRestoresWindowAndFreshLaunchGetsNewBudget() => ui.Run(() => {
            using var h = new LaunchHarness();
            Assert.True(h.Home.LaunchUnchained().GetAwaiter().GetResult().IsSome);
            Assert.Equal(Visibility.Hidden, h.Home.MainWindowVisibility);
            h.Exits[0](-67, false);
            Assert.Equal(2, h.Exits.Count);
            Assert.Same(h.Options[0], h.Options[1]);
            Assert.Equal(Visibility.Hidden, h.Home.MainWindowVisibility);
            h.Exits[1](-67, false);
            h.Modded.Verify(l => l.Launch(It.IsAny<LaunchOptions>()), Times.Exactly(2));
            Assert.Equal(Visibility.Visible, h.Home.MainWindowVisibility);
            h.Dialogue.Verify(d => d.DisplayMessage(It.Is<string>(s => s.Contains("-67"))), Times.Once);

            h.Home.LaunchUnchained().GetAwaiter().GetResult();
            h.Exits[2](-67, false);
            Assert.Equal(4, h.Exits.Count);
            h.Exits[3](0, true);
            Assert.Equal(Visibility.Visible, h.Home.MainWindowVisibility);
            h.Vanilla.Verify(l => l.Launch(It.IsAny<LaunchOptions>()), Times.Never);
        });

        [Fact]
        public Task AsynchronousReplacementStillHasNoRetryBudget() => ui.RunAsync(async () => {
            using var h = new LaunchHarness();
            await h.Home.LaunchUnchained();
            var retry = new TaskCompletionSource<Either<LaunchFailed, Process>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var attached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.OnAttached = () => attached.SetResult();
            h.Modded.Setup(l => l.Launch(It.IsAny<LaunchOptions>())).Returns(retry.Task);
            h.Exits[0](-67, false);
            Assert.Single(h.Exits);
            Assert.Equal(Visibility.Hidden, h.Home.MainWindowVisibility);
            using var replacement = new Process();
            retry.SetResult(replacement);
            await attached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Let the original async exit callback finish before delivering the next exit.
            await Dispatcher.Yield(DispatcherPriority.Background);
            h.Exits[1](-67, false);
            h.Modded.Verify(l => l.Launch(It.IsAny<LaunchOptions>()), Times.Exactly(2));
            Assert.Equal(Visibility.Visible, h.Home.MainWindowVisibility);
            h.Dialogue.Verify(d => d.DisplayMessage(It.Is<string>(s => s.Contains("-67"))), Times.Once);
        });

        [Fact]
        public void VanillaPatchFailureDoesNotLaunchModdedOrRetry() => ui.Run(() => {
            using var h = new LaunchHarness();
            h.Home.LaunchVanilla().GetAwaiter().GetResult();
            h.Exits[0](-67, false);
            h.Vanilla.Verify(l => l.Launch(It.IsAny<LaunchOptions>()), Times.Once);
            h.Modded.Verify(l => l.Launch(It.IsAny<LaunchOptions>()), Times.Never);
            Assert.Single(h.Exits);
            Assert.Equal(Visibility.Visible, h.Home.MainWindowVisibility);
            h.Dialogue.Verify(d => d.DisplayMessage(It.Is<string>(s => s.Contains("-67"))), Times.Once);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedOrThrowingRetryRestoresWindow(bool throws) => ui.Run(() => {
            using var h = new LaunchHarness();
            h.Home.LaunchUnchained().GetAwaiter().GetResult();
            if (throws)
                h.Modded.Setup(l => l.Launch(It.IsAny<LaunchOptions>())).ThrowsAsync(new InvalidOperationException("retry failed"));
            else
                h.Modded.Setup(l => l.Launch(It.IsAny<LaunchOptions>()))
                    .ReturnsAsync(UnchainedLaunchFailure.InjectionFailed().AsLaunchFailed("-test"));
            h.Exits[0](-67, false);
            Assert.Single(h.Exits);
            Assert.Equal(Visibility.Visible, h.Home.MainWindowVisibility);
            h.Dialogue.Verify(d => d.DisplayMessage(It.IsAny<string>()), Times.Once);
        });

        [Theory]
        [InlineData(0, true)]
        [InlineData(42, false)]
        public void NonPatchExitDoesNotRetry(int code, bool acceptable) => ui.Run(() => {
            using var h = new LaunchHarness();
            h.Home.LaunchUnchained().GetAwaiter().GetResult();
            h.Exits[0](code, acceptable);
            h.Modded.Verify(l => l.Launch(It.IsAny<LaunchOptions>()), Times.Once);
            Assert.Equal(Visibility.Visible, h.Home.MainWindowVisibility);
            h.Dialogue.Verify(d => d.DisplayMessage(It.IsAny<string>()), acceptable ? Times.Never() : Times.Once());
        });
    }
}