using LanguageExt;
using LanguageExt.ClassInstances.Pred;
using LanguageExt.Common;
using LanguageExt.TypeClasses;
using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods {

    class CoreMods {
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

        public EitherAsync<DisableModFailure, Unit> DisableModRelease(Release release) {
            if (!EnabledModReleases.Contains(release))
                return EitherAsync<DisableModFailure, Unit>.Left(DisableModFailure.ModNotEnabled(release));

            logger.Info("Disabling mod release: " + release.Manifest.Name + " " + release.Tag);

            var pakLocation = FilePaths.PakDir + "\\" + release.PakFileName;

            var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);
            var orgPath = Path.Combine(CoreMods.EnabledModsCacheDir, release.Manifest.Organization);
            var metadataFilePath = Path.Combine(orgPath, release.Manifest.RepoName + ".json");

            return
                UnchainedEitherExtensions.AttemptAsync(() => FileHelpers.DeleteFile(pakLocation))
                    .MapLeft(err => DisableModFailure.DeleteFailed(pakLocation, err))
                    .Tap(_ => logger.Info("Successfully deleted mod pak for " + release.Manifest.Name + " " + release.Tag))
                    .Bind(_ =>
                        UnchainedEitherExtensions.AttemptAsync(() => FileHelpers.DeleteFile(metadataFilePath))
                            .MapLeft(err => DisableModFailure.DeleteFailed(metadataFilePath, err))
                    )
                    .Tap(_ => logger.Info("Successfully deleted mod metadata for " + release.Manifest.Name + " " + release.Tag))
                    .Map(_ => default(Unit));
        }

        public EitherAsync<EnableModFailure, Unit> EnableModRelease(Release release, Option<IProgress<double>> progress, CancellationToken cancellationToken) {
            var associatedMod = Mods.First(Mods => Mods.Releases.Contains(release));
            var maybeCurrentlyEnabledRelease = AsIModManager.GetCurrentlyEnabledReleaseForMod(associatedMod);

            var whenNone = () => {
                logger.Info($"Enabling {release.Manifest.Name} version {release.Tag}");
                return EitherAsync<EnableModFailure, Unit>.Right(default);
            };

            var whenSome = (Release currentlyEnabledRelease) => {
                logger.Info($"Changing {currentlyEnabledRelease.Manifest.Name} from version {currentlyEnabledRelease.Tag} to version {release.Tag}");
                return DisableModRelease(currentlyEnabledRelease).MapLeft(EnableModFailure.Wrap);
            };

            return
                maybeCurrentlyEnabledRelease
                    .Match(None: whenNone, Some: whenSome)
                    .Bind(_ => DownloadModRelease(release, progress, cancellationToken).MapLeft(EnableModFailure.Wrap))
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
                                    ModMap = ModMap.AddOrUpdate(registry, allModsResult.Mods);
                                    return allModsResult;
                                })
                                .Aggregate((l, r) => l + r);
                    });

            logger.Info($"Got a total of {result.Mods.Count()} mods from all registries");

            return result;
        }

        private EitherAsync<DownloadModFailure, Unit> DownloadModRelease(Release release, Option<IProgress<double>> progress, CancellationToken token) {
            var outputPath = FilePaths.PakDir + "\\" + release.PakFileName;

            EitherAsync<DownloadModFailure, FileWriter> prepareDownload(IModRegistry registry) {
                logger.Info($"Downloading {release.Manifest.Name} {release.Tag} to {FilePaths.PakDir}/{release.PakFileName}");

                // If the file already exists and has the correct hash, return early with an "AlreadyDownloaded" error
                // Otherwise, prepare a FileWriter to download the pak file
                return
                    FileHelpers
                        .Sha512Async(outputPath)
                        .MapLeft(DownloadModFailure.Wrap)
                        .Map(hash => hash.Contains(release.ReleaseHash)) // If the hash is correct already, return true. We already have this file.
                        .Bind(isHashCorrect =>
                            isHashCorrect
                                ? EitherAsync<DownloadModFailure, FileWriter>.Left(DownloadModFailure.AlreadyDownloaded(release))
                                : registry.DownloadPak(release, outputPath + ".tmp").MapLeft(DownloadModFailure.Wrap)
                        );
            }

            EitherAsync<DownloadModFailure, Unit> completeDownload(FileWriter fileWriter) {
                return fileWriter
                    .WriteAsync(progress, token)
                    .MapLeft(err => DownloadModFailure.WriteFailed(fileWriter.FilePath, err))
                    .Bind(_ => FileHelpers.Sha512Async(fileWriter.FilePath).MapLeft(DownloadModFailure.Wrap))
                    .Bind(hash =>
                        (hash.Contains(release.ReleaseHash)).ToEither(
                            True: () => default(Unit),
                            False: () => DownloadModFailure.HashMismatch(release, hash)
                        ).ToAsync()
                    ).Bind(_ =>
                        UnchainedEitherExtensions
                            .AttemptAsync(() => File.Move(fileWriter.FilePath, outputPath))
                            .MapLeft(err => DownloadModFailure.WriteFailed(outputPath, err))
                    );
            }

            EitherAsync<DownloadModFailure, Unit> saveEnabledReleaseMetadata() {
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
                    .MapLeft(err => DownloadModFailure.WriteFailed(filePath, err));
            }


            Option<IModRegistry> maybeResult = GetRegistryForRelease(release);


            return maybeResult
                .ToEitherAsync(() => DownloadModFailure.ModNotFound(release))
                .Bind(prepareDownload)
                .Bind(completeDownload)
                .BindLeft(error => {
                    // If the download failed because the file was already downloaded, discard the failure
                    if (error is DownloadModFailure.AlreadyDownloadedFailure)
                        return EitherAsync<DownloadModFailure, Unit>.Right(default);

                    return EitherAsync<DownloadModFailure, Unit>.Left(error);
                })
                .Tap(_ => logger.InfoUnit($"Successfully downloaded mod release {release.Manifest.Name} {release.Tag}"))
                .Bind(_ => saveEnabledReleaseMetadata())
                .Tap(_ => logger.InfoUnit($"Successfully enabled mod release {release.Manifest.Name} {release.Tag}"));
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
    }
}