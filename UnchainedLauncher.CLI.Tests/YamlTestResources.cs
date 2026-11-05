namespace UnchainedLauncher.CLI.Tests;

internal static class YamlTestResources {
    internal static ServerOptions Server(CliCommandOptions command) => command is ServerGroupOptions group
        ? Xunit.Assert.Single(group.Servers).Options : Xunit.Assert.IsType<ServerOptions>(command);

    internal static CliLaunchOptions Launch(CliCommandOptions command) => command is ServerGroupOptions
        ? Server(command) : Xunit.Assert.IsAssignableFrom<CliLaunchOptions>(command);

    internal static CliLaunchOptions ParseLaunch(string[] args) => Launch(CommandLineOptions.Parse(args));

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
