using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using log4net;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Mods.Registry;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.TypeClasses;
using System;
using LanguageExt.ClassInstances.Pred;
using UnchainedLauncher.Core.Extensions;
using System.Collections.Immutable;
using UnchainedLauncher.Core.Mods.Registry.Resolver;

namespace UnchainedLauncher.Core.Mods
{

    class CoreMods
    {
        public const string GithubBaseURL = "https://github.com";

        public const string EnabledModsCacheDir = $"{FilePaths.ModCachePath}\\enabled_mods";
        public const string ModsCachePackageDBDir = $"{FilePaths.ModCachePath}\\package_db";
        public const string ModsCachePackageDBPackagesDir = $"{ModsCachePackageDBDir}\\packages";
        public const string ModsCachePackageDBListPath = $"{ModsCachePackageDBDir}\\mod_list_index.txt";

        public const string AssetLoaderPluginPath = $"{FilePaths.PluginDir}\\C2AssetLoaderPlugin.dll";
        public const string ServerPluginPath = $"{FilePaths.PluginDir}\\C2ServerPlugin.dll";
        public const string BrowserPluginPath = $"{FilePaths.PluginDir}\\C2BrowserPlugin.dll";

        //public const string AssetLoaderPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin.dll";
        //public const string ServerPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2ServerPlugin/releases/latest/download/C2ServerPlugin.dll";
        //public const string BrowserPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin.dll";

        public const string UnchainedPluginPath = $"{FilePaths.PluginDir}\\UnchainedPlugin.dll";
        public const string UnchainedPluginURL = $"{GithubBaseURL}/Chiv2-Community/UnchainedPlugin/releases/latest/download/UnchainedPlugin.dll";

        public const string PackageDBBaseUrl = "https://raw.githubusercontent.com/Chiv2-Community/C2ModRegistry/db/package_db";
        public const string PackageDBPackageListUrl = $"{PackageDBBaseUrl}/mod_list_index.txt";
    }

