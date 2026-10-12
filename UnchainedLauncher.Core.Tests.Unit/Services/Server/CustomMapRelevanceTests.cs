using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server;

public class CustomMapRelevanceTests {
    private static MapDto Map(string name, string? rules, string? mode, string? overrideRules = null) =>
        new($"TBL/Content/Mods/{name}.umap", "hash", null, "World", rules, name,
            null, null, null, mode, overrideRules);

    [Theory]
    [InlineData("BR_Island", "BP_BattleRoyale_C", "BR")]
    [InlineData("COOP_Arena", "BP_Horde_C", "COOP")]
    [InlineData("COOP_Arena", "BP_Horde_C", null)]
    public void ExplicitRulesClassAllowsCustomModes(string name, string rules, string? mode) =>
        Assert.True(Map(name, rules, mode).IsRelevant());

    [Theory]
    [InlineData("COOP_Arena_Lighting", "None", "COOP")]
    [InlineData("BR_Island_Geometry", "none", "BR")]
    [InlineData("TDM_Arena_Navigation", "", "TeamDeathMatch")]
    [InlineData("BR_Island_Art", "null", "BR")]
    public void ExplicitMissingRulesClassExcludesSupportLevels(string name, string rules, string mode) =>
        Assert.False(Map(name, rules, mode).IsRelevant());

    [Fact]
    public void ValidOverrideRulesClassRemainsPlayable() =>
        Assert.True(Map("COOP_Arena", "None", "COOP", "BP_Horde_C").IsRelevant());

    [Fact]
    public void LegacyManifestWithoutClassFieldsRetainsModeFallback() =>
        Assert.True(Map("FFA_Courtyard", null, "FFA").IsRelevant());

    [Fact]
    public void MissingMetadataDoesNotInferPlayabilityFromPrefix() =>
        Assert.False(Map("BR_Lighting", null, null).IsRelevant());
}
