using System.Globalization;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class IniOverrideTests : IDisposable {
    private readonly string _directory = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "ini-overrides-" + Guid.NewGuid().ToString("N"));
    private const string Section = "/Script/TBL.TBLGameMode";

    public IniOverrideTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private ServerOptions Parse(string fixture, params (string Token, string Value)[] replacements) {
        var path = YamlTestResources.Write(_directory, "ini-overrides/" + fixture, replacements);
        return YamlTestResources.Server(CommandLineOptions.Parse(["server", "--config-path", path]));
    }

    [Fact]
    public void RawIsNotParsedOrWrittenToProfile() {
        var options = Parse("raw");
        Assert.Equal(new UEINIParameter("Game", Section, "Unknown", "  SomeStructuredThing(foo, bar, 10, baz, false)  ").Rendered,
            Assert.Single(options.Ini.RawArguments));
        Assert.Contains(Assert.Single(options.Ini.RawArguments), options.ToLaunchOptions().LaunchArgs);
        options.Ini.Apply(_directory);
        Assert.False(File.Exists(Path.Combine(_directory, "Game.ini")));
    }

    [Theory]
    [InlineData("String", "example", "example")]
    [InlineData("Boolean", "true", "True")]
    [InlineData("Bool", "false", "False")]
    [InlineData("Int32", "-12", "-12")]
    [InlineData("Int", "123", "123")]
    [InlineData("Single", "1.5", "1.5")]
    [InlineData("Float", "1.25", "1.25")]
    [InlineData("Double", "2.5", "2.5")]
    [InlineData("Decimal", "0.25", "0.25")]
    [InlineData("CharacterClass", "poleman", "Poleman")]
    public void ExplicitPrimitiveAndEnumTypesUseMappedCodecs(string type, string value, string expected) {
        Parse("explicit-type", ("TYPE", type), ("VALUE", value)).Ini.Apply(_directory);
        Assert.Contains("Unknown=" + expected, File.ReadAllText(Path.Combine(_directory, "Game.ini")));
    }

    [Theory]
    [InlineData("invalid-raw", "raw requires a string")]
    [InlineData("unsupported-type", "Unsupported type")]
    [InlineData("missing-value", "supply value")]
    [InlineData("extra-wrapper-field", "Use only type and value")]
    [InlineData("invalid-boolean", "Boolean")]
    [InlineData("wrong-known-type", "requires type Boolean")]
    [InlineData("fractional-integer", "Int32")]
    [InlineData("array-scalar", "require a list")]
    [InlineData("null-element", "cannot be null")]
    [InlineData("invalid-enum", "CharacterClass")]
    [InlineData("missing-record-member", "Missing ClassLimit.ClassLimitPercent")]
    [InlineData("extra-record-member", "Invalid or duplicate member")]
    [InlineData("short-record", "requires a member mapping")]
    [InlineData("line-break", "line breaks")]
    [InlineData("nonfinite", "finite")]
    [InlineData("duplicate-field", "Duplicate INI field")]
    public void InvalidValuesFailBeforeFilesAreWritten(string fixture, string error) {
        var exception = Assert.Throws<ArgumentException>(() => Parse(fixture));
        Assert.Contains(error, exception.Message);
        Assert.False(File.Exists(Path.Combine(_directory, "Game.ini")));
    }

    [Fact]
    public void InvariantNumbersAndNoSecretValuesInErrors() {
        var previous = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var options = Parse("invariant-record");
            options.Ini.Apply(_directory);
            Assert.Contains("ClassLimitPercent=0.25", File.ReadAllText(Path.Combine(_directory, "Game.ini")));
            var error = Assert.Throws<ArgumentException>(() => Parse("secret-boolean"));
            Assert.DoesNotContain("secret-password", error.Message);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("server-name")]
    [InlineData("raw-server-name")]
    public void ExplicitServerNameIsNotShadowedByGeneratedName(string fixture) {
        var options = Parse(fixture);
        Assert.DoesNotContain(options.ToServerLaunchOptions().ToCLIArgs(), arg => arg is UEINIParameter { Key: "ServerName" });
    }

    [Fact]
    public void ExplicitStartupSettingsSuppressGeneratedDefaults() {
        var path = Path.Combine(AppContext.BaseDirectory, "test-resources", "server-configuration", "typed-values", "input", "server.yaml");
        var options = YamlTestResources.Server(YamlConfiguration.Load(path, client: false));
        Assert.DoesNotContain("bSavedHasAgreedToTOS=True", options.ToLaunchOptions().LaunchArgs);
    }
}
