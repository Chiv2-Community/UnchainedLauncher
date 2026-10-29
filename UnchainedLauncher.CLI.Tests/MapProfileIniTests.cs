using UnchainedLauncher.Core.Services.Mods;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace UnchainedLauncher.CLI.Tests;

public sealed class MapProfileIniTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "map profile ini " + Guid.NewGuid().ToString("N"));

    private CliCommandOptions Parse(string yaml) {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "server.yaml");
        File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\n" + yaml);
        return CommandLineOptions.Parse(["server", "--config-path", path]);
    }

    private ServerOptions Server(string yaml) {
        var server = Assert.IsType<ServerOptions>(Parse(yaml));
        server.Shared.Assets = new ProfileAssetPaths(server.Shared.SavedDirSuffix, _root);
        return server;
    }

    private static string[] ProfileDirectories(ServerOptions server) {
        var directory = Path.Combine(server.MapProfilePath!, "ini-overrides");
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(directory, "unchained.yaml"))));
        var mapping = Assert.IsType<YamlMappingNode>(((YamlMappingNode)yaml.Documents[0].RootNode)["map_profiles"]);
        return Enumerable.Range(0, mapping.Children.Count).Select(index => Path.Combine(directory, "map-profiles",
            ((YamlScalarNode)mapping[new YamlScalarNode(index.ToString())]).Value!)).ToArray();
    }

    [Fact]
    public async Task PublishesIndexToProfileMappingAndDefaultMergedTypedFilesOnly() {
        var server = Server("""
            map-profiles:
              day:
                map-name: FFA_Courtyard
                ini:
                  engine:
                    ModSettings:
                      Value: {type: Int, value: 20}
                      Names: {type: 'String[]', value: []}
              night:
                map-name: FFA_Courtyard
                ini:
                  Engine:
                    ModSettings:
                      Value: {type: Int, value: 30}
            server:
              starting-map: FFA_Wardenglade
              ini:
                Engine:
                  ModSettings:
                    Value: {type: Int, value: 10}
                    Neighbor: {type: String, value: keep}
                    Names: {type: 'String[]', value: [A, A, B]}
              map-rotation:
                - day
                - night
                - map-name: FFA_Courtyard
                  name: Day reprise
                  ini:
                    engine:
                      ModSettings:
                        Value: {type: Int, value: 20}
                        Names: {type: 'String[]', value: []}
                - {map-name: FFA_Wardenglade}
            """);
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        var directories = ProfileDirectories(server);
        Assert.Equal(4, directories.Length);
        Assert.NotEqual(directories[0], directories[2]);
        Assert.Equal("Day", Path.GetFileName(directories[0]));
        Assert.Equal("Day reprise", Path.GetFileName(directories[2]));
        Assert.Equal(File.ReadAllText(Path.Combine(directories[0], "Engine.ini")), File.ReadAllText(Path.Combine(directories[2], "Engine.ini")));
        Assert.NotEqual(directories[0], directories[1]);
        Assert.False(Directory.Exists(Path.Combine(server.MapProfilePath!, "ini-overrides", "map-indices")));
        Assert.Equal(3, server.Settings.Rotation!.StartIndex);
        var day = File.ReadAllText(Path.Combine(directories[0], "Engine.ini"));
        var night = File.ReadAllText(Path.Combine(directories[1], "Engine.ini"));
        var defaults = File.ReadAllText(Path.Combine(directories[3], "Engine.ini"));
        Assert.Contains("Value=20", day);
        Assert.Contains("!Names=ClearArray", day);
        Assert.DoesNotContain(".Names=", day);
        Assert.Contains("Value=30", night);
        Assert.Equal(2, night.Split('\n').Count(line => line.TrimEnd('\r') == ".Names=A"));
        foreach (var text in new[] { day, night, defaults }) Assert.Contains("Neighbor=keep", text);
        Assert.Contains("Value=10", defaults);
        Assert.Contains(".Names=B", defaults);
        Assert.Contains("--unchained-profile-location", server.ToLaunchOptions().LaunchArgs);
        Assert.Contains('"' + server.MapProfilePath + '"', server.ToLaunchOptions().LaunchArgs);

        var config = Path.Combine(_root, "base");
        ServerLauncher.InitializeServerProfile(server, _ => config);
        var baseIni = File.ReadAllText(Path.Combine(config, "Engine.ini"));
        Assert.Contains("Value=10", baseIni);
        Assert.DoesNotContain("Value=20", baseIni);
    }

    [Fact]
    public async Task ChangedAndRemovedOverridesNeverReuseStaleFilesOrSelectOldGenerations() {
        const string yaml = """
            server:
              ini:
                Engine:
                  Mod: {Value: {type: Int, value: 1}, Other: {type: Int, value: 2}}
              map-rotation:
                - map-name: FFA_Courtyard
                  ini:
                    Engine:
                      Mod: {Value: {type: Int, value: 9}}
            """;
        var first = Server(yaml);
        await MapProfileIni.PrepareAsync(first, CancellationToken.None);
        var old = ProfileDirectories(first)[0];
        File.WriteAllText(Path.Combine(old, "Unrelated.ini"), "keep");
        await MapProfileIni.PrepareAsync(first, CancellationToken.None);
        Assert.Equal(old, ProfileDirectories(first)[0]);

        var changed = Server(yaml.Replace("Other: {type: Int, value: 2}", "Other: {type: Int, value: 3}"));
        await MapProfileIni.PrepareAsync(changed, CancellationToken.None);
        var current = ProfileDirectories(changed)[0];
        Assert.NotEqual(old, current);
        Assert.Contains("Other=3", File.ReadAllText(Path.Combine(current, "Engine.ini")));
        Assert.False(File.Exists(Path.Combine(current, "Unrelated.ini")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(old, "Unrelated.ini")));

        var removed = Server("server: {map-rotation: [{map-name: FFA_Courtyard, ini: {}}]}");
        await MapProfileIni.PrepareAsync(removed, CancellationToken.None);
        Assert.Null(removed.MapProfilePath);
        Assert.DoesNotContain("unchained-profile-location", removed.ToLaunchOptions().LaunchArgs);
    }

    [Fact]
    public async Task SharedProfilesMergeWithEachServersOwnDefaults() {
        var group = Assert.IsType<ServerGroupOptions>(Parse("""
            map-profiles:
              shared:
                map-name: FFA_Courtyard
                ini: {Engine: {Mod: {Value: {type: Int, value: 9}}}}
            servers:
              first:
                ini: {Engine: {Mod: {Value: {type: Int, value: 1}, Default: {type: Int, value: 10}}}}
                map-rotation: [shared]
              second:
                game-port: 7778
                ping-port: 3076
                query-port: 7072
                rcon-port: 9002
                ini: {Engine: {Mod: {Value: {type: Int, value: 2}, Default: {type: Int, value: 20}}}}
                map-rotation: [shared]
            """));
        foreach (var entry in group.Servers) {
            var server = entry.Options;
            server.Shared.Assets = new ProfileAssetPaths(server.Shared.SavedDirSuffix, _root);
            await MapProfileIni.PrepareAsync(server, CancellationToken.None);
            var text = File.ReadAllText(Path.Combine(ProfileDirectories(server)[0], "Engine.ini"));
            Assert.Contains("Value=9", text);
            Assert.Contains("Default=" + (entry.Id == "first" ? "10" : "20"), text);
        }
        Assert.NotEqual(group.Servers[0].Options.MapProfilePath, group.Servers[1].Options.MapProfilePath);
    }

    [Theory]
    [InlineData("{Engine: {Mod: {Value: 2}}}", "Unknown field")]
    [InlineData("{Engine: {Mod: {Value: {type: raw, value: secret}}}}", "cannot use raw")]
    [InlineData("{Engine: {Mod: {Other: {type: Int, value: 2}}}}", "requires a server.ini default")]
    [InlineData("{Game: {'/Script/TBL.TBLGameMode': {MapListIndex: 1}}}", "MapListIndex")]
    [InlineData("{Game: {'/Script/TBL.TBLGameMode': {MapList: [FFA_Other]}}}", "map-rotation")]
    public void InvalidMapOverridesFailBeforeWritingFiles(string ini, string error) {
        var exception = Assert.Throws<ArgumentException>(() => Server(
            "map-profiles:\n  one: {map-name: FFA_Courtyard, ini: " + ini + "}\n" +
            "server:\n  ini: {Engine: {Mod: {Value: {type: Int, value: 1}}}}\n  map-rotation: [one]\n"));
        Assert.Contains(error, exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "Chivalry 2")));
    }

    [Fact]
    public async Task CancellationAndWriteFailureDoNotSelectAnIncompleteGeneration() {
        const string yaml = """
            server:
              ini: {Engine: {Mod: {Value: {type: Int, value: 1}}}}
              map-rotation:
                - map-name: FFA_Courtyard
                  ini: {Engine: {Mod: {Value: {type: Int, value: 2}}}}
            """;
        var server = Server(yaml);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MapProfileIni.PrepareAsync(server, stop.Token));
        Assert.Null(server.MapProfilePath);
        Directory.CreateDirectory(server.InstancePaths.Root);
        File.WriteAllText(Path.Combine(server.InstancePaths.Root, "map-config"), "not a directory");
        await Assert.ThrowsAnyAsync<IOException>(() => MapProfileIni.PrepareAsync(server, CancellationToken.None));
        Assert.Null(server.MapProfilePath);
    }

    [Theory]
    [InlineData("  game-args: ['--unchained-profile-location', '/manual']", "managed by map-profile INI")]
    [InlineData("  game-args: ['-unchained-profile-location=/manual']", "managed by map-profile INI")]
    [InlineData("  ini: {Engine: {Mod: {Value: {type: raw, value: anything}}}}", "cannot use raw")]
    public void ConflictingGlobalOverridesAreRejected(string setting, string error) {
        var yaml = """
            server:
              ini: {Engine: {Mod: {Value: {type: Int, value: 1}}}}
              map-rotation:
                - map-name: FFA_Courtyard
                  ini: {Engine: {Mod: {Value: {type: Int, value: 2}}}}
            """;
        yaml = setting.StartsWith("  ini:")
            ? yaml.Replace("  ini: {Engine: {Mod: {Value: {type: Int, value: 1}}}}", setting)
            : yaml + "\n" + setting + "\n";
        Assert.Contains(error, Assert.Throws<ArgumentException>(() => Server(yaml)).Message);
    }

    [Fact]
    public async Task ProtonMapsTheGeneratedRootBeforeBuildingArguments() {
        if (!OperatingSystem.IsLinux()) return;
        var server = Server("""
            server:
              ini: {Engine: {Mod: {Value: {type: Int, value: 1}}}}
              map-rotation:
                - map-name: FFA_Courtyard
                  ini: {Engine: {Mod: {Value: {type: Int, value: 2}}}}
            """);
        var script = Path.Combine(_root, "proton");
        await File.WriteAllTextAsync(script, "#!/bin/sh\n[ -f \"$4/ini-overrides/unchained.yaml\" ] || exit 2\nprintf '%s\\n' 'Z:\\generated profile'\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        server.Shared.Proton = new ProtonRuntime(script, _root, null, "unused");
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        Assert.Equal(@"Z:\generated profile", server.MapProfilePath);
        Assert.Contains("--unchained-profile-location \"Z:\\generated profile\"", server.ToLaunchOptions().LaunchArgs);
    }

    public void Dispose() => Directory.Delete(_root, true);
}