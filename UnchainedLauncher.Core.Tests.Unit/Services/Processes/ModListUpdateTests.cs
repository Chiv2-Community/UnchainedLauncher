using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes;

[Collection("Home launch UI")]
public class ModListUpdateTests(HomeLaunchUi ui) {
    private static Mod Mod(string name) {
        var info = new ModInfo($"https://github.com/Org/{name}", name, "", null, null, [], [], []);
        var old = new Release("v1.0.0", "", "", DateTime.UtcNow, info, null!, null);
        return new Mod(info, [old with { Tag = "v2.0.0" }, old]);
    }

    [Fact]
    public Task UpdateCommandUpdatesEveryCardDespiteSynchronousDisplayRebuild() => ui.RunAsync(async () => {
        var mods = new[] { Mod("First"), Mod("Second") };
        var registry = new Mock<IModRegistry>();
        registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult([], mods));
        var manager = new ModManager(registry.Object, Mock.Of<IPakDir>(), mods.Select(mod => ReleaseCoordinates.FromRelease(mod.Releases.Last())), mods);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage("Update Mods?", It.IsAny<string>(), "Yes", "No", null, It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Callback<string, string, string, string, string?, IEnumerable<DependencyUpdate>>((_, _, _, _, _, updates) => Assert.Equal(2, updates.Count()))
            .Returns(UserDialogueChoice.Yes);
        dialogue.Setup(d => d.DisplayMessage("Mods updated successfully"));
        var view = new ModListVM(manager, dialogue.Object);
        var rebuilds = 0;
        view.DisplayMods.CollectionChanged += (_, args) => {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) rebuilds++;
        };

        await view.UpdateModsCommand.ExecuteAsync(null);

        Assert.True(rebuilds >= 2);
        Assert.Equal(2, view.DisplayMods.Count);
        Assert.All(view.DisplayMods, card => Assert.Equal("v2.0.0", card.EnabledVersion));
        Assert.All(manager.EnabledModReleaseCoordinates, release => Assert.Equal("v2.0.0", release.Version));
        dialogue.Verify(d => d.DisplayMessage("Mods updated successfully"), Times.Once);
        dialogue.VerifyAll();
    });

    [Fact]
    public Task UpdateCommandMaterializesFailuresWithoutRetryingUpdates() => ui.RunAsync(async () => {
        var mod = Mod("Failed");
        var manager = new Mock<IModManager>(MockBehavior.Strict);
        manager.SetupGet(m => m.Mods).Returns([mod]);
        manager.SetupGet(m => m.EnabledModReleaseCoordinates).Returns([ReleaseCoordinates.FromRelease(mod.Releases.Last())]);
        manager.Setup(m => m.UpdateModsList()).ReturnsAsync(new GetAllModsResult([], [mod]));
        manager.Setup(m => m.EnableModRelease(ReleaseCoordinates.FromRelease(mod.Releases.First()))).Returns(false);
        var dialogue = new Mock<IUserDialogueSpawner>(MockBehavior.Strict);
        dialogue.Setup(d => d.DisplayUpdateMessage("Update Mods?", It.IsAny<string>(), "Yes", "No", null, It.IsAny<IEnumerable<DependencyUpdate>>()))
            .Returns(UserDialogueChoice.Yes);
        dialogue.Setup(d => d.DisplayMessage("Failed to enable mods: Failed\n\nCheck the logs for more details."));
        dialogue.Setup(d => d.DisplayMessage("Mods updated successfully"));
        var view = new ModListVM(manager.Object, dialogue.Object);

        await view.UpdateModsCommand.ExecuteAsync(null);

        manager.Verify(m => m.EnableModRelease(It.IsAny<ReleaseCoordinates>()), Times.Once);
        dialogue.VerifyAll();
    });
}