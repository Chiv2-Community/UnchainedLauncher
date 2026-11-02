namespace UnchainedLauncher.CLI.Tests;

internal static class YamlTestResources {
    internal static string Read(string name, params (string Token, string Value)[] replacements) {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-resources", "yaml-tests", name + ".yaml"));
        foreach (var (token, value) in replacements) text = text.Replace("__" + token + "__", value, StringComparison.Ordinal);
        return text;
    }

    internal static string Write(string directory, string name, params (string Token, string Value)[] replacements) {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "launch.yaml");
        File.WriteAllText(path, Read(name, replacements));
        return path;
    }
}
