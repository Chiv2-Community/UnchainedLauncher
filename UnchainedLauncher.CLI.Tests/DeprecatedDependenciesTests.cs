using System.Text.Json;
using UnchainedLauncher.Core.Services.Dlls;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class DeprecatedDependenciesTests {
    [Theory]
    [InlineData("", "Legacy")]
    [InlineData(",\"mod_dependencies\":[]", null)]
    [InlineData(",\"mod_dependencies\":[{\"repo_url\":\"https://github.com/Org/Modern\",\"version\":\"1.0.0\"}]", "Modern")]
    public void DeprecatedDependenciesAreOnlyAFallback(string modern, string? expected) {
        const string legacy = "\"dependencies\":[{\"repo_url\":\"https://github.com/Org/Legacy\",\"version\":\"1.0.0\"}]";
        foreach (var fields in new[] { legacy + modern, modern.TrimStart(',') + (modern.Length == 0 ? "" : ",") + legacy }) {
            var info = JsonSerializer.Deserialize<PluginInfo>("{\"name\":\"Plugin\",\"description\":\"Test\",\"authors\":[\"Author\"]," + fields + "}")!;
            info.Validate();
            var roundTrip = JsonSerializer.Deserialize<PluginInfo>(JsonSerializer.Serialize(info))!;
            Assert.Equal(info.RequiredMods, roundTrip.RequiredMods);
            Assert.Empty(info.RequiredPlugins);
            if (expected == null) Assert.Empty(info.RequiredMods);
            else Assert.Equal("https://github.com/Org/" + expected, Assert.Single(info.RequiredMods).RepoUrl);
        }
    }

    [Fact]
    public void ModernListIgnoresInvalidLegacyDependencyValues() {
        var info = JsonSerializer.Deserialize<PluginInfo>("""
            {"name":"Plugin","description":"Test","authors":["Author"],
             "dependencies":[{"repo_url":"not a repository","version":"invalid"}],
             "mod_dependencies":[]}
            """)!;
        info.Validate();
        Assert.Empty(info.RequiredMods);
    }

    [Fact]
    public void PackageComparisonUsesEffectiveDependencies() {
        const string legacy = """
            {"name":"Plugin","description":"Test","authors":["Author"],
             "dependencies":[{"repo_url":"https://github.com/Org/Mod","version":"1.0.0"}]}
            """;
        var modern = legacy.Replace("\"dependencies\"", "\"mod_dependencies\"");
        var package = $$"""
            {"latest_release_info":{{legacy}},"releases":[{
              "tag":"v1.0.0","hash":"{{new string('a', 64)}}","dll_file_name":"Plugin.dll",
              "download_url":"https://example.test/Plugin.dll","size":1,
              "release_date":"2026-10-26T00:00:00Z","info":{{modern}},"release_notes_markdown":null}]}
            """;
        var release = Assert.Single(DllManager.ParsePackage(package, "Org/Plugin"));
        Assert.Equal("https://github.com/Org/Mod", Assert.Single(release.Info!.RequiredMods).RepoUrl);
    }

    [Fact]
    public void LegacyDependenciesAreValidatedWhenUsed() {
        var info = JsonSerializer.Deserialize<PluginInfo>("""
            {"name":"Plugin","description":"Test","authors":["Author"],
             "dependencies":[{"repo_url":"not a repository","version":"invalid"}]}
            """)!;
        Assert.Throws<InvalidDataException>(info.Validate);
    }
}