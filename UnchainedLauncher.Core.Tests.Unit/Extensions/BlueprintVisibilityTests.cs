using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Extensions;

public class BlueprintVisibilityTests {
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void RequiresExplicitShowInGui(bool? showInGui, bool expected) {
        var actor = new BlueprintDto("/Game/Mods/Test.Test_C", "hash", null, null,
            "Test", "1.0.0", null, null, null, null, null, showInGui, null, null, null, null, null);

        Assert.Equal(expected, actor.IsRelevant());
        var manifest = new AssetCollections([], [actor], [], [], []);
        Assert.Equal(expected ? new[] { actor } : [], manifest.RelevantBlueprints());
    }
}
