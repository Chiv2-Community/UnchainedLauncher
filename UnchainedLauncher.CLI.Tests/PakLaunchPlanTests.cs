using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class PakLaunchPlanTests {
    private static Release Release(string name, string tag = "v1.0.0", params Dependency[] dependencies) =>
        new(tag, "hash", "pak", DateTime.UnixEpoch,
            new($"https://github.com/org/{name}", name, "", null, null, [], [], dependencies.ToList(), PluginDependencies: []), new([], [], [], [], []), null);

    private static Dependency Requires(string name, string range = "*") => new($"https://github.com/org/{name}", range);

    private static ModManager Manager(Release[] roots, params Release[] releases) =>
        new(new AggregateModRegistry(), new PakDir(".", []), roots.Select(ReleaseCoordinates.FromRelease),
            releases.GroupBy(ModIdentifier.FromRelease).Select(group => new Mod(group.First().Info, group.ToList())));

    [Fact]
    public void IntersectingRangesBacktrackAndKeepExplicitPinsAndExactEdges() {
        var leaf1 = Release("leaf");
        var leaf2 = Release("leaf", "v2.0.0");
        var left = Release("left", "v1.0.0", Requires("leaf", ">=1.0.0"));
        var right = Release("right", "v1.0.0", Requires("leaf", "<2.0.0"));
        var latest = Release("right", "v2.0.0", Requires("absent"));
        var manager = Manager([left, right], latest, leaf2, right, left, leaf1);
        var plan = PakLaunchPlan.Create(manager);
        Assert.Equal([leaf1, left, right], plan.Releases);
        Assert.Same(plan, PakLaunchPlan.Create(manager));
        Assert.Equal([ReleaseCoordinates.FromRelease(left), ReleaseCoordinates.FromRelease(right)], manager.EnabledModReleaseCoordinates);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("range")]
    [InlineData("pin")]
    [InlineData("cycle")]
    [InlineData("self")]
    public void InvalidGraphsFailWithoutPublishingAPartialSnapshot(string graph) {
        var leaf = Release("leaf", "v2.0.0", graph == "cycle" ? [Requires("root")] : []);
        var root = Release("root", "v1.0.0", Requires(graph == "absent" ? "absent" : graph == "self" ? "root" : "leaf",
            graph is "range" or "pin" ? "<2.0.0" : "*"));
        var manager = Manager(graph == "pin" ? [root, leaf] : [root], root, leaf);
        Assert.Throws<InvalidOperationException>(() => PakLaunchPlan.Create(manager));
        Assert.Empty(((PakDir)manager.PakDir).ManagedPaks);
        root.Info.Dependencies!.Clear();
        Assert.NotEmpty(PakLaunchPlan.Create(manager).Releases);
    }

    [Fact]
    public async Task SnapshotInvalidatesForSelectionRefreshAndInPlaceDependencyChanges() {
        var leaf = Release("leaf");
        var root = Release("root");
        var manager = Manager([root], root, leaf);
        var first = PakLaunchPlan.Create(manager);
        Assert.Same(first, PakLaunchPlan.Create(manager, roots: [root]));
        root.Info.Dependencies!.Add(Requires("leaf"));
        var changed = PakLaunchPlan.Create(manager);
        Assert.NotSame(first, changed);
        Assert.Equal([leaf, root], changed.Releases);
        manager.EnableModRelease(ReleaseCoordinates.FromRelease(leaf));
        var selected = PakLaunchPlan.Create(manager);
        Assert.NotSame(changed, selected);
        await manager.UpdateModsList();
        Assert.Throws<ArgumentException>(() => PakLaunchPlan.Create(manager));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependencyFirstOrderDoesNotDependOnMetadataOrRootEnumeration(bool reverse) {
        var leaf = Release("leaf");
        var a = Release("a", "v1.0.0", Requires("leaf"));
        var z = Release("z", "v1.0.0", Requires("leaf"));
        var manager = Manager(reverse ? [z, a] : [a, z], reverse ? [z, a, leaf] : [leaf, a, z]);
        Assert.Equal([leaf, a, z], PakLaunchPlan.Create(manager).Releases);
    }

    [Fact]
    public void ManualDependencyPolicyAllowsAbsentDevelopmentPakButValidatesSelectedVersionsAndCycles() {
        var root = Release("root", "v1.0.0", Requires("leaf", "<2.0.0"));
        var leaf = Release("leaf", "v2.0.0");
        var manager = Manager([root], root, leaf);
        Assert.Equal([root], PakLaunchPlan.Create(manager, false).Releases);
        manager.EnableModRelease(ReleaseCoordinates.FromRelease(leaf));
        Assert.Throws<InvalidOperationException>(() => PakLaunchPlan.Create(manager, false));
        root.Info.Dependencies!.Clear();
        root.Info.Dependencies.Add(Requires("root"));
        Assert.Throws<InvalidOperationException>(() => PakLaunchPlan.Create(manager, false));
    }

    [Fact]
    public void CancellationAlsoAppliesToCachedSnapshots() {
        var root = Release("root");
        var manager = Manager([root], root);
        PakLaunchPlan.Create(manager);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PakLaunchPlan.Create(manager, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void DependencyKindChangesCannotReuseAnOldClosure() {
        var leaf = Release("leaf");
        var root = Release("root", "v1.0.0", Requires("leaf"));
        var manager = Manager([root], root, leaf);
        var first = PakLaunchPlan.Create(manager);
        Assert.Equal([leaf, root], first.Releases);
        root.Info.Dependencies!.Clear();
        root.Info.PluginDependencies!.Add(Requires("leaf"));
        var second = PakLaunchPlan.Create(manager);
        Assert.NotSame(first, second);
        Assert.Equal([root], second.Releases);
    }

    [Fact]
    public void ReplacementReleaseWithSameCoordinatesCannotReuseOldMetadata() {
        var root = Release("root");
        var manager = Manager([root], root);
        var first = PakLaunchPlan.Create(manager);
        var replacement = root with { ReleaseHash = "new hash" };
        var second = PakLaunchPlan.Create(manager, roots: [replacement]);
        Assert.NotSame(first, second);
        Assert.Same(replacement, Assert.Single(second.Releases));
    }
}