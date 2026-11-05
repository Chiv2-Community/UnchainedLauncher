using YamlDotNet.RepresentationModel;

namespace UnchainedLauncher.CLI.Tests;

// Test-only model of the plugin's folder order; this does not validate native UE imports.
internal static class NativeIniTestLayers {
    internal static IReadOnlyList<string> Select(ServerOptions server, int slot) {
        var root = Path.Combine(server.MapProfilePath!, "ini-overrides");
        var rotation = server.Settings.Rotation!;
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(server.MapProfilePath!, "unchained.yaml"))));
        var profiles = (YamlSequenceNode)((YamlMappingNode)yaml.Documents[0].RootNode)["match-rotation"];
        var profile = ((YamlScalarNode)((YamlMappingNode)profiles.Children[slot])["profile"]).Value!;
        var map = rotation.Maps[slot].Split('/').Last().Split('.')[0];
        IEnumerable<string> Matches(string group, string name) {
            var directory = Path.Combine(root, group);
            if (!Directory.Exists(directory)) return [];
            return Directory.GetDirectories(directory)
                .Where(path => Path.GetFileName(path) is var key && (key.EndsWith('~')
                    ? name.StartsWith(key[..^1], StringComparison.OrdinalIgnoreCase)
                    : name.Equals(key, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(path => !Path.GetFileName(path).EndsWith('~'))
                .ThenBy(path => Path.GetFileName(path).Length);
        }
        return Matches("maps", map).Concat(Matches("profiles", profile))
            .Append(Path.Combine(root, "indices", slot.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Where(Directory.Exists).ToArray();
    }

    internal static IniOverrides Compose(ServerOptions server, int slot) => Select(server, slot)
        .Aggregate(server.Ini, (ini, directory) => ini.Merge(IniOverrides.Parse(new IniSource { DirectoryPath = directory })));
}
