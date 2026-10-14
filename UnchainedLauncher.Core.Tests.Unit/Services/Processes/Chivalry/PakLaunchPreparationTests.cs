using System.Diagnostics;
using System.Security.Cryptography;
using LanguageExt;
using LanguageExt.Common;
using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using ManagedPakDirectory = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes.Chivalry;

public sealed class PakLaunchPreparationTests : IDisposable {
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pak-launch-" + Guid.NewGuid().ToString("N"));
    private readonly ReleaseCoordinates _coords = new("Chiv2-Community", "Unchained-Mods", "v0.1.0");
    private const string Preferred = "q__-__Unchained-Mods.pak";
    private const string Installed = "q__-__Unchained-Mods (1).pak";
    private sealed class ImmediateContext : SynchronizationContext {
        public override void Post(SendOrPostCallback callback, object? state) {
            var previous = Current;
            SetSynchronizationContext(this);
            try { callback(state); }
            finally { SetSynchronizationContext(previous); }
        }
    }

    public PakLaunchPreparationTests() {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "pakchunk0-WindowsNoEditor.sig"), "signature");
    }

    private ModManager Manager(ManagedPakDirectory pakDir) {
        var info = new ModInfo("https://github.com/Chiv2-Community/Unchained-Mods", "Unchained-Mods", "test", null, null, [], [], []);
        var hash = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(Path.Combine(_dir, Installed)))).ToLowerInvariant();
        var release = new Release(_coords.Version, hash, "https://example.test/mod.pak", DateTime.UtcNow,
            info, new AssetCollections([], [], [], [], []), null);
        return new ModManager(Mock.Of<IModRegistry>(), pakDir, [_coords], [new Mod(info, [release])]);
    }

    private static LaunchOptions Options() => new([], Option<string>.None, "", false, false,
        Option<string>.None, Option<ServerLaunchOptions>.None);

    private sealed class Capture : IProcessLauncher {
        public string Executable => "test.exe";
        public int Calls { get; private set; }
        public Action? VerifyPrepared { get; set; }
        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args) {
            Calls++;
            VerifyPrepared?.Invoke();
            return Prelude.Left(new LaunchFailed(Executable, args, Error.New("test boundary")));
        }
    }

    [Fact]
    public async Task OccupiedUnmanagedNameStaysUntouchedAcrossRepeatedPreparations() {
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        File.WriteAllText(Path.Combine(_dir, Preferred), "unmanaged bytes");
        File.WriteAllText(Path.Combine(_dir, Installed), "registered bytes");
        var pakDir = new ManagedPakDirectory(_dir, [new ManagedPak(_coords, Installed, 0)]);
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<UnchainedLauncher.Core.Utilities.MemoryProgress>())).Returns(() => { });
        IChivalry2LaunchPreparer<LaunchOptions> installer = new Chivalry2ModsInstaller(Manager(pakDir), dialogue.Object);
        var preparer = installer.Sub(SigPreparer.Create(pakDir, dialogue.Object));
        var capture = new Capture { VerifyPrepared = () => Assert.True(File.Exists(Path.ChangeExtension(Path.Combine(_dir, Installed), ".sig"))) };
        var launcher = new UnchainedChivalry2Launcher(preparer, capture, _dir, new NullInjector());
        await launcher.Launch(Options());
        await launcher.Launch(Options());
        Assert.Equal(2, capture.Calls);
        Assert.Equal("unmanaged bytes", File.ReadAllText(Path.Combine(_dir, Preferred)));
        Assert.Equal("registered bytes", File.ReadAllText(Path.Combine(_dir, Installed)));
        dialogue.Verify(d => d.DisplayMessage(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPakMoveCancelsLaunchAndClosesProgress(bool vanilla) {
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        File.WriteAllText(Path.Combine(_dir, Installed), "registered bytes");
        Directory.CreateDirectory(Path.Combine(_dir, Preferred)); // Deterministic destination failure.
        var pakDir = new ManagedPakDirectory(_dir, [new ManagedPak(_coords, Installed, 0)]);
        var closed = false;
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<UnchainedLauncher.Core.Utilities.MemoryProgress>())).Returns(() => closed = true);
        IChivalry2LaunchPreparer<LaunchOptions> preparer = new Chivalry2ModsInstaller(Manager(pakDir), dialogue.Object);
        var capture = new Capture();
        IChivalry2Launcher launcher = vanilla
            ? new Chivalry2Launcher(preparer, capture, _dir)
            : new UnchainedChivalry2Launcher(preparer, capture, _dir, new NullInjector());
        Assert.True((await launcher.Launch(Options())).IsLeft);
        Assert.Equal(0, capture.Calls);
        Assert.True(closed);
        dialogue.Verify(d => d.DisplayMessage(It.Is<string>(text => text.Contains("Launch cancelled"))), Times.Once);
        dialogue.Verify(d => d.DisplayYesNoMessage(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Equal("registered bytes", File.ReadAllText(Path.Combine(_dir, Installed)));
    }

    [Fact]
    public async Task FailedPakRemovalBlocksLaunchAndPreservesPak() {
        SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
        File.WriteAllText(Path.Combine(_dir, Installed), "registered bytes");
        File.WriteAllText(Path.Combine(_dir, Preferred), "unmanaged bytes");
        var stale = new ReleaseCoordinates("org", "stale", "v1.0.0");
        File.WriteAllText(Path.Combine(_dir, "stale.pak"), "stale bytes");
        Directory.CreateDirectory(Path.Combine(_dir, "stale.sig"));
        var pakDir = new ManagedPakDirectory(_dir, [new ManagedPak(_coords, Installed, 0), new ManagedPak(stale, "stale.pak", 1)]);
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<UnchainedLauncher.Core.Utilities.MemoryProgress>())).Returns(() => { });
        var capture = new Capture();
        var launcher = new UnchainedChivalry2Launcher(new Chivalry2ModsInstaller(Manager(pakDir), dialogue.Object), capture, _dir, new NullInjector());
        Assert.True((await launcher.Launch(Options())).IsLeft);
        Assert.Equal(0, capture.Calls);
        Assert.Equal("stale bytes", File.ReadAllText(Path.Combine(_dir, "stale.pak")));
        dialogue.Verify(d => d.DisplayMessage(It.Is<string>(text => text.Contains("Launch cancelled"))), Times.Once);
    }

    [Fact]
    public async Task LaunchWaitsForEveryStageAndPassesPreparedOptions() {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<Option<LaunchOptions>>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChivalry2LaunchPreparer<LaunchOptions> first = Chivalry2LaunchPreparer.Create<LaunchOptions>(async _ => {
            started.SetResult();
            return await finish.Task;
        });
        var stages = 0;
        var prepared = Options() with { LaunchArgs = "-prepared" };
        var chain = first.AndThen(opts => { Assert.Same(prepared, opts); stages++; return Task.FromResult(Prelude.Some(opts)); });
        var capture = new Capture { VerifyPrepared = () => Assert.Equal(1, stages) };
        var launcher = new UnchainedChivalry2Launcher(chain, capture, _dir, new NullInjector());
        var launch = launcher.Launch(Options());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, capture.Calls);
        Assert.Equal(0, stages);
        finish.SetResult(Prelude.Some(prepared));
        await launch.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, capture.Calls);
    }

    public void Dispose() => Directory.Delete(_dir, true);
}