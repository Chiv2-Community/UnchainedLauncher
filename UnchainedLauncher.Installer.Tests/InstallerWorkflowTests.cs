using Semver;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Windows;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.GUI.ViewModels.Installer;
using UnchainedLauncher.GUI.Views.Installer;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UnchainedLauncher.Installer.Tests {
    public class InstallerWorkflowTests {
        private static DirectoryInfo Target(string name) => new(Path.Combine(Path.GetTempPath(), "installer-tests", name));
        private static ReleaseTarget Release => new("", "", new SemVersion(1, 0), Array.Empty<ReleaseAsset>(), DateTimeOffset.Now, true, false);

        public InstallerWorkflowTests() {
            MessageBox.Messages.Clear();
            MessageBox.Result = MessageBoxResult.No;
            LaunchUnchainedLauncherWindow.Show(Array.Empty<InstallationTargetViewModel>());
        }

        [Fact]
        public void StartupScansOnceOffThreadAndPublishesOnUiThread() => UiThread.Run(async () => {
            var owner = Environment.CurrentManagedThreadId;
            var finder = new Chivalry2InstallationFinder { Steam = Target("steam") };
            var page = new InstallationSelectionPageViewModel(finder);
            Assert.Equal(0, finder.Scans);
            var changes = 0;
            page.Installations.CollectionChanged += (_, _) => {
                Assert.Equal(owner, Environment.CurrentManagedThreadId);
                changes++;
            };
            var window = new InstallerWindowViewModel(new() { page }, page.Installations);
            while (page.IsScanning) await Task.Yield();
            await page.Load();
            Assert.Equal(1, finder.Scans);
            Assert.NotEqual(owner, finder.ScanThread);
            Assert.True(changes > 0);
            Assert.Single(page.Installations);
            Assert.False(window.CanGoBack);
            Assert.Equal(Visibility.Hidden, window.DisplayGoBackButton);
            await window.BackButtonCommand.ExecuteAsync(null);
            Assert.Equal(0, window.CurrentPageIndex);
            window.CurrentPageIndex = -1;
            Assert.Equal(0, window.CurrentPageIndex);
            window.CurrentPageIndex = 1;
            Assert.Equal(0, window.CurrentPageIndex);
        });

        [Fact]
        public void BrowseAndEveryCollectionChangeRecomputeSelection() => UiThread.Run(async () => {
            var owner = Environment.CurrentManagedThreadId;
            var page = new InstallationSelectionPageViewModel(new Chivalry2InstallationFinder());
            page.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(page.CanContinue))
                    Assert.Equal(owner, Environment.CurrentManagedThreadId);
            };
            await page.Load();
            Assert.False(page.CanContinue);
            page.BrowseForInstallationCommand.Execute(null);
            var target = Assert.Single(page.Installations);
            Assert.True(target.IsSelected);
            Assert.True(page.CanContinue);
            target.IsSelected = false;
            Assert.False(page.CanContinue);
            target.IsSelected = true;
            Assert.True(page.CanContinue);
            page.Installations.Remove(target);
            Assert.False(page.CanContinue);
            page.Installations.Add(target);
            Assert.True(page.CanContinue);
            page.Installations.Clear();
            Assert.False(page.CanContinue);
        });

        [Fact]
        public void RescanClearsStaleValidityAndNavigationRejectsEmptySelection() => UiThread.Run(async () => {
            var page = new InstallationSelectionPageViewModel(new Chivalry2InstallationFinder { Steam = Target("steam") });
            var next = new TestPage();
            var window = new InstallerWindowViewModel(new() { page, next }, page.Installations);
            while (page.IsScanning) await Task.Yield();
            page.Installations.Single().IsSelected = true;
            Assert.True(window.CanContinue);
            await page.ScanForInstallationsCommand.ExecuteAsync(null);
            Assert.False(page.CanContinue);
            Assert.False(window.CanContinue);
            Assert.False(page.Installations.Single().IsSelected);
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.Equal(0, window.CurrentPageIndex);
            // A stale validity flag must not advance an empty selection.
            page.CanContinue = true;
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.Equal(0, window.CurrentPageIndex);
            page.Installations.Single().IsSelected = true;
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.Equal(1, window.CurrentPageIndex);
            await window.BackButtonCommand.ExecuteAsync(null);
            await window.BackButtonCommand.ExecuteAsync(null);
            Assert.Equal(0, window.CurrentPageIndex);
        });

        [Fact]
        public void ScanErrorsAreObservedAndCanBeRetried() => UiThread.Run(async () => {
            var finder = new Chivalry2InstallationFinder { ThrowOnScan = true };
            var page = new InstallationSelectionPageViewModel(finder);
            var window = new InstallerWindowViewModel(new() { page }, page.Installations);
            while (page.IsScanning) await Task.Yield();
            Assert.Contains(MessageBox.Messages, m => m.Contains("Discovery failed"));
            Assert.False(page.IsScanning);
            Assert.False(window.CanContinue);
            finder.ThrowOnScan = false;
            finder.Steam = Target("steam");
            await page.ScanForInstallationsCommand.ExecuteAsync(null);
            Assert.Single(page.Installations);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DiscoveryFailurePreservesOtherStoreAndUiGuards(bool steamFails) => UiThread.Run(async () => {
            var owner = Environment.CurrentManagedThreadId;
            var finder = new Chivalry2InstallationFinder {
                Steam = Target("steam"),
                Epic = Target("epic"),
                ThrowOnScan = steamFails,
                ThrowOnEpicScan = !steamFails
            };
            var page = new InstallationSelectionPageViewModel(finder);
            page.Installations.CollectionChanged += (_, _) => {
                Assert.Equal(owner, Environment.CurrentManagedThreadId);
                Assert.True(page.IsScanning);
                Assert.False(page.CanContinue);
            };
            var window = new InstallerWindowViewModel(new() { page, new TestPage() }, page.Installations);
            while (page.IsScanning) await Task.Yield();

            Assert.Equal(1, finder.Scans);
            Assert.Equal(1, finder.EpicScans);
            Assert.NotEqual(owner, finder.ScanThread);
            Assert.NotEqual(owner, finder.EpicScanThread);
            Assert.Equal("Error scanning for installations: " + (steamFails ? "Discovery failed" : "Corrupt Epic metadata"),
                Assert.Single(MessageBox.Messages));
            var target = Assert.Single(page.Installations);
            Assert.Equal(steamFails ? finder.Epic : finder.Steam, target.Path);
            Assert.Equal(steamFails ? InstallationType.EpicGamesStore : InstallationType.Steam, target.InstallationType);
            Assert.False(page.IsScanning);
            Assert.False(page.CanContinue);
            Assert.False(window.CanContinue);
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.Equal(0, window.CurrentPageIndex);
            target.IsSelected = true;
            Assert.True(page.CanContinue);
            Assert.True(window.CanContinue);
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.Equal(1, window.CurrentPageIndex);
        });

        [Theory]
        [InlineData(false, false, 0)]
        [InlineData(true, false, 1)]
        [InlineData(false, true, 1)]
        [InlineData(true, true, 2)]
        public async Task ResultsGateSuccessAndFinish(bool first, bool second, int successful) {
            var installer = new MockInstaller();
            installer.Results.Enqueue(first);
            installer.Results.Enqueue(second);
            var page = new InstallerLogPageViewModel(installer, () => new[] { Target("one"), Target("two") }, () => Release);
            await page.Load();
            Assert.Equal(2, installer.Calls.Count);
            Assert.Equal(successful, page.SuccessfulTargets.Count);
            Assert.Equal(first && second, page.CanContinue);
            Assert.Equal(!(first && second), page.CanGoBack);
            Assert.Equal(first && second, page.Log.Contains("✅ Installation complete!"));
            Assert.Equal(first && second, MessageBox.Messages.Any(m => m.Contains("installed successfully")));
            if (!second) {
                Assert.Contains(Target("two").FullName, page.Log);
                Assert.Contains("go back", page.Log);
            }
        }

        [Fact]
        public async Task EmptyTargetsNeverInstallOrReportSuccess() {
            var installer = new MockInstaller();
            var page = new InstallerLogPageViewModel(installer, () => Array.Empty<DirectoryInfo>(), () => throw new Exception("Release must not be needed"));
            await page.Load();
            Assert.Empty(installer.Calls);
            Assert.False(page.CanContinue);
            Assert.True(page.CanGoBack);
            Assert.Empty(MessageBox.Messages);
            Assert.Contains("No installation targets", page.Log);
        }

        [Fact]
        public async Task ThrownInstallErrorsOfferRecoveryAndRetryResetsResults() {
            var installer = new MockInstaller { ThrowOnInstall = true };
            var page = new InstallerLogPageViewModel(installer, () => new[] { Target("one") }, () => Release);
            await page.Load();
            Assert.False(page.CanContinue);
            Assert.True(page.CanGoBack);
            Assert.Contains("Installation failed", page.Log);
            installer.ThrowOnInstall = false;
            await page.Load();
            Assert.True(page.CanContinue);
            Assert.False(page.CanGoBack);
            Assert.Single(page.SuccessfulTargets);
            Assert.DoesNotContain("Installation failed", page.Log);
        }

        [Fact]
        public async Task FailedInstallCannotFinishAndBackAllowsRetry() {
            var installer = new MockInstaller();
            installer.Results.Enqueue(false);
            var log = new InstallerLogPageViewModel(installer, () => new[] { Target("one") }, () => Release);
            var window = new InstallerWindowViewModel(new() { new TestPage(), log }, new());
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.False(window.CanContinue);
            Assert.True(window.CanGoBack);
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.False(window.Finished);
            Assert.Empty(MessageBox.Messages);
            await window.BackButtonCommand.ExecuteAsync(null);
            Assert.Equal(0, window.CurrentPageIndex);
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.True(window.CanContinue);
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.True(window.Finished);
        }

        [Fact]
        public async Task FailedInstallRefreshesBoundBackLabelAndRetryClearsIt() {
            var installer = new MockInstaller();
            installer.Results.Enqueue(false);
            var log = new InstallerLogPageViewModel(installer, () => new[] { Target("one") }, () => Release);
            var window = new InstallerWindowViewModel(new() { new TestPage(), log }, new());
            var boundBackText = window.GoBackButtonText;
            var labels = new List<string>();
            window.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(window.GoBackButtonText)) {
                    boundBackText = window.GoBackButtonText;
                    labels.Add(boundBackText);
                }
            };

            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.True(window.CanGoBack);
            Assert.Equal(Visibility.Visible, window.DisplayGoBackButton);
            Assert.Equal("Back", boundBackText);
            Assert.Contains("", labels);
            Assert.Equal("Back", labels.Last());

            await window.BackButtonCommand.ExecuteAsync(null);
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.True(window.CanContinue);
            Assert.False(window.CanGoBack);
            Assert.Equal(Visibility.Hidden, window.DisplayGoBackButton);
            Assert.Equal("", boundBackText);
            Assert.Equal("", labels.Last());
        }

        [Fact]
        public async Task NavigationIsBlockedWhileLoadingAndLoadErrorsAreObserved() {
            var completion = new TaskCompletionSource();
            var first = new TestPage();
            var second = new TestPage { OnLoad = () => completion.Task };
            var window = new InstallerWindowViewModel(new() { first, second }, new());
            var advancing = window.NextButtonCommand.ExecuteAsync(null);
            Assert.False(window.CanContinue);
            Assert.False(window.CanGoBack);
            await window.BackButtonCommand.ExecuteAsync(null);
            Assert.Equal(1, window.CurrentPageIndex);
            completion.SetException(new IOException("Load failed"));
            await advancing;
            Assert.Contains(MessageBox.Messages, m => m.Contains("Load failed"));
            Assert.True(window.CanGoBack);
        }

        [Fact]
        public async Task InitialLoadBlocksBothNavigationCommands() {
            var completion = new TaskCompletionSource();
            var page = new TestPage { OnLoad = () => completion.Task };
            var window = new InstallerWindowViewModel(new() { page, new TestPage() }, new());
            Assert.False(window.CanContinue);
            Assert.False(window.CanGoBack);
            await window.NextButtonCommand.ExecuteAsync(null);
            await window.BackButtonCommand.ExecuteAsync(null);
            Assert.Equal(0, window.CurrentPageIndex);
            completion.SetResult();
            while (!window.CanContinue) await Task.Yield();
        }

        [Fact]
        public void InitialLoadErrorsAreObserved() {
            var page = new TestPage { OnLoad = () => throw new IOException("Initial load failed") };
            var window = new InstallerWindowViewModel(new() { page }, new());
            Assert.Contains(MessageBox.Messages, m => m.Contains("Initial load failed"));
            Assert.False(window.Finished);
            Assert.Equal(0, window.CurrentPageIndex);
        }

        [Fact]
        public async Task DisabledPagesCannotAdvanceThroughTheCommand() {
            var page = new TestPage { CanContinue = false };
            var window = new InstallerWindowViewModel(new() { page, new TestPage() }, new());
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.Equal(0, window.CurrentPageIndex);
            Assert.False(window.Finished);
        }

        [Fact]
        public async Task FinishLaunchesOnlySuccessfulSelectedTargets() {
            var selected = new InstallationTargetViewModel(Target("one"), InstallationType.Steam, true);
            var uninstalled = new InstallationTargetViewModel(Target("other"), InstallationType.Steam, true);
            var unselected = new InstallationTargetViewModel(Target("unselected"), InstallationType.Steam, false);
            var page = new InstallerLogPageViewModel(new MockInstaller(), () => new[] { selected.Path, unselected.Path }, () => Release);
            await page.Load();
            var window = new InstallerWindowViewModel(new() { new TestPage(), page }, new() { selected, uninstalled, unselected });
            await window.NextButtonCommand.ExecuteAsync(null);
            MessageBox.Result = MessageBoxResult.Yes;
            await window.NextButtonCommand.ExecuteAsync(null);
            Assert.Same(selected, Assert.Single(LaunchUnchainedLauncherWindow.Targets));
        }

        [Fact]
        public void EmptyLaunchCandidatesDoNotThrowOrStartAProcess() {
            var closed = false;
            var page = new LaunchUnchainedLauncherWindowViewModel(Array.Empty<InstallationTargetViewModel>(), () => closed = true);
            Assert.Null(page.SelectedTarget);
            Assert.Empty(page.LaunchTargets);
            page.LaunchCommand.Execute(null);
            Assert.False(closed);
            Assert.Single(MessageBox.Messages);
        }

        [PropertyChanged.DoNotNotify]
        private class TestPage : IInstallerPageViewModel {
            public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
            public string TitleText => "Test";
            public string? DescriptionText => null;
            public bool CanContinue { get; set; } = true;
            public string ContinueButtonText => "Continue";
            public bool CanGoBack => true;
            public string? GoBackButtonText => "Back";
            public Func<Task> OnLoad { get; set; } = () => Task.CompletedTask;
            public Task Load() => OnLoad();
            public Task Continue() => Task.CompletedTask;
        }

        private sealed class UiThread : SynchronizationContext {
            private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
            public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

            public static void Run(Func<Task> action) {
                var previous = Current;
                var context = new UiThread();
                SetSynchronizationContext(context);
                try {
                    var task = action();
                    var timeout = DateTime.UtcNow.AddSeconds(15);
                    while (!task.IsCompleted) {
                        if (DateTime.UtcNow > timeout) throw new TimeoutException("UI-context test did not complete");
                        if (context._queue.TryTake(out var work, 100)) work.Callback(work.State);
                    }
                    task.GetAwaiter().GetResult();
                }
                finally {
                    SetSynchronizationContext(previous);
                }
            }
        }
    }
}