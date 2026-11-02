using UnchainedLauncher.Core.Services.Mods;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace UnchainedLauncher.CLI.Tests;

public sealed class MapProfileIniTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "map profile ini " + Guid.NewGuid().ToString("N"));

    private CliCommandOptions Parse(string fixture, params (string Token, string Value)[] replacements) {
        var path = YamlTestResources.Write(_root, "map-profile-ini/" + fixture, replacements);
        return CommandLineOptions.Parse(["server", "--config-path", path]);
    }

    private ServerOptions Server(string fixture, params (string Token, string Value)[] replacements) {
        var server = Assert.IsType<ServerOptions>(Parse(fixture, replacements));
        server.Shared.Assets = new ProfileAssetPaths(server.Shared.SavedDirSuffix, _root);
        return server;
    }

    private static string[] ProfileDirectories(ServerOptions server) {
        var directory = Path.Combine(server.MapProfilePath!, "ini-overrides");
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(directory, "unchained.yaml"))));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        Assert.False(root.Children.ContainsKey(new YamlScalarNode("map_profiles")));
        var entries = Assert.IsType<YamlSequenceNode>(root["match-rotation"]);
        Assert.Equal(entries.Children.Count, entries.Children.Select(node => ((YamlScalarNode)((YamlMappingNode)node)["profile"]).Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        return entries.Children.Select((node, index) => {
            var entry = Assert.IsType<YamlMappingNode>(node);
            Assert.Equal(server.Settings.Rotation!.Names[index], ((YamlScalarNode)entry["name"]).Value);
            return Path.Combine(directory, "profiles", ((YamlScalarNode)entry["profile"]).Value!);
        }).ToArray();
    }

    [Fact]
    public async Task PublishesSeparateProfileAndIndexLayersWithoutBaselineCopies() {
        var path = Path.Combine(AppContext.BaseDirectory, "test-resources", "settings-resolution", "legacy-profile-arrays", "server.yaml");
        var server = Assert.IsType<ServerOptions>(CommandLineOptions.Parse(["server", "--config-path", path]));
        server.Shared.Assets = new ProfileAssetPaths(server.Shared.SavedDirSuffix, _root);
        await MapProfileIni.PrepareAsync(server, CancellationToken.None);
        var directories = ProfileDirectories(server);
        Assert.Equal(4, directories.Length);
        Assert.NotEqual(directories[0], directories[2]);
        Assert.Equal("day", Path.GetFileName(directories[0]));
        Assert.Equal("inline-2", Path.GetFileName(directories[2]));
        var inline = Path.Combine(server.MapProfilePath!, "ini-overrides", "indices", "2");
        Assert.False(Directory.Exists(directories[2]));
        Assert.Equal(File.ReadAllText(Path.Combine(directories[0], "engine.ini")), File.ReadAllText(Path.Combine(inline, "engine.ini")));
        Assert.NotEqual(directories[0], directories[1]);
        Assert.False(Directory.Exists(Path.Combine(server.MapProfilePath!, "ini-overrides", "map-indices")));
        Assert.Equal(3, server.Settings.Rotation!.StartIndex);
        var day = File.ReadAllText(Path.Combine(directories[0], "engine.ini"));
        var night = File.ReadAllText(Path.Combine(directories[1], "Engine.ini"));
        Assert.False(Directory.Exists(directories[3]));
        Assert.Contains("Value=20", day);
        Assert.Contains("!Names=ClearArray", day);
        Assert.DoesNotContain(".Names=", day);
        Assert.Contains("Value=30", night);
        Assert.Equal(new[] { "-Names=A" },
            night.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Contains("Names=")));
        foreach (var text in new[] { day, night }) Assert.DoesNotContain("Neighbor=keep", text);
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
        var first = Server("generation-change", ("OTHER", "2"));
        await MapProfileIni.PrepareAsync(first, CancellationToken.None);
        var old = Path.Combine(first.MapProfilePath!, "ini-overrides", "indices", "0");
        File.WriteAllText(Path.Combine(old, "Unrelated.ini"), "keep");
        await MapProfileIni.PrepareAsync(first, CancellationToken.None);
        Assert.Equal(old, Path.Combine(first.MapProfilePath!, "ini-overrides", "indices", "0"));

        var changed = Server("generation-change", ("OTHER", "3"));
        await MapProfileIni.PrepareAsync(changed, CancellationToken.None);
        var current = Path.Combine(changed.MapProfilePath!, "ini-overrides", "indices", "0");
        Assert.NotEqual(old, current);
        Assert.Contains("Value=9", File.ReadAllText(Path.Combine(current, "Engine.ini")));
        Assert.DoesNotContain("Other=", File.ReadAllText(Path.Combine(current, "Engine.ini")));
        Assert.False(File.Exists(Path.Combine(current, "Unrelated.ini")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(old, "Unrelated.ini")));

        var removed = Server("removed-overrides");
        await MapProfileIni.PrepareAsync(removed, CancellationToken.None);
        Assert.Null(removed.MapProfilePath);
        Assert.DoesNotContain("unchained-profile-location", removed.ToLaunchOptions().LaunchArgs);
    }

    [Fact]
    public async Task SharedProfilesDoNotCopyEachServersOwnDefaults() {
        var group = Assert.IsType<ServerGroupOptions>(Parse("shared-defaults"));
        foreach (var entry in group.Servers) {
            var server = entry.Options;
            server.Shared.Assets = new ProfileAssetPaths(server.Shared.SavedDirSuffix, _root);
            await MapProfileIni.PrepareAsync(server, CancellationToken.None);
            var text = File.ReadAllText(Path.Combine(ProfileDirectories(server)[0], "Engine.ini"));
            Assert.Contains("Value=9", text);
            Assert.DoesNotContain("Default=", text);
            var composed = Path.Combine(_root, entry.Id);
            NativeIniTestLayers.Compose(server, 0).Apply(composed);
            Assert.Contains("Default=" + (entry.Id == "first" ? "10" : "20"), File.ReadAllText(Path.Combine(composed, "Engine.ini")));
        }
        Assert.NotEqual(group.Servers[0].Options.MapProfilePath, group.Servers[1].Options.MapProfilePath);
    }

    [Theory]
    [InlineData("invalid-raw", "cannot use raw")]
    [InlineData("missing-default", "requires a server.ini default")]
    [InlineData("invalid-index", "MapListIndex")]
    [InlineData("invalid-map-list", "map-rotation")]
    [InlineData("conflicting-path", "managed by map-profile INI")]
    [InlineData("conflicting-path-equals", "managed by map-profile INI")]
    [InlineData("conflicting-raw", "cannot use raw")]
    public void InvalidMapOverridesFailBeforeWritingFiles(string fixture, string error) {
        var exception = Assert.Throws<ArgumentException>(() => Server(fixture));
        Assert.Contains(error, exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "Chivalry 2")));
    }

    [Fact]
    public async Task CancellationAndWriteFailureDoNotSelectAnIncompleteGeneration() {
        var server = Server("per-map");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MapProfileIni.PrepareAsync(server, stop.Token));
        Assert.Null(server.MapProfilePath);
        Directory.CreateDirectory(server.InstancePaths.Root);
        File.WriteAllText(Path.Combine(server.InstancePaths.Root, "map-config"), "not a directory");
        await Assert.ThrowsAnyAsync<IOException>(() => MapProfileIni.PrepareAsync(server, CancellationToken.None));
        Assert.Null(server.MapProfilePath);
    }

    [Fact]
    public async Task ProtonMapsTheGeneratedRootBeforeBuildingArguments() {
        if (!OperatingSystem.IsLinux()) return;
        var server = Server("per-map");
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
