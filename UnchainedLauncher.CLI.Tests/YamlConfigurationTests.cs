using System.Diagnostics;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Mods.Registry;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

[Collection("Installation directory")]
public class YamlConfigurationTests : IDisposable {
    private readonly string _directory = Path.Combine(
        Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "yaml-cli-tests-" + Guid.NewGuid().ToString("N"));

    public YamlConfigurationTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string body, bool includeRequired = true) {
        var path = Path.Combine(_directory, "launch.yaml");
        File.WriteAllText(path, (includeRequired ? "chiv2-path: './game installation'\nplatform: Steam\n" : "") + body);
        return path;
    }

    private CliLaunchOptions Load(string body, bool client = false) =>
        CliLaunchOptions.Parse([client ? "client" : "server", "--config-path", Write(body)]);

    [Fact]
    public void ConfigurationIsAuthoritativeWithEitherConfigPathSyntax() {
        var path = Write("server:\n  name: From YAML\n");
        foreach (var args in new[] {
            new[] { "server", "--typo", "--name", "Ignored", "--config-path", path, "--", "-ignored-game-arg" },
            new[] { "server", "--config-path=" + path, "--platform=Invalid", "--help" }
        }) {
            var options = CliLaunchOptions.Parse(args);
            Assert.IsType<ServerOptions>(options);
            Assert.False(options.Help);
            Assert.Equal("From YAML", Assert.IsType<ServerOptions>(options).Settings.Name);
            Assert.Empty(options.GameArguments);
        }
    }

    [Fact]
    public void AuthoritativeYamlIgnoresResponseFileTokensAndAllOtherOptionErrors() {
        var path = Write("server: {name: From YAML}\n");
        var response = Path.Combine(_directory, "ignored.rsp");
        File.WriteAllText(response, "--config-path=must-not-be-loaded --name=must-not-be-bound");
        foreach (var command in new[] { "server", "client" }) {
            foreach (var token in new[] { "@" + response, "@" + Path.Combine(_directory, "missing.rsp") }) {
                string[] ignored = ["--name", token, "--name=duplicate", "--platform=Invalid", "--game-port=invalid", "--headless=invalid",
                    "--no-plugin-update", "--no-plugin-update", "--unknown", "unexpected", "--mod=invalid", token];
                foreach (var args in new string[][] {
                    [command, "--config-path", path, .. ignored, "--", "@game-response"],
                    [command, .. ignored, "--config-path=" + path],
                    [command, .. ignored, "--config-path=" + path, "--help"]
                }) {
                    var options = CliLaunchOptions.Parse(args);
                    Assert.False(options.Help);
                    Assert.True(options.Shared.UpdatePlugin);
                    Assert.Empty(options.GameArguments);
                    if (command == "server") Assert.Equal("From YAML", Assert.IsType<ServerOptions>(options).Settings.Name);
                    else Assert.IsType<ClientOptions>(options);
                }
            }
        }
    }

    [Fact]
    public void OrdinaryOptionValuesDoNotExpandExistingOrMissingResponseFiles() {
        var response = Path.Combine(_directory, "ordinary.rsp");
        File.WriteAllText(response, "--name expanded --platform Invalid");
        foreach (var token in new[] { "@" + response, "@" + Path.Combine(_directory, "missing.rsp") }) {
            var options = CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", "--name", token]);
            Assert.Equal(token, Assert.IsType<ServerOptions>(options).Settings.Name);
            Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", token]));
        }
    }

    [Theory]
    [InlineData("server", false)]
    [InlineData("server", true)]
    [InlineData("client", false)]
    [InlineData("client", true)]
    public void OneLibraryParseControlsBothOrdinaryAndYamlLaunches(string command, bool yaml) {
        string[] args = yaml
            ? [command, "--config-path", Write(""), "--platform=Invalid", "--unknown"]
            : [command, "--chivalry2-path", ".", "--platform", "Steam"];
        var calls = 0;
        var options = CommandLineOptions.Parse([.. args, "--", "@game-file", "--config-path", "not-config"], (root, tokens, configuration) => {
            calls++;
            Assert.Null(configuration.ResponseFileTokenReplacer);
            Assert.Equal(args, tokens);
            Assert.Equal(new[] { "server", "client", "mod-query", "dll-query" }, root.Subcommands.Select(subcommand => subcommand.Name));
            Assert.Single(root.Options.Where(option => option.Name == "--config-path"));
            foreach (var subcommand in root.Subcommands.Where(command => command.Name is "client" or "server")) {
                Assert.Single(subcommand.Options.Where(option => option.Name == "--platform"));
                Assert.Single(subcommand.Options.Where(option => option.Name == "--chivalry2-path"));
            }
            return root.Parse(tokens, configuration);
        });
        Assert.Equal(1, calls);
        Assert.Equal(command == "server", options is ServerOptions);
        Assert.Equal(yaml ? [] : new[] { "@game-file", "--config-path", "not-config" }, Assert.IsAssignableFrom<CliLaunchOptions>(options).GameArguments);
    }

    [Fact]
    public void AuthoritativeYamlStillRejectsMissingEmptyAndDuplicateSelectors() {
        var path = Write("");
        foreach (var command in new[] { "server", "client" }) {
            foreach (var selectors in new[] {
                new[] { "--config-path" },
                new[] { "--config-path=" },
                new[] { "--config-path", " " },
                new[] { "--config-path", path, "--config-path", path },
                new[] { "--config-path=" + path, "--config-path=" + path },
                new[] { "--config-path", path, "--config-path" }
            }) {
                Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse([command, "--platform=Invalid", .. selectors]));
                Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse([command, "--platform=Invalid", .. selectors, "--help"]));
            }
        }
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["--config-path", path]));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["--config-path", path, "--help"]));
    }

    [Fact]
    public void ConfigPathAfterGameSeparatorIsForwardedNotLoaded() {
        var options = CliLaunchOptions.Parse(["server", "--chivalry2-path", _directory, "--platform", "Steam", "--", "--config-path", "game-file"]);
        Assert.Equal(new[] { "--config-path", "game-file" }, options.GameArguments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SubcommandSelectsModeRegardlessOfIncludedBlocks(bool client) {
        var options = Load("client: {}\nserver: {}\n", client);
        Assert.Equal(client, options is ClientOptions);
        Assert.Equal(client, options.ToLaunchOptions().ServerLaunchOptions.IsNone);
        if (client) Assert.StartsWith("TBL --additional-pak-dir ", options.ToLaunchOptions().LaunchArgs);
        else Assert.StartsWith("TBL -server --additional-pak-dir ", options.ToLaunchOptions().LaunchArgs);
        Assert.Equal(!client, options.ToLaunchOptions().ToCLIArgs().Any(arg => arg.Rendered == "-nullrhi"));
    }

    [Fact]
    public void ExplicitClientIgnoresServerFieldsEvenWhenTheyAreInvalid() {
        var options = Load("""
            client:
              game-args: ['-client-only']
            game-args: ['-AUTH_PASSWORD=auth value']
            server:
              game-port: not-a-port
              headless: not-a-boolean
              typo: whatever
              mod-actors: not-a-list
              discord:
                bot-token: secret
                general-channel-id: []
              game-args: ['-server']
            """, client: true);
        Assert.IsType<ClientOptions>(options);
        var launch = options.ToLaunchOptions();
        Assert.True(launch.ServerLaunchOptions.IsNone);
        Assert.DoesNotContain("-server", launch.LaunchArgs);
        Assert.Contains("\"-AUTH_PASSWORD=auth value\"", launch.LaunchArgs);
        Assert.Contains("-client-only", launch.LaunchArgs);
    }

    [Fact]
    public void ServerIgnoresClientBlockAndEachCommandValidatesOnlyItsOwnSchema() {
        var server = Load("client: not-a-mapping\nserver: {name: Correct}");
        Assert.Equal("Correct", Assert.IsType<ServerOptions>(server).Settings.Name);
        Assert.Throws<ArgumentException>(() => Load("client: not-a-mapping", client: true));
        Assert.Throws<ArgumentException>(() => Load("client: {typo: invalid}", client: true));
        Assert.IsType<ClientOptions>(Load("server: not-a-mapping\nclient: {}", client: true));
    }

    public static TheoryData<bool, string> InactiveYamlNodes {
        get {
            var data = new TheoryData<bool, string>();
            foreach (var client in new[] { true, false }) {
                foreach (var node in new[] {
                    "not-a-mapping", "null", "[]", "{}", "*unused-alias",
                    "{game-args: [], game-args: []}",
                    "{nested: {password: first-secret, password: second-secret}}",
                    "[{nested: {field: one, field: two}}, [scalar, {field: [], field: {}}]]",
                    "{nested: [{field: one, field: two}], nested: ignored}",
                    "!ignored-tag {field: one, field: two}",
                    "|-\n  literal scalar\n  field: not-a-mapping",
                    "\n  nested:\n    field: one\n    field: two",
                    "\n  - nested:\n      field: one\n      field: two\n  - [scalar, {}]"
                }) data.Add(client, node);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(InactiveYamlNodes))]
    public void InactiveYamlNodeIsSkippedAtProductionLaunchBoundary(bool client, string node) {
        var active = client ? "client" : "server";
        var inactive = client ? "server" : "client";
        var options = Load($"{inactive}: {node}\n{active}: {{game-args: ['-active']}}\ngame-args: ['-shared']\n", client);
        Assert.Equal(client, options is ClientOptions);
        var launch = options.ToLaunchOptions();
        Assert.Equal(client, launch.ServerLaunchOptions.IsNone);
        Assert.Contains("-active", launch.LaunchArgs);
        Assert.Contains("-shared", launch.LaunchArgs);
        Assert.DoesNotContain("secret", launch.LaunchArgs);
        Assert.Equal(!client, launch.ToCLIArgs().Any(arg => arg.Rendered == "-nullrhi"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InactiveYamlAliasIsNotBoundToAnActiveObject(bool client) {
        var active = client ? "client" : "server";
        var inactive = client ? "server" : "client";
        var options = Load($"{active}: &active {{game-args: ['-active']}}\n{inactive}: *active\n", client);
        Assert.Equal(client, options is ClientOptions);
        Assert.Contains("-active", options.ToLaunchOptions().LaunchArgs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SkippingInactiveYamlRetainsRootSharedAndActiveDuplicateChecks(bool client) {
        var active = client ? "client" : "server";
        var inactive = client ? "server" : "client";
        foreach (var body in new[] {
            "platform: Steam\n",
            "game-args: []\ngame-args: []\n",
            "mod-setup: {mods: [], mods: []}\n",
            $"{active}: {{game-args: [], game-args: []}}\n",
            $"{active}: {{}}\n{active}: {{}}\n",
            $"{inactive}: {{}}\n"
        }) {
            var error = Assert.Throws<ArgumentException>(() => Load($"{inactive}: {{nested: {{field: one, field: two}}}}\n" + body, client));
            Assert.Contains("Invalid YAML configuration or schema", error.Message);
        }
        if (!client) {
            Assert.Throws<ArgumentException>(() => Load("client: {}\nserver: {discord: {bot-token: first-secret, bot-token: second-secret}}\n"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SkippedInactiveYamlStillRequiresValidSyntaxWithoutExposingSecrets(bool client) {
        var inactive = client ? "server" : "client";
        var error = Assert.Throws<ArgumentException>(() => Load($"{inactive}: {{password: TOP-SECRET, nested: [}}\n", client));
        Assert.Contains("Invalid YAML configuration or schema", error.Message);
        Assert.DoesNotContain("TOP-SECRET", error.Message);
    }

    [Fact]
    public void ServerSafetyFlagsDefaultOnAndCanBeDisabledInYaml() {
        var defaults = Assert.IsType<ServerOptions>(Load("server: {}")).ToServerLaunchOptions();
        Assert.True(defaults.Headless);
        Assert.True(defaults.DesyncPatch);
        Assert.True(defaults.UseBackendBanlist);
        Assert.True(defaults.LocalIp.IsSome);
        Assert.Contains("-nullrhi", defaults.ToCLIArgs().Select(arg => arg.Rendered));
        Assert.Contains("--desync-patch", defaults.ToCLIArgs().Select(arg => arg.Rendered));
        var disabled = Assert.IsType<ServerOptions>(Load("server: {desync-patch: false, use-backend-banlist: false}")).ToServerLaunchOptions();
        Assert.False(disabled.DesyncPatch);
        Assert.False(disabled.UseBackendBanlist);
        Assert.DoesNotContain("--desync-patch", disabled.ToCLIArgs().Select(arg => arg.Rendered));
        Assert.DoesNotContain("--use-backend-banlist", disabled.ToCLIArgs().Select(arg => arg.Rendered));
    }

    [Theory]
    [InlineData("server: {headless: false}")]
    [InlineData("server: {headless: false, desync-patch: true}")]
    public void RenderingRequiresExplicitlyDisablingDesyncPatchInYaml(string yaml) {
        var error = Assert.Throws<ArgumentException>(() => Load(yaml));
        Assert.Contains("headless", error.Message);
        Assert.Contains("--no-desync-patch", error.Message);
        Assert.Contains("server.desync-patch: false", error.Message);
        var client = Assert.IsType<ClientOptions>(Load(yaml, client: true)).ToLaunchOptions();
        Assert.True(client.ServerLaunchOptions.IsNone);
        Assert.StartsWith("TBL --additional-pak-dir ", client.LaunchArgs);
    }

    [Fact]
    public void RenderingWithExplicitlyDisabledDesyncPatchInYamlIsAllowed() {
        var options = Assert.IsType<ServerOptions>(Load("server: {headless: false, desync-patch: false}"));
        var server = options.ToServerLaunchOptions();
        Assert.False(server.Headless);
        Assert.False(server.DesyncPatch);
        Assert.True(server.UseBackendBanlist);
        Assert.DoesNotContain("-nullrhi", server.ToCLIArgs().Select(arg => arg.Rendered));
        Assert.DoesNotContain("--desync-patch", server.ToCLIArgs().Select(arg => arg.Rendered));
        Assert.True(options.ToLaunchOptions().ServerLaunchOptions.IsSome);
    }

    [Theory]
    [InlineData("ffa-time-limit")]
    [InlineData("ffa-score-limit")]
    [InlineData("tdm-time-limit")]
    [InlineData("tdm-ticket-count")]
    [InlineData("player-bot-count")]
    [InlineData("warmup-time")]
    public void IniFieldsAreNotExposedInYamlOrCommandLine(string field) {
        Assert.Throws<ArgumentException>(() => Load($"server: {{{field}: 1}}"));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", $"--{field}=1"]));
        Assert.IsType<ClientOptions>(Load($"server: {{{field}: 1}}", client: true));
    }

    [Fact]
    public void PathsAreRelativeToYamlFileWithoutChangingCwdAndDefaultRegistryRemains() {
        var cwd = Directory.GetCurrentDirectory();
        Directory.CreateDirectory(Path.Combine(_directory, "local registry"));
        var options = Load("""
            update-plugin: false
            allow-unstable-plugin: true
            no-tui: true
            server:
              saved-dir-suffix: Test_1
              mod-setup:
                registries: ['local:local registry', 'config:registries.json', 'github:owner/repo']
                mods: ['org/one:v1.2.3', 'org/two:v2.0.0']
                dlls: ['org/plugin:v1.0.0']
            """);
        Assert.Equal(cwd, Directory.GetCurrentDirectory());
        Assert.Equal(Path.Combine(_directory, "game installation"), options.Shared.Chivalry2Path);
        Assert.Equal(Path.Combine(_directory, "local registry"), options.Shared.Registries[0].Location);
        Assert.Equal(Path.Combine(_directory, "registries.json"), options.Shared.Registries[1].Location);
        Assert.Equal("owner/repo", options.Shared.Registries[2].Location);
        Assert.Equal(2, options.Shared.Mods.Count);
        Assert.Single(options.Shared.Dlls);
        Assert.Equal("Test_1", options.Shared.SavedDirSuffix);
        Assert.False(options.Shared.UpdatePlugin);
        Assert.True(options.Shared.AllowUnstablePlugin);
        Assert.True(Assert.IsType<ServerOptions>(options).NoTui);
        var plain = Load("server: {mod-setup: { registries: [], mods: [], dlls: [] }}");
        var official = Assert.Single(ServerLauncher.CreateRegistry(plain).ModRegistries);
        Assert.IsType<GithubModRegistry>(official);
        Assert.Equal("Chiv2-Community", ((GithubModRegistry)official).Organization);
    }

    [Fact]
    public void AllServerAndDiscordFieldsMapToCoreAndRenderedArguments() {
        var options = Load("""
            client:
              typo: this ignored client field would be invalid in client mode
              game-args: ['-client-only']
            server-browser-backend: https://example.test
            game-args: ['-log']
            server:
              headless: true
              name: YAML Server
              description: Example MOTD
              password: private password
              starting-map: TO_Test
              local-ip: 127.0.0.2
              game-port: 10001
              ping-port: 10002
              query-port: 10003
              rcon-port: 10004
              register: true
              mod-actors: ['/Game/A.A_C', '/Game/B.B_C', '/Game/A.A_C']
              desync-patch: true
              use-backend-banlist: true
              censor-mode: zealous
              game-args: ['-custom=value with spaces']
              discord:
                bot-token: '--secret-token'
                admin-channel-id: '123456789012345678'
                general-channel-id: '234567890123456789'
                dashboard-channel-id: '345678901234567890'
                event-log-channel-id: '456789012345678901'
                admin-role-id: '567890123456789012'
                mention-admins: false
            """);
        var server = Assert.IsType<ServerOptions>(options).ToServerLaunchOptions();
        Assert.True(server.Headless);
        Assert.Equal("YAML Server", server.Name);
        Assert.Equal("Example MOTD", server.Description);
        Assert.Equal("private password", server.Password.IfNone(""));
        Assert.Equal("TO_Test", server.Map);
        Assert.Equal("127.0.0.2", server.LocalIp.IfNone(""));
        Assert.Equal((10001, 10002, 10003, 10004), (server.GamePort, server.BeaconPort, server.QueryPort, server.RconPort));
        Assert.True(server.RegisterWithBackend);
        Assert.True(server.DesyncPatch);
        Assert.True(server.UseBackendBanlist);
        Assert.Equal(Core.Services.Processes.Chivalry.CensorArg.Zealous, server.CensorMode);
        Assert.Null(server.FFATimeLimit);
        Assert.Null(server.FFAScoreLimit);
        Assert.Null(server.TDMTimeLimit);
        Assert.Null(server.TDMTicketCount);
        Assert.Null(server.PlayerBotCount);
        Assert.Null(server.WarmupTime);
        Assert.Equal(2, server.ServerMods.Count());
        var rendered = server.ToCLIArgs().Select(arg => arg.Rendered).ToArray();
        Assert.Contains("--server-mods /Game/A.A_C,/Game/B.B_C", rendered);
        Assert.Contains("--desync-patch", rendered);
        Assert.Contains("--use-backend-banlist", rendered);
        Assert.Contains("--discord-bot-token --secret-token", rendered);
        Assert.Contains("--discord-mention-admins false", rendered);
        Assert.Contains("--discord-admin-channel-id 123456789012345678", rendered);
        Assert.Contains("--discord-general-channel-id 234567890123456789", rendered);
        Assert.Contains("--discord-dashboard-channel-id 345678901234567890", rendered);
        Assert.Contains("--discord-event-log-channel-id 456789012345678901", rendered);
        Assert.Contains("--discord-admin-role-id 567890123456789012", rendered);
        Assert.Contains("-nullrhi", rendered);
        Assert.Contains("-log", options.ToLaunchOptions().LaunchArgs);
        Assert.Contains("\"-custom=value with spaces\"", options.ToLaunchOptions().LaunchArgs);
        Assert.DoesNotContain("-client-only", options.ToLaunchOptions().LaunchArgs);
    }

    [Theory]
    [InlineData("typo: value")]
    [InlineData("server: []")]
    [InlineData("server: {mod-setup: {registries: not-a-list}}")]
    [InlineData("server: {mod-setup: {mods: [null]}}")]
    [InlineData("server: {mod-setup: {mods: ['org/mod:v1', 'org/mod:v2']}}")]
    [InlineData("saved-dir-suffix: OldRootValue")]
    [InlineData("mod-setup: {mods: []}")]
    [InlineData("server: {query-port: 7777}")]
    [InlineData("server: {game-port: 0}")]
    [InlineData("server: {game-port: 1.5}")]
    [InlineData("server: {headless: invalid}")]
    [InlineData("server: {typo: true}")]
    [InlineData("server: {map: TO_Test}")]
    [InlineData("server: {starting-map: ''}")]
    [InlineData("server: {censor-mode: invalid}")]
    [InlineData("server: {warmup-time: -1}")]
    [InlineData("server: {discord: {bot-token: secret}}")]
    [InlineData("server: {discord: {general-channel-id: '123'}}")]
    [InlineData("server: {name: one, name: two}")]
    [InlineData("game-args: {invalid: mapping}")]
    public void InvalidConfigurationFailsBeforeLaunching(string body) {
        Assert.Throws<ArgumentException>(() => Load(body));
    }

    [Fact]
    public void InvalidYamlMissingFilesAndDocumentsFailWithoutExposingSecrets() {
        var malformed = Write("server:\n  password: TOP-SECRET\n  discord: [\n");
        var error = Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--config-path", malformed]));
        Assert.DoesNotContain("TOP-SECRET", error.Message);
        Assert.Throws<ArgumentException>(() => YamlConfiguration.Load(Path.Combine(_directory, "missing.yaml"), false));
        Assert.Throws<ArgumentException>(() => YamlConfiguration.Load(Write("", includeRequired: false), false));
        Assert.Throws<ArgumentException>(() => YamlConfiguration.Load(Write("---\nchiv2-path: .\nplatform: Steam\n"), false));
        Assert.Throws<ArgumentException>(() => YamlConfiguration.Load(Write("platform: Steam", includeRequired: false), false));
        Assert.Throws<ArgumentException>(() => YamlConfiguration.Load(Write("chiv2-path: .", includeRequired: false), false));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--config-path"]));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--config-path="]));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--config-path=x", "--config-path=y"]));
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["--config-path", Write("")]));
    }

    [Fact]
    public void DirectClientCommandHasNoServerOptions() {
        var options = CliLaunchOptions.Parse(["client", "--chivalry2-path", _directory, "--platform", "EGS"]);
        Assert.IsType<ClientOptions>(options);
        Assert.True(options.ToLaunchOptions().ServerLaunchOptions.IsNone);
        Assert.StartsWith("TBL --additional-pak-dir ", options.ToLaunchOptions().LaunchArgs);
    }

    [Theory]
    [InlineData("Steam", InstallationType.Steam)]
    [InlineData("EGS", InstallationType.EpicGamesStore)]
    [InlineData("EpicGamesStore", InstallationType.EpicGamesStore)]
    public void YamlUsesCoreInstallationTypeAndPreservesEgsAlias(string platform, InstallationType expected) {
        var path = Write($"chiv2-path: .\nplatform: {platform}\nupdate-plugin: false\nallow-unstable-plugin: true\n", includeRequired: false);
        foreach (var command in new[] { "server", "client" }) {
            var options = CliLaunchOptions.Parse([command, "--config-path", path]);
            Assert.Equal(expected, options.Shared.Platform);
            Assert.False(options.Shared.UpdatePlugin);
            Assert.True(options.Shared.AllowUnstablePlugin);
        }
    }

    [Theory]
    [InlineData("NotSet")]
    [InlineData("1")]
    [InlineData("Unknown")]
    public void PlaceholderAndNumericPlatformsAreNotLaunchable(string platform) {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["client", "--chivalry2-path", ".", "--platform", platform]));
        var path = Write($"chiv2-path: .\nplatform: {platform}\n", includeRequired: false);
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--config-path", path]));
    }

    [Fact]
    public void EmptyOptionalServerFieldsAreAllowed() {
        var options = Load("server: {description: '', password: '', local-ip: '', discord: {bot-token: '', general-channel-id: ''}}");
        Assert.Equal("", Assert.IsType<ServerOptions>(options).Settings.Description);
        Assert.True(Assert.IsType<ServerOptions>(options).ToServerLaunchOptions().Password.IsNone);
        Assert.True(Assert.IsType<ServerOptions>(options).ToDiscordOptions().IsNone);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProductionSupervisionLeavesClientRunningButStopsServer(bool client) {
        var options = Load(client ? "client: {}\n" : "no-tui: true\n", client);
        using var child = Process.Start(new ProcessStartInfo {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sleep",
            Arguments = OperatingSystem.IsWindows() ? "/c ping -n 30 127.0.0.1 > nul" : "30",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        try {
            Assert.Equal(0, await Program.SuperviseAsync(options, child, stop));
            Assert.Equal(!client, child.HasExited);
        }
        finally {
            if (!child.HasExited) {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }
    }
}