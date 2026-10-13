using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

// A read-only projection of a launch YAML file. Querying requires only registry
// specifications, not a valid installation, credentials, or launch settings.
public sealed class RegistryQueryConfiguration {
    public RegistryQueryModSetup? ModSetup { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? Chiv2Path { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? Platform { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? SavedDirSuffix { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? ServerBrowserBackend { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? UpdatePlugin { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? AllowUnstablePlugin { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? NoTui { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? GameArgs { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? Server { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? Client { get; set; }
}

public sealed class RegistryQueryModSetup {
    public List<string>? Registries { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? Mods { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))] public object? Dlls { get; set; }
}