using System.Globalization;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class IniOverrideTests : IDisposable {
    private readonly string _directory = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "ini-overrides-" + Guid.NewGuid().ToString("N"));
    private const string Section = "/Script/TBL.TBLGameMode";

    public IniOverrideTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private ServerOptions Parse(string fields) {
        var path = Path.Combine(_directory, "server.yaml");
        File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\nserver:\n  ini:\n    Game:\n      " + Section + ":\n" +
            string.Join("\n", fields.Split('\n').Select(line => "        " + line)));
        return Assert.IsType<ServerOptions>(CommandLineOptions.Parse(["server", "--config-path", path]));
    }

    [Fact]
    public void InferredFieldsPatchOnlySelectedKeysAndReplaceLists() {
        var options = Parse("""
            CustomMaps: {type: 'String[]', value: [FFA_Courtyard, FFA_Wardenglade]}
            BotBackfillEnabled: true
            ClassLimits:
              - Class: Poleman
                ClassLimitPercent: 10
              - Class: Archer
                ClassLimitPercent: 0
            """);
        var path = Path.Combine(_directory, "Game.ini");
        File.WriteAllText(path, $"; preserve comment\n[{Section}]\nServerName=Original\nBotBackfillEnabled=False\n+CustomMaps=Old\n+MapList=KeepMap\n.ClassLimits=(Class=Knight,ClassLimitPercent=25)\nCustom=keep-me\n[/Other]\nUntouched=yes\n");
        options.Ini.Apply(_directory);
        var text = File.ReadAllText(path);
        Assert.Contains("ServerName=Original", text);
        Assert.Contains("Custom=keep-me", text);
        Assert.Contains("Untouched=yes", text);
        Assert.Contains("; preserve comment", text);
        Assert.Contains("BotBackfillEnabled=True", text);
        Assert.Contains("!CustomMaps=ClearArray", text);
        Assert.Contains(".CustomMaps=FFA_Courtyard", text);
        Assert.Contains(".CustomMaps=FFA_Wardenglade", text);
        Assert.Contains("+MapList=KeepMap", text);
        Assert.Contains(".ClassLimits=(Class=Poleman,ClassLimitPercent=10)", text);
        Assert.Contains(".ClassLimits=(Class=Archer,ClassLimitPercent=0)", text);
        Assert.DoesNotContain("CustomMaps=Old", text);
        Assert.DoesNotContain("Class=Knight", text);
        options.Ini.Apply(_directory);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void ExplicitAndInferredWrappersAndPositionalParametersWork() {
        var options = Parse("""
            CustomMaps: {type: 'String[]', value: []}
            BotBackfillEnabled: {value: false}
            ClassLimits: {type: 'ClassLimit[]', value: [[Archer, 0.25]]}
            Custom: {type: String, value: example}
            Balance: {type: AutoBalance, value: [0, 64, 32]}
            Callbacks: {type: CallbackList, value: [first, second]}
            """);
        options.Ini.Apply(_directory);
        var text = File.ReadAllText(Path.Combine(_directory, "Game.ini"));
        Assert.Contains("!CustomMaps=ClearArray", text);
        Assert.Contains("BotBackfillEnabled=False", text);
        Assert.Contains(".ClassLimits=(Class=Archer,ClassLimitPercent=0.25)", text);
        Assert.Contains("Custom=example", text);
        Assert.Contains("Balance=(MinNumPlayers=0,MaxNumPlayers=64,AllowedNumPlayersDifference=32)", text);
        Assert.Contains("Callbacks=(first, second)", text);
    }

    [Fact]
    public void RawIsNotParsedOrWrittenToProfile() {
        var options = Parse("Unknown: {type: raw, value: '  SomeStructuredThing(foo, bar, 10, baz, false)  '}");
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
        Parse($"Unknown: {{type: {type}, value: {value}}}").Ini.Apply(_directory);
        Assert.Contains("Unknown=" + expected, File.ReadAllText(Path.Combine(_directory, "Game.ini")));
    }

    [Theory]
    [InlineData("Unknown: example", "Unknown field")]
    [InlineData("Unknown: [a, b]", "supply { type:")]
    [InlineData("Unknown: {value: example}", "Unknown field")]
    [InlineData("Unknown: {type: raw, value: [a]}", "raw requires a string")]
    [InlineData("Unknown: {type: SecretType, value: secret}", "Unsupported type")]
    [InlineData("Unknown: {type: String}", "supply value")]
    [InlineData("Unknown: {type: String, value: x, typo: y}", "Use only type and value")]
    [InlineData("BotBackfillEnabled: nope", "Boolean")]
    [InlineData("BotBackfillEnabled: {type: String, value: nope}", "requires type Boolean")]
    [InlineData("BotBackfillLowBots: 1.5", "Int32")]
    [InlineData("CustomMaps: {type: 'String[]', value: map}", "require a list")]
    [InlineData("CustomMaps: {type: 'String[]', value: [null]}", "cannot be null")]
    [InlineData("ClassLimits: [{Class: Invalid, ClassLimitPercent: 0}]", "CharacterClass")]
    [InlineData("ClassLimits: [{Class: Archer}]", "Missing ClassLimit.ClassLimitPercent")]
    [InlineData("ClassLimits: [{Class: Archer, ClassLimitPercent: 0, Typo: 1}]", "Invalid or duplicate member")]
    [InlineData("ClassLimits: [[Archer]]", "requires a member mapping")]
    [InlineData("Unknown: {type: String, value: \"a\\nb\"}", "line breaks")]
    [InlineData("MinTimeBeforeStartingMatch: NaN", "finite")]
    [InlineData("ClassLimits: []\nclasslimits: []", "Duplicate INI field")]
    public void InvalidValuesFailBeforeFilesAreWritten(string fields, string error) {
        var exception = Assert.Throws<ArgumentException>(() => Parse(fields));
        Assert.Contains(error, exception.Message);
        Assert.False(File.Exists(Path.Combine(_directory, "Game.ini")));
    }

    [Fact]
    public void InvariantNumbersAndNoSecretValuesInErrors() {
        var previous = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var options = Parse("ClassLimits: [[Archer, 0.25]]");
            options.Ini.Apply(_directory);
            Assert.Contains("ClassLimitPercent=0.25", File.ReadAllText(Path.Combine(_directory, "Game.ini")));
            var error = Assert.Throws<ArgumentException>(() => Parse("BotBackfillEnabled: secret-password"));
            Assert.DoesNotContain("secret-password", error.Message);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("ServerName: Custom")]
    [InlineData("ServerName: {type: raw, value: Custom}")]
    public void ExplicitServerNameIsNotShadowedByGeneratedName(string yaml) {
        var options = Parse(yaml);
        Assert.DoesNotContain(options.ToServerLaunchOptions().ToCLIArgs(), arg => arg is UEINIParameter { Key: "ServerName" });
    }

    [Fact]
    public void ProfilePreparationUsesNormalizedSuffixAndAppliesOnlyRequestedFields() {
        var path = Path.Combine(_directory, "launch.yaml");
        File.WriteAllText(path, """
            chiv2-path: .
            platform: Steam
            server:
              saved-dir-suffix: My-Server
              ini:
                Engine:
                  /Script/OnlineSubsystemUtils.IpNetDriver:
                    NetServerMaxTickRate: 30
                    ConnectionTimeout: 12.5
                Game:
                  /Script/TBL.TBLTitleScreen:
                    bSavedHasAgreedToTOS: false
                  /Script/TBL.TBLGameMode:
                    ServerName: INI Name
                GameUserSettings:
                  CustomSection:
                    PluginOption: {type: Boolean, value: true}
            """);
        var options = Assert.IsType<ServerOptions>(CommandLineOptions.Parse(["server", "--config-path", path]));
        File.WriteAllText(Path.Combine(_directory, "Engine.ini"), "[/Script/OnlineSubsystemUtils.IpNetDriver]\nMaxClientRate=12345\n");
        ServerLauncher.InitializeServerProfile(options, suffix => {
            Assert.Equal("MyServer", suffix);
            return _directory;
        });
        var engine = File.ReadAllText(Path.Combine(_directory, "Engine.ini"));
        Assert.Contains("MaxClientRate=12345", engine);
        Assert.Contains("NetServerMaxTickRate=30", engine);
        Assert.Contains("ConnectionTimeout=12.5", engine);
        Assert.DoesNotContain("InitialConnectTimeout=", engine);
        var game = File.ReadAllText(Path.Combine(_directory, "Game.ini"));
        Assert.Contains("bSavedHasAgreedToTOS=False", game);
        Assert.Contains("ServerName=INI Name", game);
        Assert.DoesNotContain("bSavedHasAgreedToTOS=True", options.ToLaunchOptions().LaunchArgs);
        Assert.Contains("PluginOption=True", File.ReadAllText(Path.Combine(_directory, "GameUserSettings.ini")));
    }
}