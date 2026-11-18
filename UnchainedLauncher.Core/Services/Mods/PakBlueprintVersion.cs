using Semver;
using UnchainedLauncher.UnrealModScanner.PakScanning;

namespace UnchainedLauncher.Core.Services.Mods;

internal static class PakBlueprintVersion {

    internal static string? Latest(IEnumerable<string> versions) => versions
        .Select(value => value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? value[1..] : value)
        .Select(value => SemVersion.TryParse(value, SemVersionStyles.Strict, out var version) ? version : null)
        .OfType<SemVersion>()
        .OrderByDescending(version => version, SemVersion.PrecedenceComparer)
        .FirstOrDefault()?.ToString();

    internal static async Task<string?> Read(string path) => Latest(await BlueprintVersionScanner.ReadVersions(path));
}