using CUE4Parse.UE4.Versions;
using log4net;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;
using UnchainedLauncher.UnrealModScanner.Utility;

namespace UnchainedLauncher.UnrealModScanner.PakScanning;

public static class BlueprintVersionScanner {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(BlueprintVersionScanner));

    public static Task<IReadOnlyList<string>> ReadVersions(string pakPath) => Task.Run<IReadOnlyList<string>>(() => {
        var options = GameScanOptions.Chivalry2;
        var path = Path.GetFullPath(pakPath);
        using var stream = File.OpenRead(path);
        using var provider = new FilteredFileProvider(Path.GetDirectoryName(path)!, SearchOption.TopDirectoryOnly,
            options.ScanFilter.CreatePakContentFilter(), options.AesKey,
            new VersionContainer(options.GameVersion));
        provider.InitializePak(path, stream);
        ModScanOrchestrator.InitializeCompression(provider);
        var versions = new List<string>();
        foreach (var file in provider.FilteredFiles) {
            try {
                var package = provider.LoadPackage(file.Key);
                foreach (var blueprint in PackageUtility.GetClassDefaultObjects(package)) {
                    foreach (var property in blueprint.Properties.Where(property => property.Name.Text.Contains("Version", StringComparison.OrdinalIgnoreCase))) {
                        var value = property.Tag?.GenericValue?.ToString();
                        if (value != null) versions.Add(value);
                    }
                }
            }
            catch (Exception error) {
                Logger.Warn($"Could not read Blueprint versions from {file.Key}: {error.Message}");
            }
        }
        return versions;
    });
}