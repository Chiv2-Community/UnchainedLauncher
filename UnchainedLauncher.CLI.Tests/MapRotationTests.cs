using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class MapRotationTests : IDisposable {
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "map-rotation-" + Guid.NewGuid().ToString("N"));
    private const string Section = "/Script/TBL.TBLGameMode";

    public MapRotationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private CliCommandOptions Parse(string yaml, string command = "server") {
        var path = Path.Combine(_root, "server.yaml");
        File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\n" + yaml);
        return CommandLineOptions.Parse([command, "--config-path", path]);
    }

    [Fact]
    public void MixedReferencesAndInlineEntriesPreserveOrderAndDuplicates() {
        var options = Assert.IsType<ServerOptions>(Parse("""
            server:
              map-rotation:
                - courtyard
                - map-name: FFA_Wardenglade
                - courtyard
            map-profiles:
              courtyard:
                map-name: FFA_Courtyard
            """));
        Assert.Equal(["FFA_Courtyard", "FFA_Wardenglade", "FFA_Courtyard"], options.Settings.Rotation!.Maps);
        Assert.Equal(0, options.Settings.Rotation.StartIndex);
        Assert.Equal("FFA_Courtyard", options.ToServerLaunchOptions().Map);
    }

    [Fact]
    public void ProfilesAreSharedButServerRotationsAndStartingMapsAreIndependent() {
        var group = Assert.IsType<ServerGroupOptions>(Parse("""
            servers:
              first:
                map-rotation: [courtyard, {map-name: FFA_Wardenglade}]
                starting-map: ffa_wardenglade
              second:
                game-port: 7778
                ping-port: 3076
                query-port: 7072
                rcon-port: 9002
                map-rotation: [courtyard, {map-name: FFA_Bazaar}]
            map-profiles:
              courtyard: {map-name: FFA_Courtyard}
            """));
        var first = group.Servers[0].Options;
        var second = group.Servers[1].Options;
        Assert.Equal(1, first.Settings.Rotation!.StartIndex);
        Assert.Equal(0, second.Settings.Rotation!.StartIndex);
        Assert.Equal("FFA_Wardenglade", first.ToServerLaunchOptions().Map);
        Assert.Equal("FFA_Courtyard", second.ToServerLaunchOptions().Map);
        foreach (var server in group.Servers) {
            var directory = Path.Combine(_root, server.Id);
            ServerLauncher.InitializeServerProfile(server.Options, _ => directory);
        }
        var firstIni = File.ReadAllText(Path.Combine(_root, "first", "Game.ini"));
        var secondIni = File.ReadAllText(Path.Combine(_root, "second", "Game.ini"));
        Assert.Contains(".MapList=FFA_Wardenglade", firstIni);
        Assert.DoesNotContain("FFA_Bazaar", firstIni);
        Assert.Contains("MapListIndex=1", firstIni);
        Assert.Contains(".MapList=FFA_Bazaar", secondIni);
        Assert.DoesNotContain("FFA_Wardenglade", secondIni);
        Assert.Contains("MapListIndex=0", secondIni);
    }

    [Fact]
    public void RotationPatchesOnlyMapListAndIndexAndDoesNotAccumulateOnRelaunch() {
        var options = Assert.IsType<ServerOptions>(Parse("""
            server:
              map-rotation:
                - map-name: FFA_Wardenglade
                - map-name: FFA_Wardenglade
                - map-name: /Game/Maps/Custom.Custom
            """));
        Assert.Equal("FFA_Wardenglade", options.ToServerLaunchOptions().Map);
        var path = Path.Combine(_root, "Game.ini");
        File.WriteAllText(path, $"; keep comment\n[{Section}]\n.MapList=Old\n+MapList=Other\nMapListIndex=9\nServerName=KeepName\nCustom=KeepValue\n[/Other]\nUntouched=yes\n");
        options.Settings.Rotation!.Apply(_root);
        var text = File.ReadAllText(path);
        Assert.Contains("; keep comment", text);
        Assert.Contains("ServerName=KeepName", text);
        Assert.Contains("Custom=KeepValue", text);
        Assert.Contains("Untouched=yes", text);
        Assert.DoesNotContain("MapList=Old", text);
        Assert.DoesNotContain("MapList=Other", text);
        Assert.Contains("!MapList=ClearArray", text);
        Assert.Equal(2, text.Split('\n').Count(line => line.TrimEnd('\r') == ".MapList=FFA_Wardenglade"));
        Assert.Contains(".MapList=/Game/Maps/Custom.Custom", text);
        Assert.Contains("MapListIndex=0", text);
        options.Settings.Rotation.Apply(_root);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void OmittedRotationPreservesExistingMapListAndDefaultStartup() {
        var options = Assert.IsType<ServerOptions>(Parse("server: {}"));
        Assert.Null(options.Settings.Rotation);
        Assert.Equal("FFA_Courtyard", options.ToServerLaunchOptions().Map);
        var path = Path.Combine(_root, "Game.ini");
        File.WriteAllText(path, $"[{Section}]\nMapList=KeepMap\nMapListIndex=7\n");
        ServerLauncher.InitializeServerProfile(options, _ => _root);
        var text = File.ReadAllText(path);
        Assert.Contains("MapList=KeepMap", text);
        Assert.Contains("MapListIndex=7", text);
    }

    [Theory]
    [InlineData("server: {map-rotation: []}")]
    [InlineData("server: {map-rotation: [null]}")]
    [InlineData("server: {map-rotation: [~]}")]
    [InlineData("server: {map-rotation: [[]]}")]
    [InlineData("server: {map-rotation: [{}]}")]
    [InlineData("server: {map-rotation: [{map-name: null}]}")]
    [InlineData("server: {map-rotation: [{map-name: ''}]}")]
    [InlineData("server: {map-rotation: [{map-name: 'FFA_Courtyard?mods=Turbo'}]}")]
    [InlineData("server: {map-rotation: [{map-name: 'map with spaces'}]}")]
    [InlineData("server: {map-rotation: [{map-name: '../outside'}]}")]
    [InlineData("server: {map-rotation: [{map-name: FFA_Courtyard, typo: true}]}")]
    [InlineData("server: {map-rotation: [{map-name: FFA_Courtyard, mod-actors: []}]}")]
    [InlineData("map-profiles: {unused: {}}")]
    [InlineData("map-profiles: {unused: null}")]
    [InlineData("map-profiles: {'': {map-name: FFA_Courtyard}}")]
    [InlineData("map-profiles: {one: {map-name: FFA_Courtyard}, one: {map-name: FFA_Bazaar}}")]
    [InlineData("map-profiles: {one: {map-name: FFA_Courtyard, mod-actors: []}}")]
    [InlineData("map-profiles: {one: other}")]
    [InlineData("server: {starting-map: FFA_Bazaar, map-rotation: [{map-name: FFA_Courtyard}]}")]
    public void InvalidRotationsAndProfilesFailBeforePreparation(string yaml) {
        Assert.Throws<ArgumentException>(() => Parse(yaml));
        Assert.False(File.Exists(Path.Combine(_root, "Game.ini")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("FFA_Courtyard")]
    [InlineData("COURTYARD")]
    public void MissingReferencesExplainHowToFixThem(string reference) {
        var error = Assert.Throws<ArgumentException>(() => Parse($"server: {{map-rotation: [{reference}]}}\nmap-profiles: {{courtyard: {{map-name: FFA_Courtyard}}}}"));
        Assert.Contains("Unknown map-profile", error.Message);
        Assert.Contains("map-profiles", error.Message);
        Assert.Contains("inline map-name", error.Message);
    }

    [Theory]
    [InlineData("MapList: [FFA_Courtyard]")]
    [InlineData("maplist: null")]
    [InlineData("MapList: {type: 'String[]', value: [FFA_Courtyard]}")]
    [InlineData("MapList: {type: raw, value: anything}")]
    [InlineData("'+MapList': anything")]
    [InlineData("'!MapList': anything")]
    public void IniMapListAlwaysDirectsUsersToRotation(string field) {
        var error = Assert.Throws<ArgumentException>(() => Parse($"server:\n  ini:\n    Game:\n      {Section}:\n        {field}"));
        Assert.Contains("MapList", error.Message);
        Assert.Contains("map-rotation", error.Message);
    }

    [Fact]
    public void RotationIndexCannotBeOverriddenByIni() {
        var error = Assert.Throws<ArgumentException>(() => Parse($"server:\n  map-rotation: [{{map-name: FFA_Courtyard}}]\n  ini:\n    Game:\n      {Section}:\n        MapListIndex: 1"));
        Assert.Contains("starting-map", error.Message);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("mod-query")]
    [InlineData("dll-query")]
    public void InactiveSchemasSkipMapProfilesAndRotation(string command) {
        var path = Path.Combine(_root, "inactive.yaml");
        File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\nmap-profiles: [invalid]\nservers: {one: {map-rotation: [missing]}}\n");
        var options = CommandLineOptions.Parse([command, .. command == "client" ? Array.Empty<string>() : ["list"], "--config-path", path]);
        Assert.NotNull(options);
    }
}