    public class ModManager : IModManager {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModManager));

        private IModManager AsIModManager => (IModManager)this;

        public ObservableCollection<Release> EnabledModReleases { get; }

        public HashMap<IModRegistry, IEnumerable<Mod>> ModMap { get; private set; }

        public IEnumerable<Mod> Mods => ModMap.Values.Flatten();

        public ModManager(
            HashMap<IModRegistry, IEnumerable<Mod>> modMap,
            List<Release> enabledMods) {
            ModMap = modMap;
            EnabledModReleases = new ObservableCollection<Release>(enabledMods);
        }

        public static ModManager ForRegistries(params IModRegistry[] registries) {


            var loadReleaseMetadata = (string path) => {
                logger.Info("Loading release metadata from " + path);
                if (!File.Exists(path)) {
                    logger.Warn("Failed to find metadata file: " + path);
                    return null;
                }

                var s = File.ReadAllText(path);

                var deserializationResult =
                    JsonHelpers.Deserialize<Release>(s).RecoverWith(ex => {
                        logger.Warn("Falling back to V2 deserialization" + ex?.Message ?? "unknown failure");
                        return JsonHelpers.Deserialize<JsonModels.Metadata.V2.Release>(s)
                                .Select(Release.FromV2);
                    }).RecoverWith(ex => {
                        logger.Warn("Falling back to V1 deserialization" + ex?.Message ?? "unknown failure");
                        return JsonHelpers.Deserialize<JsonModels.Metadata.V1.Release>(s)
                            .Select(JsonModels.Metadata.V2.Release.FromV1)
                            .Select(Release.FromV2);
                    });

                if (!deserializationResult.Success) {
                    logger.Error("Failed to deserialize metadata file " + path + " " + deserializationResult.Exception?.Message);
                    return null;
                }

                return deserializationResult.Result;
            };

            var enabledModReleases = new List<Release>();

            if (Directory.Exists(CoreMods.EnabledModsCacheDir)) {
                // List everything in the EnabledModsCacheDir and its direct subdirs, then deserialize and filter out any failures (null)
                enabledModReleases =
                    Directory.GetDirectories(CoreMods.EnabledModsCacheDir)
                        .SelectMany(Directory.GetFiles)
                        .Select(loadReleaseMetadata)
                        .ToList();
            }

            var registryMap = 
                new HashMap<IModRegistry, IEnumerable<Mod>>(
                    registries.ToList().Map(r => (r, (IEnumerable<Mod>)new List<Mod>()))
                );


            return new ModManager(
                registryMap,
                new List<Release>(enabledModReleases)
            );
        }

        public EitherAsync<Error, Unit> DisableModRelease(Release release) {
            if (!EnabledModReleases.Contains(release))
                return EitherAsync<Error, Unit>.Right(default);

            return Prelude.Try<Unit>(() => {
                logger.Info("Disabling mod release: " + release.Manifest.Name + " " + release.Tag);

                // Should be doing this when all downloads get done, but cba to do it better right now.
                FileHelpers.DeleteFile(FilePaths.PakDir + "\\" + release.PakFileName);

                var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);
                var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
                var metadataFilePath = orgPath + "\\" + urlParts.Last() + ".json";
                FileHelpers.DeleteFile(metadataFilePath);

                EnabledModReleases.Remove(release);
                return default;
            }).ToAsync().ToEither();
        }

        public EitherAsync<Error, Unit> EnableModRelease(Release release, Option<IProgress<double>> progress, CancellationToken cancellationToken) {
            var associatedMod = Mods.First(Mods => Mods.Releases.Contains(release));
            var maybeCurrentlyEnabledRelease = AsIModManager.GetCurrentlyEnabledReleaseForMod(associatedMod);

            var whenNone = () => {
                logger.Info($"Enabling {release.Manifest.Name} version {release.Tag}");
                return EitherAsync<Error, Unit>.Right(default);
            };

            var whenSome = (Release currentlyEnabledRelease) => {
                logger.Info($"Changing {currentlyEnabledRelease.Manifest.Name} from version {currentlyEnabledRelease.Tag} to version {release.Tag}");
                return DisableModRelease(currentlyEnabledRelease);
            };

            return
                maybeCurrentlyEnabledRelease
                    .Match(None: whenNone, Some: whenSome)
                    .Bind(_ => DownloadModRelease(release, progress, cancellationToken))
                    .Tap(_ => EnabledModReleases.Add(release));
        }

        public async Task<GetAllModsResult> UpdateModsList() {

            static Unit logResults(IModRegistry registry, GetAllModsResult result) {
                logger.Info($"Got {result.Mods.Count()} mods from registry {registry.Name}");

                if (result.Errors.Any())
                    logger.Error($"Got {result.Errors.Count()} exceptions from registry {registry.Name}");

                result.Errors.ToList().ForEach(exception => logger.Error(exception));
                return default;
            }

            logger.Info("Updating mods list...");

            var registries = ModMap.Keys;

            var result =
                await registries
                    .Map(async registry => (registry, result: await registry.GetAllMods()))
                    .SequenceParallel()
                    .Map(resultPairs => {
                        return 
                            resultPairs
                                .Map(tuple => {
                                    var registry = tuple.registry;
                                    var allModsResult = tuple.result;

                                    logResults(registry, allModsResult);
                                    ModMap.AddOrUpdate(registry, allModsResult.Mods);
                                    return allModsResult;
                                })
                                .Aggregate((l, r) => l + r);
                    });

            logger.Info($"Got a total of {result.Mods.Count()} mods from all registries");

            return result;
        }

        public EitherAsync<Error, Unit> DownloadModFiles(bool downloadPlugin) {
            logger.Info("Downloading mod files...");
            logger.Info("Creating mod diretories...");
            Directory.CreateDirectory(CoreMods.EnabledModsCacheDir);
            Directory.CreateDirectory(CoreMods.ModsCachePackageDBDir);
            Directory.CreateDirectory(CoreMods.ModsCachePackageDBPackagesDir);
            Directory.CreateDirectory(FilePaths.PluginDir);

            var DeprecatedLibs = new List<string>()
            {
                CoreMods.AssetLoaderPluginPath,
                CoreMods.ServerPluginPath,
                CoreMods.BrowserPluginPath
            };

            foreach (var depr in DeprecatedLibs)
                FileHelpers.DeleteFile(depr);

            var tryDownload =
                Prelude.TryAsync(() =>
                    downloadPlugin
                        ? HttpHelpers.DownloadFileAsync(CoreMods.UnchainedPluginURL, CoreMods.UnchainedPluginPath).Task
                        : Task.CompletedTask
                );

            return
                tryDownload
                    .ToEither()
                    .Bind(_ =>
                        EnabledModReleases
                            .ToList()
                            .Select(r => DownloadModRelease(r, Option<IProgress<double>>.None, CancellationToken.None))
                            .SequenceParallel()
                     )
                    .Select(_ => default(Unit)); // discard the results because they're all Unit

        }

        private EitherAsync<Error, Unit> DownloadModRelease(Release release, Option<IProgress<double>> progress, CancellationToken token) {
            var outputPath = FilePaths.PakDir + "\\" + release.PakFileName;

            EitherAsync<DownloadModFailure, FileWriter> prepareDownload(IModRegistry registry) {
                return
                    FileHelpers
                        .Sha512Async(outputPath)
                        .MapLeft(err => new DownloadModFailure.HashFailureWrapper(err).Widen)
                        .Bind(hash => {
                            if (hash == release.ReleaseHash)
                                return Prelude.RightAsync(default(Unit));
                            else
                                return Prelude.LeftAsync(new DownloadModFailure.HashMismatch(release, hash).Widen);
                        })
                        .Tap(_ => logger.Info($"Downloading {release.Manifest.Name} {release.Tag} to {FilePaths.PakDir}/{release.PakFileName}"))
                        .Bind(_ => registry.DownloadPak(release, outputPath + ".tmp"));
            }

            async Task<EitherAsync<Error, Unit>> completeDownload(FileWriter fileWriter) {
                await fileWriter.WriteAsync(progress, token);
                var shaHash = FileHelpers.Sha512(outputPath + ".tmp");

                if (shaHash != release.ReleaseHash) {
                    return Prelude.Try(() => FileHelpers.DeleteFile(outputPath + ".tmp"))
                        .ToAsync()
                        .ToEither()
                        .Bind(_ => EitherAsync<Error, Unit>.Left(
                            Error.New($"Downloaded pak file hash for {release.Manifest.Name} {release.Tag} does not match expected hash. Expected: {release.ReleaseHash} Got: {shaHash}")
                        ));
                }

                return Prelude.Try(() => File.Move(outputPath + ".tmp", outputPath))
                    .ToAsync()
                    .ToEither()
                    .Bind(_ => EitherAsync<Error, Unit>.Right(default));
            }


            Option<IModRegistry> maybeResult = GetRegistryForRelease(release);


            return maybeResult
                .ToEitherAsync(() => Error.New($"Failed to find mod release {release.Manifest.Name} {release.Tag}"))
                .Bind(registry => prepareDownload(registry))
                .BindAsync(fileWriter => completeDownload(fileWriter))
                .Map(fileWriter => logger.InfoUnit($"Successfully downloaded mod release {release.Manifest.Name} {release.Tag}"))
                .Bind(_ => SaveEnabledReleaseMetadata(release))
                .Map(_ => logger.InfoUnit($"Successfully enabled mod release {release.Manifest.Name} {release.Tag}"));
        }

        private Option<IModRegistry> GetRegistryForRelease(Release release) {
            var result =
                ModMap.ToHashMap()
                    .Keys
                    .Select(registry =>
                        ModMap[registry]
                            .Bind(v => v.Releases)
                            .Find(r => r == release)
                            .Map(_ => registry)
                    ).FirstOrDefault();

            if (result == null)
                return Option<IModRegistry>.None;

            return result;
        }

        private static 
        private static EitherAsync<Error, Unit> SaveEnabledReleaseMetadata(Release release) {
            var enabledModJson = JsonConvert.SerializeObject(release);
            var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);

            var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
            var filePath = orgPath + "\\" + urlParts.Last() + ".json";

            return Prelude.TryAsync(
                Task.Run(async () => {
                    logger.Info("Writing enabled mod json metadata to " + filePath);
                    Directory.CreateDirectory(orgPath);
                    await File.WriteAllTextAsync(filePath, enabledModJson);
                    return default(Unit);
                }))
                .ToEither()
                .Tap(_ => logger.Info("Successfully wrote enabled mod json metadata to " + filePath))
                .MapLeft(err =>
                    Error.New(
                        "Failed to write enabled mod json metadata to " + filePath + " " + err.Message,
                        err
                    )
                );
        }
    }

    public abstract record DownloadModFailure {
        private DownloadModFailure() { }
        public record ModPakStreamAcquisitionFailureWrapper(ModPakStreamAcquisitionFailure Failure) : DownloadModFailure;
        public record HashFailureWrapper(HashFailure Failure) : DownloadModFailure;
        public record HashMismatch(Release release, string InvalidHash) : DownloadModFailure;

        public DownloadModFailure Widen => this;

    }
    
}
