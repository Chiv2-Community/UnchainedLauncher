using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class YamlIncludeTests : IDisposable {
    private readonly string _directory = Path.Combine(
        Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "yaml-include-" + Guid.NewGuid().ToString("N"));

    private string Write(string name, string fixture, params (string Token, string Value)[] replacements) {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, YamlTestResources.Read("includes/" + fixture, replacements));
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void LoadsSequencesScalarsAndRepeatedReferencesWithoutChangingTheirTypes() {
        Write("actors.yaml", "actors");
        Write("name.yaml", "name");
        var config = YamlConfiguration.LoadDocument<ServerConfiguration>(Write("launch.yaml", "repeated"));
        Assert.Equal("true", config.Servers!["one"]!.Name);
        Assert.Equal(new[] { "first", "second" }, config.Servers["one"]!.ModActors);
        Assert.Equal(new[] { "first", "second" }, config.Servers["two"]!.ModActors);
    }

    [Theory]
    [InlineData("cycle", "cycle")]
    [InlineData("missing", "missing.yaml")]
    [InlineData("remote", "local file")]
    [InlineData("mapping-load", "invalid !load")]
    [InlineData("unknown-tag", "Unsupported YAML tag")]
    [InlineData("qualified-tag", "Unsupported YAML tag")]
    [InlineData("load-anchor", "cannot declare an anchor")]
    [InlineData("multiple-documents", "exactly one document")]
    public void RejectsInvalidIncludes(string fixture, string message) {
        var path = Write("launch.yaml", fixture);
        Assert.Contains(message, Assert.Throws<ArgumentException>(() => YamlConfiguration.LoadDocument<ServerConfiguration>(path)).Message);
    }

    [Theory]
    [InlineData("duplicate-name")]
    [InlineData("unknown-setting")]
    [InlineData("unterminated")]
    public void IncludedErrorsIdentifyTheirSourceWithoutEchoingValues(string fixture) {
        var included = Write("included.yaml", fixture);
        var root = Write("launch.yaml", "included");
        var error = Assert.Throws<ArgumentException>(() => YamlConfiguration.LoadDocument<ServerConfiguration>(root));
        Assert.Contains(included, error.Message);
        Assert.DoesNotContain("secret-password", error.Message);
        Assert.Contains("line", error.Message);
    }

    [Fact]
    public void RejectsExcessiveNesting() {
        for (var index = 0; index < 35; index++) Write($"{index}.yaml", "nested", ("NEXT", (index + 1).ToString()));
        var error = Assert.Throws<ArgumentException>(() => YamlConfiguration.LoadDocument<ServerConfiguration>(Path.Combine(_directory, "0.yaml")));
        Assert.Contains("nesting exceeds", error.Message);
    }

    [Theory]
    [InlineData(false, "client")]
    [InlineData(true, "server")]
    [InlineData(true, "global-settings")]
    public void DoesNotReadIncludesInInactiveSettings(bool client, string inactive) {
        var path = Write("launch.yaml", "inactive", ("INACTIVE", inactive));
        if (client) Assert.Null(YamlConfiguration.LoadDocument<ClientConfiguration>(path).Server);
        else Assert.Null(YamlConfiguration.LoadDocument<ServerConfiguration>(path).Client);
    }

    [Fact]
    public void RejectsOversizedInputBeforeParsing() {
        var path = Write("launch.yaml", "oversized", ("PADDING", new string(' ', 16 * 1024 * 1024 + 1)));
        Assert.Contains("exceeds 16 MiB", Assert.Throws<ArgumentException>(() => YamlConfiguration.LoadDocument<ServerConfiguration>(path)).Message);
    }

    [Fact]
    public void RejectsExcessiveEventExpansion() {
        var path = Write("launch.yaml", "event-limit", ("ACTORS", string.Join(',', Enumerable.Repeat("actor", 100_001))));
        Assert.Contains("exceeds 100000 events", Assert.Throws<ArgumentException>(() => YamlConfiguration.LoadDocument<ServerConfiguration>(path)).Message);
    }

    [Fact]
    public void AnchorsAreLocalToEachIncludedDocument() {
        Write("one.yaml", "local-anchor", ("NAME", "First"));
        Write("two.yaml", "local-anchor", ("NAME", "Second"));
        var root = Write("launch.yaml", "local-anchors");
        var config = YamlConfiguration.LoadDocument<ServerConfiguration>(root);
        Assert.Equal("First", config.Servers!["one"]!.Description);
        Assert.Equal("Second", config.Servers["two"]!.Description);
        Assert.Equal("First", config.Servers["three"]!.Description);
    }

    [Fact]
    public void CannotReferenceAnAnchorFromAnotherFile() {
        Write("one.yaml", "anchor-definition");
        Write("two.yaml", "anchor-reference");
        var root = Write("launch.yaml", "cross-file-anchor");
        Assert.Throws<ArgumentException>(() => YamlConfiguration.LoadDocument<ServerConfiguration>(root));
    }
}
