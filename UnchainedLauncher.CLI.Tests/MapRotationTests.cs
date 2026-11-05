using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class MapRotationTests : IDisposable {
    private readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "map-rotation-" + Guid.NewGuid().ToString("N"));
    private const string Section = "/Script/TBL.TBLGameMode";

    public MapRotationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private CliCommandOptions Parse(string fixture, params (string Token, string Value)[] replacements) {
        var path = YamlTestResources.Write(_root, "map-rotation/" + fixture, replacements);
        return CommandLineOptions.Parse(["server", "--config-path", path]);
    }

    private CliCommandOptions ParseFixture(string testCase, string file = "server.yaml") => CommandLineOptions.Parse(["server", "--config-path",
        Path.Combine(AppContext.BaseDirectory, "test-resources", "settings-resolution", testCase, file)]);

    [Fact]
    public void ProfilesAreSharedButServerRotationsAndStartingMapsAreIndependent() {
        var group = Assert.IsType<ServerGroupOptions>(ParseFixture("legacy-multi-server"));
        var first = group.Servers[0].Options;
        var second = group.Servers[1].Options;
        Assert.Equal(1, first.Settings.Rotation!.StartIndex);
        Assert.Equal(0, second.Settings.Rotation!.StartIndex);
        Assert.Equal("FFA_Wardenglade", first.ToServerLaunchOptions().Map);
        Assert.Equal("FFA_Courtyard", second.ToServerLaunchOptions().Map);
        Assert.Equal(["FFA_Courtyard", "FFA_Wardenglade", "FFA_Courtyard"], first.Settings.Rotation.Maps);
        Assert.Equal(["Courtyard", "FFA Wardenglade", "Courtyard reprise"], first.Settings.Rotation.Names);
        Assert.Equal(["FFA_Courtyard", "FFA_Bazaar"], second.Settings.Rotation.Maps);
    }

    [Fact]
    public void OmittedRotationUsesTheDefaultStartupMap() {
        var path = Path.Combine(AppContext.BaseDirectory, "test-resources", "server-configuration", "startup-defaults", "input", "server.yaml");
        var options = YamlTestResources.Server(CommandLineOptions.Parse(["server", "--config-path", path]));
        Assert.Null(options.Settings.Rotation);
        Assert.Equal("FFA_Courtyard", options.ToServerLaunchOptions().Map);
    }

    [Theory]
    [InlineData("server.yaml", "1", 1)]
    [InlineData("server.yaml", "-1", 0)]
    [InlineData("server.yaml", null, 0)]
    [InlineData("explicit.yaml", "1", 0)]
    [InlineData("explicit.yaml", "invalid", 0)]
    public void ExistingRotationIndexIsReadBeforeSelectingTheStartupMap(string file, string? savedIndex, int expected) {
        var options = YamlTestResources.Server(ParseFixture("startup-index", file));
        File.WriteAllText(Path.Combine(_root, "Game.ini"), $"[{Section}]\n" + (savedIndex is null ? "" : $"MapListIndex={savedIndex}\n"));
        ServerLauncher.ReadSavedRotationIndex(options, _root);
        Assert.Equal(expected, options.Settings.Rotation!.StartIndex);
        Assert.Equal(expected == 0 ? "FFA_Courtyard" : "FFA_Wardenglade", options.ToServerLaunchOptions().Map);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-2")]
    [InlineData("invalid")]
    public void InvalidSavedIndicesRequireAnExplicitStartingMap(string index) {
        var options = YamlTestResources.Server(ParseFixture("startup-index"));
        var path = Path.Combine(_root, "Game.ini");
        var original = $"[{Section}]\nMapListIndex={index}\n";
        File.WriteAllText(path, original);
        Assert.Contains("starting-map", Assert.Throws<ArgumentException>(() => ServerLauncher.InitializeServerProfile(options, _ => _root)).Message);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null-slot")]
    [InlineData("tilde-slot")]
    [InlineData("list-slot")]
    [InlineData("empty-slot")]
    [InlineData("null-map")]
    [InlineData("empty-map")]
    [InlineData("query-map")]
    [InlineData("spaces-map")]
    [InlineData("outside-map")]
    [InlineData("unknown-slot-key")]
    [InlineData("legacy-slot-actors")]
    [InlineData("empty-profile")]
    [InlineData("null-profile")]
    [InlineData("blank-profile-key")]
    [InlineData("duplicate-profile")]
    [InlineData("legacy-profile-actors")]
    [InlineData("scalar-profile")]
    [InlineData("missing-start")]
    public void InvalidRotationsAndProfilesFailBeforePreparation(string fixture) {
        Assert.Throws<ArgumentException>(() => Parse(fixture));
        Assert.False(File.Exists(Path.Combine(_root, "Game.ini")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("FFA_Courtyard")]
    [InlineData("COURTYARD")]
    public void MissingReferencesExplainHowToFixThem(string reference) {
        var error = Assert.Throws<ArgumentException>(() => Parse("missing-reference", ("REFERENCE", reference)));
        Assert.Contains("Unknown map-profile", error.Message);
        Assert.Contains("map-profiles", error.Message);
        Assert.Contains("inline map-name", error.Message);
    }

    [Theory]
    [InlineData("map-list")]
    [InlineData("null-map-list")]
    [InlineData("typed-map-list")]
    [InlineData("raw-map-list")]
    [InlineData("append-map-list")]
    [InlineData("clear-map-list")]
    public void IniMapListAlwaysDirectsUsersToRotation(string fixture) {
        var error = Assert.Throws<ArgumentException>(() => Parse(fixture));
        Assert.Contains("MapList", error.Message);
        Assert.Contains("map-rotation", error.Message);
    }

    [Fact]
    public void RotationIndexCannotBeOverriddenByIni() {
        var error = Assert.Throws<ArgumentException>(() => Parse("map-list-index"));
        Assert.Contains("starting-map", error.Message);
    }

    [Theory]
    [InlineData("foo-bar", "Foo bar")]
    [InlineData("FOO_BAR", "Foo bar")]
    public void MissingProfileNamesBecomeReadableLabels(string key, string expected) {
        var options = YamlTestResources.Server(Parse("readable-name", ("KEY", key)));
        Assert.Equal(expected, Assert.Single(options.Settings.Rotation!.Names));
    }

    [Fact]
    public void GeneratedNamesPreserveModesAndMapWords() {
        var options = YamlTestResources.Server(ParseFixture("legacy-generated-labels"));
        Assert.Equal(["FFA Courtyard", "Night Wardenglade", "TO Galencourt", "TO Tournament Grounds"], options.Settings.Rotation!.Names);
    }

    [Fact]
    public void ExplicitLabelsAreIndependentOfMapAndDirectoryIdentity() {
        var options = YamlTestResources.Server(ParseFixture("legacy-explicit-labels"));
        Assert.Equal(["Wardenglade / GiantSlayers", "Later FFA"], options.Settings.Rotation!.Names);
        Assert.Equal(["FFA_Wardenglade", "FFA_Wardenglade"], options.Settings.Rotation.Maps);
        Assert.Equal(["wardenglade-giantslayers", "inline-1"], options.Settings.Rotation.Profiles);
    }

    [Theory]
    [InlineData("repeated-reference")]
    [InlineData("repeated-map")]
    [InlineData("case-folded-names")]
    [InlineData("defaulted-names")]
    public void RotationNamesMustBeUniqueAfterDefaultingAndCaseFolding(string fixture) {
        Assert.Contains("names must be unique", Assert.Throws<ArgumentException>(() => Parse(fixture)).Message);
        Assert.False(File.Exists(Path.Combine(_root, "Game.ini")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" leading")]
    [InlineData("line\nbreak")]
    public void ProfileNamesMustBeNonblankLabels(string name) {
        var quoted = System.Text.Json.JsonSerializer.Serialize(name);
        var error = Assert.Throws<ArgumentException>(() => Parse("profile-label", ("NAME", quoted)));
        Assert.Contains("profile name", error.Message);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    [InlineData("bad:profile")]
    public void ProfileReferencesMustBePortableFolders(string profile) {
        var quoted = System.Text.Json.JsonSerializer.Serialize(profile);
        var error = Assert.Throws<ArgumentException>(() => Parse("portable-reference", ("PROFILE", quoted)));
        Assert.Contains("profile directory", error.Message);
    }

    [Fact]
    public void DirectoryReferencesCannotCollideAcrossCaseOrInlineEntries() {
        foreach (var fixture in new[] { "case-folded-directories", "inline-directory" })
            Assert.Contains("directory references must be unique", Assert.Throws<ArgumentException>(() => Parse(fixture)).Message);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("mod-query")]
    [InlineData("dll-query")]
    public void InactiveSchemasSkipMapProfilesAndRotation(string command) {
        var path = YamlTestResources.Write(_root, "map-rotation/inactive");
        var options = CommandLineOptions.Parse([command, .. command == "client" ? Array.Empty<string>() : ["list"], "--config-path", path]);
        Assert.NotNull(options);
    }
}
