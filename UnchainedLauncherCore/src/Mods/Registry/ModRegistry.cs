using log4net;
using System.Runtime.CompilerServices;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncherCore.Mods.Registry
{
    public abstract class ModRegistry
    {
        protected static readonly ILog logger = LogManager.GetLogger(nameof(ModRegistry));

        public const string ModsCacheDir = ".mod_cache";
        public const string EnabledModsCacheDir = $"{ModsCacheDir}\\enabled_mods";
        public const string ModsCachePackageDBDir = $"{ModsCacheDir}\\package_db";
        public const string ModsCachePackageDBPackagesDir = $"{ModsCachePackageDBDir}\\packages";

        public abstract DownloadTask<IEnumerable<DownloadTask<Mod>>> GetAllMods();
        public abstract DownloadTask<string> GetModMetadataString(string modPath);
        public DownloadTask<Mod> GetModMetadata(string modPath) {
            return GetModMetadataString(modPath).ContinueWith(json =>
                // Try to deserialize as V3 first
                JsonHelpers.Deserialize<Mod>(json).RecoverWith(e => {
                    // If that fails, try to deserialize as V2
                    logger.Warn("Falling back to V2 deserialization: " + e?.Message ?? "unknown failure");
                    return JsonHelpers.Deserialize<UnchainedLauncher.Core.JsonModels.Metadata.V2.Mod>(json)
                        .Select(Mod.FromV2);
                }).RecoverWith(e => {
                    // If that fails, try to deserialize as V1
                    logger.Warn("Falling back to V1 deserialization" + e?.Message ?? "unknown failure");
                    return JsonHelpers.Deserialize<UnchainedLauncher.Core.JsonModels.Metadata.V1.Mod>(json)
                        .Select(UnchainedLauncher.Core.JsonModels.Metadata.V2.Mod.FromV1)
                        .Select(Mod.FromV2);
                })
            ).ContinueWith(result => {
                if (result.Success) {
                    return result.Result!;
                } else {
                    logger.Error("Failed to deserialize mod metadata: " + result.Exception?.Message ?? "unknown failure");
                    throw new Exception("Failed to deserialize mod metadata", result.Exception);
                }
            });
        }
    }
}
