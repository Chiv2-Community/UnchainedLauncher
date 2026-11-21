using LanguageExt;
using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class PakLaunchPlanRecoveryTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pak-plan-recovery-" + Guid.NewGuid().ToString("N"));

    public PakLaunchPlanRecoveryTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("plugin")]
    [InlineData("cancel")]
    public async Task KeepRevalidatesSubstitutedReleaseBeforeOwnershipOrFilesystemMutation(string graph) {
        var source = Path.Combine(_directory, "old.pak");
        File.WriteAllText(source, "old approved bytes");
        var hash = FileHelpers.Sha512(source).IfLeft(error => throw new Exception(error.Message));
        Release Release(string tag, string releaseHash) => new(tag, releaseHash, "mod.pak", DateTime.UnixEpoch,
            new("https://github.com/org/mod", "mod", "", null, null, [], [], [], PluginDependencies: []), new([], [], [], [], []), null);
        var old = Release("v1.0.0", hash);
        if (graph == "missing") old.Info.Dependencies!.Add(new("https://github.com/org/absent", "*"));
        if (graph == "plugin") old.Info.PluginDependencies!.Add(new("https://github.com/org/absent", "*"));
        var latest = Release("v2.0.0", "different");
        var registry = new AggregateModRegistry();
        var paks = new PakDir(_directory, []);
        var manager = new ModManager(registry, paks, [ReleaseCoordinates.FromRelease(latest)], [new Mod(latest.Info, [latest, old])]);
        var before = PakLaunchPlan.Create(manager);
        var dialogue = new Mock<IUserDialogueSpawner>();
        dialogue.Setup(d => d.DisplayProgress(It.IsAny<MemoryProgress>())).Returns(() => { });
        dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(graph == "cancel" ? UserDialogueChoice.Cancel : UserDialogueChoice.Yes);
        using var http = new HttpClient();
        var dlls = DllManager.CreateForLaunch(Path.Combine(_directory, "plugins"), [], [], http, () => PakLaunchPlan.Create(manager).Releases);
        PakLaunchPlan? consumed = null;
        var installer = new Chivalry2ModsInstaller(manager, dialogue.Object, preparePlan: (roots, _) => {
            consumed = PakLaunchPlan.Create(manager, roots: roots);
            dlls.ValidatePakPlan(consumed);
            return consumed;
        });
        var options = new LaunchOptions([], "", "", false, false, Prelude.None, Prelude.None);
        Assert.Equal(graph == "valid", (await installer.PrepareLaunch(options)).IsSome);
        if (graph == "valid") {
            Assert.NotSame(before, consumed);
            Assert.Equal([old], consumed!.Releases);
            Assert.Equal(ReleaseCoordinates.FromRelease(old), Assert.Single(manager.EnabledModReleaseCoordinates));
            Assert.Single(paks.ManagedPaks);
            Assert.Equal("old approved bytes", File.ReadAllText(Path.Combine(_directory, paks.ManagedPaks.Single().PakFileName)));
            Assert.Same(consumed, PakLaunchPlan.Create(manager));
        }
        else {
            Assert.Equal("old approved bytes", File.ReadAllText(source));
            Assert.Empty(paks.ManagedPaks);
            Assert.Equal(ReleaseCoordinates.FromRelease(latest), Assert.Single(manager.EnabledModReleaseCoordinates));
            Assert.Equal([source], Directory.GetFiles(_directory, "*.pak"));
            if (graph == "cancel") Assert.Null(consumed);
        }
    }
}
