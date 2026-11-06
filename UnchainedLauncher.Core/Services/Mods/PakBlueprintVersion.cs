using log4net;
using Semver;
using UnchainedLauncher.UnrealModScanner.PakScanning;

namespace UnchainedLauncher.Core.Services.Mods;

internal static class PakBlueprintVersion {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(PakBlueprintVersion));

    internal static string? Latest(IEnumerable<string> versions) => versions
        .Select(value => value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? value[1..] : value)
        .Select(value => SemVersion.TryParse(value, SemVersionStyles.Strict, out var version) ? version : null)
        .OfType<SemVersion>()
        .OrderByDescending(version => version, SemVersion.PrecedenceComparer)
        .FirstOrDefault()?.ToString();

    internal static async Task<string?> Read(string path) {
        try {
            return Latest(await BlueprintVersionScanner.ReadVersions(path));
        }
        catch (Exception error) {
            Logger.Warn($"Could not read Blueprint versions from {path}: {error.Message}");
            return null;
        }
    }
}