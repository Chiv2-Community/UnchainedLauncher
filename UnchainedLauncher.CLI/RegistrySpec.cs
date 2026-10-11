using System.Text.RegularExpressions;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.CLI;

public sealed record RegistrySpec(string Kind, string Location) {
    public static RegistrySpec Parse(string value) {
        var parts = value.Split(':', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
            throw new ArgumentException("Registry must be github:owner/repo, local:<directory>, or config:<json-file>.");
        var kind = parts[0].ToLowerInvariant();
        if (kind == "github") {
            if (!Regex.IsMatch(parts[1], @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z"))
                throw new ArgumentException("GitHub registry must be github:owner/repo.");
            return new RegistrySpec(kind, parts[1]);
        }
        if (kind is "local" or "config") return new RegistrySpec(kind, Path.GetFullPath(parts[1]));
        throw new ArgumentException($"Unsupported registry kind: {kind}");
    }

    public IModRegistry Create() => Kind switch {
        "github" => new GithubModRegistry(Location.Split('/')[0], Location.Split('/')[1]),
        "local" => Directory.Exists(Location) ? new LocalModRegistry(Location)
            : throw new DirectoryNotFoundException($"Local registry directory does not exist: {Location}"),
        "config" => LoadConfiguration(),
        _ => throw new ArgumentException($"Unsupported registry kind: {Kind}")
    };

    private IModRegistry LoadConfiguration() {
        // Core resolves relative local paths against the current directory. Resolve them
        // against the configuration's directory instead, without changing process CWD.
        var result = Core.Utilities.JsonHelpers.Deserialize<ModRegistryMetadata>(File.ReadAllText(Location));
        if (!result.Success) throw new ArgumentException($"Invalid registry configuration: {Location}", result.Exception);
        return ModRegistryCodec.ToClassType(ResolvePaths(result.Result!, Path.GetDirectoryName(Location)!));
    }

    private static ModRegistryMetadata ResolvePaths(ModRegistryMetadata metadata, string directory) => metadata switch {
        LocalModRegistryMetadata local => local with { PakReleaseDir = Path.GetFullPath(local.PakReleaseDir, directory) },
        AggregateModRegistryMetadata aggregate => aggregate with {
            Registries = aggregate.Registries.Select(r => ResolvePaths(r, directory)).ToArray()
        },
        _ => metadata
    };
}
