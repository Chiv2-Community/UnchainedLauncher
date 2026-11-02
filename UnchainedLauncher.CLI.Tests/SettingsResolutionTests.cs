using UnchainedLauncher.Core.Services.Mods;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class SettingsResolutionTests : IDisposable {
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "settings-resolution-" + Guid.NewGuid().ToString("N"));
    private string Write(string name, string text) {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    private string Fixture(string testCase, string file = "server.yaml") {
        var source = Path.Combine(AppContext.BaseDirectory, "test-resources", "settings-resolution", testCase);
        foreach (var input in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
            var target = Path.Combine(_root, Path.GetRelativePath(source, input));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(input, target, overwrite: true);
        }
        return Path.Combine(_root, file);
    }
    private CliCommandOptions Parse(string testCase, string file = "server.yaml") => YamlConfiguration.Load(Fixture(testCase, file), client: false);
    private ServerOptions LoadServer(string path) {
        var server = Assert.IsType<ServerOptions>(YamlConfiguration.Load(path, client: false));
        server.Shared.Assets = new ProfileAssetPaths("Test", _root);
        return server;
    }
    private ServerOptions Server(string testCase) => LoadServer(Fixture(testCase));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public void CombinesAndDeduplicatesAlwaysOnAndSlotActorsWithoutEnablingSlotActorsGlobally() {
        var server = Server("actor-union");
        Assert.Equal(new[] { "global", "shared", "server" }, server.Settings.ModActors);
        var rotation = server.Settings.Rotation!;
        Assert.Equal(new[] { "global", "shared", "server", "server-map", "preset" }, rotation.Actors![0]);
        Assert.Equal(new[] { "global", "shared", "server", "preset", "inline" }, rotation.Actors[1]);
        Assert.DoesNotContain("preset", rotation.Actors[2]);
    }

    [Fact]
    public async Task MoreSpecificRulesWinRegardlessOfDeclarationOrderAgainstTheFinalMapLeaf() {
        var server = Server("overlapping-rules");
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        NativeIniTestLayers.Compose(server, 0).Apply(Path.Combine(_root, "resolved"));
        Assert.Contains("Value=2", File.ReadAllText(Path.Combine(_root, "resolved", "Engine.ini")));
    }

    [Theory]
    [InlineData(0, "FFA_Courtyard", "MapValue=30", "PresetValue=40")]
    [InlineData(1, "FFA_Bazaar", "MapValue=20", "PresetValue=20")]
    [InlineData(2, "AA", "MapValue=60", "PresetValue=1")]
    public async Task RuleSpecificityPrecedesScopeAndEqualSpecificityRetainsScopeAndDeclarationOrder(int slot, string map, string mapValue, string presetValue) {
        var server = Server("specificity-rules");
        var rotation = server.Settings.Rotation!;
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        Assert.Equal(map, rotation.Maps[slot]);
        var output = Path.Combine(_root, "resolved");
        NativeIniTestLayers.Compose(server, slot).Apply(output);
        var ini = File.ReadAllText(Path.Combine(output, "Engine.ini"));
        Assert.Contains(mapValue, ini);
        Assert.Contains(presetValue, ini);
        Assert.Equal(rotation.Profiles.Count, rotation.Profiles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ServerPresetsReplaceGlobalDefinitionsWithoutMutatingTheGlobalInput() {
        var config = YamlConfiguration.LoadDocument<ServerConfiguration>(Fixture("preset-isolation"));
        var original = config.GlobalSettings!.MatchPresets!["foo"];
        var one = SettingsResolution.Resolve(config.GlobalSettings, config.Servers!["one"]!);
        var two = SettingsResolution.Resolve(config.GlobalSettings, config.Servers["two"]!);
        Assert.Equal("TDM_Wardenglade", one.Rotation!.Maps[0]);
        Assert.Equal("FFA_Courtyard", two.Rotation!.Maps[0]);
        Assert.Equal(new[] { "local-preset" }, one.Rotation.Actors![0]);
        Assert.Equal(new[] { "global-preset" }, two.Rotation.Actors![0]);
        Assert.Same(original, config.GlobalSettings.MatchPresets["foo"]);
        Assert.Null(config.Servers["one"]!.Resolved);
    }

    [Theory]
    [InlineData(0, "FFA_Courtyard", 16, 8, 6, 180, 35)]
    [InlineData(1, "FFA_Wardenglade", 12, 8, 9, 99, 25)]
    [InlineData(2, "FFA_Courtyard", 16, 2, 6, 180, 15)]
    [InlineData(3, "TDM_Wardenglade", 24, 2, 2, 180, 15)]
    [InlineData(4, "FFA_Courtyard", 16, 8, 6, 180, 25)]
    [InlineData(5, "FFA_Bazaar", 20, 2, 4, 180, 15)]
    public async Task InlineMapSelectsMapRulesWithoutDiscardingPresetIni(int slot, string map, int maxPlayers, int lowBots, int highBots, int spectate, int wait) {
        var server = Server("inline-map-preset");
        var rotation = server.Settings.Rotation!;
        Assert.Equal(map, rotation.Maps[slot]);
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        var directory = Path.Combine(_root, "composed");
        NativeIniTestLayers.Compose(server, slot).Apply(directory);
        var path = Path.Combine(directory, "Game.ini");
        var lines = File.ReadAllLines(path);
        Assert.Contains($"MaxPlayers={maxPlayers}", lines);
        Assert.Contains($"BotBackfillLowBots={lowBots}", lines);
        Assert.Contains($"BotBackfillHighBots={highBots}", lines);
        Assert.Contains($"IdleKickTimerSpectate={spectate}", lines);
        Assert.Contains($"MaxTimeBeforeStartingMatch={wait}", lines);
    }

    [Fact]
    public async Task NativeHierarchyPreservesWildcardMapPresetAndInlineBoundaries() {
        var server = Server("inline-map-preset");
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        var root = Path.Combine(server.MapProfilePath!, "ini-overrides");
        Assert.Equal(new[] {
            "indices/0/Game.ini", "maps/FFA_Courtyard/Game.ini", "maps/FFA_Wardenglade/Game.ini", "maps/FFA_~/Game.ini",
            "profiles/wardenglade.slot-1/Game.ini", "profiles/wardenglade.slot-4/Game.ini", "profiles/wardenglade/Game.ini", "unchained.yaml"
        }.OrderBy(path => path, StringComparer.Ordinal), Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal));
        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative)).Replace("\r\n", "\n");
        var wildcard = Read("maps/FFA_~/Game.ini");
        var courtyard = Read("maps/FFA_Courtyard/Game.ini");
        var preset = Read("profiles/wardenglade/Game.ini");
        Assert.Contains("MaxPlayers=20", wildcard);
        Assert.Contains("BotBackfillHighBots=4", wildcard);
        Assert.Contains("MaxPlayers=16", courtyard);
        Assert.Contains("BotBackfillHighBots=6", courtyard);
        Assert.DoesNotContain("BotBackfillLowBots", courtyard);
        Assert.DoesNotContain("MaxPlayers", preset);
        Assert.Contains("BotBackfillLowBots=8", preset);
        Assert.Contains("MaxTimeBeforeStartingMatch=25", preset);
        Assert.Equal(preset, Read("profiles/wardenglade.slot-4/Game.ini"));
        Assert.Equal("[/Script/TBL.TBLGameMode]\nMaxTimeBeforeStartingMatch=35\n", Read("indices/0/Game.ini"));
        foreach (var path in Directory.GetFiles(root, "*.ini", SearchOption.AllDirectories))
            Assert.DoesNotContain("IdleKickTimerSpectate=180", File.ReadAllText(path));
    }

    [Fact]
    public void PresetMatchingUsesThePluginsCaseInsensitiveNames() {
        var server = Server("preset-case");
        Assert.Equal("FFA_Courtyard", server.Settings.Rotation!.Maps[0]);
    }

    [Fact]
    public async Task RepeatedPresetAliasesCannotAddWildcardOrExactRuleMatches() {
        var server = Server("native-profile-aliases");
        Assert.Equal(new[] { "foo", "foo_slot-1" }, server.Settings.Rotation!.Profiles);
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        var directory = Path.Combine(_root, "composed");
        NativeIniTestLayers.Compose(server, 1).Apply(directory);
        Assert.Contains("Value=1", File.ReadAllLines(Path.Combine(directory, "Engine.ini")));
    }

    [Fact]
    public void PackagesFromAllUsedSlotsAreStagedWithoutEnablingTheirActorsGlobally() {
        var server = Server("used-slot-packages");
        Assert.Equal(5, server.Shared.Mods.Count);
        Assert.Equal(2, server.Shared.Dlls.Count);
        Assert.DoesNotContain(server.Shared.Mods, item => item.ModuleName == "unused");
        Assert.Empty(server.Settings.ModActors!);
    }

    [Theory]
    [InlineData("missing-preset", "Unknown match-preset")]
    [InlineData("empty-rotation", "at least one")]
    [InlineData("conflicting-mods", "Conflicting versions")]
    [InlineData("conflicting-plugins", "Conflicting versions")]
    [InlineData("duplicate-names", "names must be unique")]
    [InlineData("missing-baseline", "requires a server.ini default")]
    [InlineData("mixed-schemas", "instead of")]
    [InlineData("root-mods", "schema")]
    [InlineData("rules-without-rotation", "requires an explicit map-rotation")]
    [InlineData("star-map-pattern", "use ~ instead of *")]
    [InlineData("star-preset-pattern", "use ~ instead of *")]
    [InlineData("wildcard-rotation-reference", "must be concrete names")]
    [InlineData("wildcard-map-value", "requires a map containing")]
    [InlineData("question-map-pattern", "only one trailing ~")]
    [InlineData("middle-map-pattern", "only one trailing ~")]
    [InlineData("question-preset-pattern", "only one trailing ~")]
    [InlineData("multiple-map-wildcards", "only one trailing ~")]
    [InlineData("array-operation-scalar", "require an array type")]
    [InlineData("array-operation-raw", "cannot use raw")]
    [InlineData("array-operation-clear-values", "requires an empty list")]
    [InlineData("inferred-null", "cannot be null")]
    [InlineData("inferred-mixed-array", "Cannot infer a common INI array type")]
    [InlineData("inferred-structured-value", "supply an explicit type")]
    [InlineData("inferred-nested-array", "Nested INI arrays")]
    [InlineData("inferred-nonfinite", "finite")]
    [InlineData("array-operation-no-baseline", "ClearArray baseline")]
    public void RejectsInvalidResolvedConfigurationsBeforeLaunch(string testCase, string message) {
        Assert.Contains(message, Assert.Throws<ArgumentException>(() => Parse("invalid", testCase + ".yaml")).Message);
    }

    [Theory]
    [InlineData("missing-map", "entry 1")]
    [InlineData("unresolved-preset-map", "entry 2 (preset 'duel-night')")]
    [InlineData("blank-inline-map", "entry 1 (preset 'duel-night')")]
    public void UnresolvedMapsIdentifyTheSlotAfterAllMatchingConfigurations(string testCase, string location) {
        var error = Assert.Throws<ArgumentException>(() => Parse("invalid", testCase + ".yaml"));
        Assert.Contains(location, error.Message);
        Assert.Contains("requires a map after applying all matching configurations", error.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "Chivalry 2")));
    }

    [Fact]
    public void GlobalSettingsAloneApplyToTheDefaultServer() {
        var server = Server("global-only");
        Assert.Equal(new[] { "always" }, server.Settings.ModActors);
        Assert.Single(server.Shared.Mods);
        Assert.Equal("FFA_Courtyard", server.Settings.StartingMap);
        Assert.Null(server.Settings.Rotation);
    }

    [Fact]
    public async Task IniReferencesResolveAgainstIncludedFilesAndUseImmutableSnapshots() {
        var input = Fixture("immutable-snapshot");
        var server = LoadServer(input);
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        var output = Path.Combine(server.MapProfilePath!, "ini-overrides", "maps", "~", "Engine.ini");
        var original = File.ReadAllText(output);
        Assert.Contains("; retained comment", original);
        Write("parts/ini/Engine.ini", "[Mod]\nValue=99\n");
        Assert.Equal(original, File.ReadAllText(output));
        var changed = LoadServer(input);
        await MapProfileIni.PrepareAsync(changed, CancellationToken.None);
        Assert.NotEqual(server.MapProfilePath, changed.MapProfilePath);
        Assert.Contains("Value=99", File.ReadAllText(Path.Combine(changed.MapProfilePath!, "ini-overrides", "maps", "~", "Engine.ini")));
    }

    [Theory]
    [InlineData("missing-directory", "INI directory does not exist")]
    [InlineData("missing-file", "Cannot read INI file")]
    [InlineData("duplicate-categories", "schema")]
    public void InvalidIniReferencesFailBeforeSideEffects(string testCase, string message) {
        Assert.Contains(message, Assert.Throws<ArgumentException>(() => Parse("invalid", testCase + ".yaml")).Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "map-config")));
    }

    [Fact]
    public void FilePublicationFallsBackToCopiesAndNeverMutatesItsSource() {
        var source = Fixture("copy-fallback", "Engine.ini");
        var ini = IniOverrides.Parse(new IniSource { Files = { ["Engine"] = source } });
        var destination = Path.Combine(_root, "output");
        var cache = Path.Combine(_root, "cache");
        var attempted = false;
        ini.Apply(destination, cache, (_, _) => { attempted = true; return false; });
        Assert.True(attempted);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(destination, "Engine.ini")));
        File.WriteAllText(Path.Combine(destination, "Engine.ini"), "changed copy");
        Assert.Contains("Value=1", File.ReadAllText(source));
        Assert.Contains("Value=1", File.ReadAllText(Assert.Single(Directory.GetFiles(cache))));
    }

    [Fact]
    public void MergingIntoAnExistingLinkedOutputDetachesTheLinkBeforeWriting() {
        var source = Fixture("detach-link", "Engine.ini");
        var original = IniOverrides.Parse(new IniSource { Files = { ["Engine"] = source } });
        var destination = Path.Combine(_root, "output");
        var cache = Path.Combine(_root, "cache");
        original.Apply(destination, cache);
        var patch = IniOverrides.Parse(new Dictionary<string, Dictionary<string, Dictionary<string, object?>>> {
            ["Engine"] = new() { ["Mod"] = new() { ["Value"] = new Dictionary<string, object?> { ["type"] = "Int", ["value"] = 2 } } }
        });
        original.Merge(patch).Apply(destination, cache);
        Assert.Contains("Value=2", File.ReadAllText(Path.Combine(destination, "Engine.ini")));
        Assert.Contains("Value=1", File.ReadAllText(source));
        Assert.Contains("Value=1", File.ReadAllText(Assert.Single(Directory.GetFiles(cache))));
    }

    [Fact]
    public void ImportedArraysRequireAnExplicitRestorationBaseline() {
        var error = Assert.Throws<ArgumentException>(() => Server("imported-array-baseline"));
        Assert.Contains("ClearArray baseline", error.Message);
    }

    [Fact]
    public void InstallationProtonAndRegistryResourcesUseTheirContainingFile() {
        var server = Server("resource-origins");
        var config = YamlConfiguration.LoadDocument<ServerConfiguration>(Path.Combine(_root, "direct.yaml"));
        Assert.Equal(Path.Combine(_root, "parts", "installation"), config.Chiv2Path);
        Assert.Equal(Path.Combine(_root, "parts", "proton"), server.Shared.ProtonConfiguration.Path);
        Assert.Equal(Path.Combine(_root, "parts", "registry"), server.Shared.Registries[0].Location);
    }

    [Theory]
    [InlineData("scalar.yaml")]
    [InlineData("categories.yaml")]
    public void IncludedIniPathScalarsRetainTheirOwnOrigin(string file) {
        var server = LoadServer(Fixture("ini-path-origins", file));
        server.Ini.Apply(Path.Combine(_root, "resolved"));
        Assert.Contains("Value=1", File.ReadAllText(Path.Combine(_root, "resolved", "Engine.ini")));
    }

    [Fact]
    public void RegistryQueriesDoNotReadServerGlobalSettingsIncludes() {
        var query = YamlConfiguration.LoadDocument<RegistryQueryConfiguration>(Fixture("inactive-query-settings"));
        Assert.Empty(query.ModSetup!.Registries!);
    }

    [Fact]
    public void RootRotationPreservesAllSixSlotsWithoutArrayOperators() {
        var server = Server("inline-map-preset");
        var directory = Path.Combine(_root, "profile");
        ServerLauncher.InitializeServerProfile(server, _ => directory);
        var file = Path.Combine(directory, "Game.ini");
        var text = File.ReadAllText(file);
        var entries = new StructuredINI.StructuredINIParser().ReadEntries(text);
        Assert.Equal(new[] { "FFA_Courtyard", "FFA_Wardenglade", "FFA_Courtyard", "TDM_Wardenglade", "FFA_Courtyard", "FFA_Bazaar" },
            entries.Where(entry => entry.Name == "MapList").Select(entry => entry.Value));
        Assert.All(entries, entry => Assert.DoesNotContain(entry.Key[0], "!+.-"));
        ServerLauncher.InitializeServerProfile(server, _ => directory);
        Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void AlwaysOnImportedFilesArePublishedInTheServerProfileWithoutChangingSources() {
        var server = Server("always-on-import");
        var source = Path.Combine(_root, "Engine.ini");
        var destination = Path.Combine(_root, "profile");
        ServerLauncher.InitializeServerProfile(server, _ => destination);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(destination, "Engine.ini")));
        Assert.Contains("FirstLoadCompleted=True", File.ReadAllText(Path.Combine(destination, "Game.ini")));
        Write("Engine.ini", "[Mod]\nValue=99\n");
        Assert.Contains("Value=1", File.ReadAllText(Path.Combine(destination, "Engine.ini")));
    }

    [Theory]
    [InlineData("Engine", false)]
    [InlineData("Engine", true)]
    [InlineData("GameUserSettings", false)]
    [InlineData("GameUserSettings", true)]
    public void SavingImportedRootFilesPreservesSnapshotsAndIndependentOutputs(string category, bool existingLinkedRoot) {
        var server = Server("root-save-isolation");
        var source = Path.Combine(_root, "Engine.ini");
        var sourceBytes = File.ReadAllBytes(source);
        var cache = Path.Combine(server.InstancePaths.Root, "ini-cache");
        var conditional = Path.Combine(_root, "conditional");
        server.Ini.Apply(conditional, cache);
        var conditionalBytes = File.ReadAllBytes(Path.Combine(conditional, category + ".ini"));
        var first = Path.Combine(_root, "first-profile");
        var second = Path.Combine(_root, "second-profile");
        // Simulate a root file linked by an earlier launcher version.
        if (existingLinkedRoot) server.Ini.Apply(first, cache);
        ServerLauncher.InitializeServerProfile(server, _ => first);
        ServerLauncher.InitializeServerProfile(server, _ => second);
        var rootFile = Path.Combine(first, category + ".ini");
        var rootBytes = File.ReadAllBytes(rootFile);
        var snapshots = Directory.GetFiles(cache).ToDictionary(path => path, File.ReadAllBytes);

        var writer = new StructuredINI.StructuredINIWriter();
        writer.MergeSection("Mod", new[] { KeyValuePair.Create("Value", "99") });
        Assert.True(writer.WriteOut(rootFile, isConfigRoot: true));
        Assert.Contains("Value=99", File.ReadAllText(rootFile));
        var error = Record.Exception(() => ServerLauncher.InitializeServerProfile(server, _ => first));

        Assert.Null(error);
        Assert.Equal(rootBytes, File.ReadAllBytes(rootFile));
        Assert.Equal(rootBytes, File.ReadAllBytes(Path.Combine(second, category + ".ini")));
        Assert.Equal(conditionalBytes, File.ReadAllBytes(Path.Combine(conditional, category + ".ini")));
        Assert.Equal(sourceBytes, File.ReadAllBytes(source));
        Assert.All(snapshots, snapshot => Assert.Equal(snapshot.Value, File.ReadAllBytes(snapshot.Key)));
    }

    [Fact]
    public void EffectiveProfileNamesAreStableAcrossReorderingAndDistinctDisplayLabels() {
        var first = LoadServer(Fixture("profile-identity", "first.yaml"));
        var second = LoadServer(Path.Combine(_root, "second.yaml"));
        Assert.Equal(first.Settings.Rotation!.Profiles[0], second.Settings.Rotation!.Profiles[1]);
        Assert.NotEqual(first.Settings.Rotation.Profiles[0], first.Settings.Rotation.Profiles[2]);
        Assert.DoesNotContain("map-indices", first.Settings.Rotation.Profiles[0]);
    }
}
