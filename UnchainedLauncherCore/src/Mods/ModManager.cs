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
using UnchainedLauncher.Core.Logging;
using UnchainedLauncher.Core.LanguageExt;
using LanguageExt.ClassInstances.Pred;

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

    public class ModManager {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModManager));

        public ObservableCollection<Release> EnabledModReleases { get; }

        public Dictionary<ModRegistry, IEnumerable<Mod>> ModMap { get; }

        public IEnumerable<Mod> Mods => ModMap.Values.Flatten();
        public ModRegistry ModRegistry { get; }

        public ModManager(
            Dictionary<ModRegistry, IEnumerable<Mod>> modMap,
            ObservableCollection<Release> enabledMods) {
            ModMap = modMap;
            EnabledModReleases = enabledMods;
        }

        public static ModManager ForRegistries(params ModRegistry[] registries) {


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

            var registryMap = new Dictionary<ModRegistry, IEnumerable<Mod>>();

            registries.ToList().ForEach(registry =>
                registryMap.Add(registry, new List<Mod>())
            );


            return new ModManager(
                registryMap,
                new ObservableCollection<Release>(enabledModReleases!)
            );
        }

        public Release? GetCurrentlyEnabledReleaseForMod(Mod mod) {
            return EnabledModReleases.FirstOrDefault(x => x.Manifest.RepoUrl == mod.LatestManifest.RepoUrl);
        }

        public void DisableModRelease(Release release) {
            logger.Info("Disabling mod release: " + release.Manifest.Name + " " + release.Tag);

            // Should be doing this when all downloads get done, but cba to do it better right now.
            FileHelpers.DeleteFile(FilePaths.PakDir + "\\" + release.PakFileName);

            var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);
            var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
            var metadataFilePath = orgPath + "\\" + urlParts.Last() + ".json";
            FileHelpers.DeleteFile(metadataFilePath);

            EnabledModReleases.Remove(release);
        }

        public EitherAsync<Error, Unit> EnableModRelease(Release release, Option<IProgress<double>> progress, CancellationToken cancellationToken) {
            logger.Info("Enabling mod release: " + release.Manifest.Name + " " + release.Tag);
            var associatedMod = Mods.First(Mods => Mods.Releases.Contains(release));
            var currentlyEnabledRelease = GetCurrentlyEnabledReleaseForMod(associatedMod);
            if (currentlyEnabledRelease != null) {
                logger.Info("Disabling currently enabled release: " + currentlyEnabledRelease.Manifest.Name + " " + currentlyEnabledRelease.Tag);
                DisableModRelease(currentlyEnabledRelease);
            }

            EnabledModReleases.Add(release);
            return DownloadModRelease(release, progress, cancellationToken);
        }

        public async Task<(IEnumerable<RegistryMetadataException>, IEnumerable<Mod>)> UpdateModsList() {
            logger.Info("Updating mods list...");

            logger.Info($"Downloading mod list from registry {ModRegistry}");

            var registries = ModMap.Keys;

            var result =
                await registries
                    .Select(registry => (registry, registry.GetAllMods()))
                    .Select(async tuple => {
                        var (registry, getAllModsTask) = tuple;
                        var (exceptions, mods) = await getAllModsTask;
                        logger.Info($"Got {mods.Count()} mods from registry {registry}");

                        if (exceptions.Any())
                            logger.Error($"Got {exceptions.Count()} exceptions from registry {registry.Name}");

                        exceptions.ToList().ForEach(exception => logger.Error(exception));
                        ModMap.Add(registry, mods);
                        return (exceptions, mods);
                    })
                    .SequenceSerial()
                    .Select(results =>
                        results.Aggregate((l, r) => (
                            l.exceptions.Concat(r.exceptions),
                            l.mods.Concat(r.mods)
                        ))
                    );



            logger.Info($"Got a total of   {result.mods.Count()} mods from all registries");

            return result;
        }

        public IEnumerable<Tuple<Release, Release>> GetUpdateCandidates() {
            return Mods
                .Select(mod => new Tuple<Release?, Release?>(mod.LatestRelease, GetCurrentlyEnabledReleaseForMod(mod))) // Get the currently enabled release, as well as the latest mod release
                .Where(Release => Release.Item2 != null && Release.Item1 != null) // Filter out mods that aren't enabled
                .Where(tuple => tuple.Item1!.Version.ComparePrecedenceTo(tuple.Item2!.Version) > 0) // Filter out older releases
                .Select(tuple => new Tuple<Release, Release>(tuple.Item1!, tuple.Item2!)); // Get the latest release
        }

        public EitherAsync<Error,Unit> DownloadModFiles(bool downloadPlugin) {
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
                    .Select(_ => Unit.Default); // discard the results because they're all Unit

        }

        private EitherAsync<Error, Unit> DownloadModRelease(Release release, Option<IProgress<double>> progress, CancellationToken token) {
            var outputPath = FilePaths.PakDir + "\\" + release.PakFileName;

            Option<ModRegistry> maybeResult = GetRegistryForRelease(release);

            return maybeResult
                .ToEitherAsync(() => Error.New($"Failed to find mod release {release.Manifest.Name} {release.Tag}"))
                .Bind(registry => PrepareDownload(registry, release, progress, outputPath))
                .BindAsync(fileWriter => CompleteDownload(release, progress, fileWriter, outputPath, token))
                .Map(fileWriter => logger.InfoUnit($"Successfully downloaded mod release {release.Manifest.Name} {release.Tag}"))
                .Bind(_ => SaveEnabledReleaseMetadata(release))
                .Map(_ => logger.InfoUnit($"Successfully enabled mod release {release.Manifest.Name} {release.Tag}"));
        }

        private Option<ModRegistry> GetRegistryForRelease(Release release) {
            var result = 
                ModMap.ToHashMap()
                    .Keys
                    .Select(registry =>
                        ModMap[registry]
                            .Bind(v => v.Releases)
                            .Find(r => r == release)
                            .Map(_ => registry)
                    ).FirstOrDefault();

            if(result == null)
                return Option<ModRegistry>.None;

            return result;
        }

        private static async Task<EitherAsync<Error, Unit>> CompleteDownload(Release release, Option<IProgress<double>> progress, FileWriter fileWriter, string outputPath, CancellationToken token) {
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
                .Bind(_ => EitherAsync<Error, Unit>.Right(Unit.Default));
        }

        private static EitherAsync<Error, FileWriter> PrepareDownload(ModRegistry registry, Release release, Option<IProgress<double>> progress, string outputPath) {

            Func<bool, EitherAsync<Error, Unit>> checkExistingFileHash = (bool exists) => {
                if (!exists)
                    return EitherAsync<Error, Unit>.Right(Unit.Default);

                return Prelude.Try(() => FileHelpers.Sha512(outputPath))
                    .ToAsync()
                    .ToEither()
                    .Bind(shaHash => {
                        if (shaHash == release.ReleaseHash) {
                            progress.IfSome(p => p.Report(100));
                            // We're successful and we need to do nothing, so return a successful task with an empty result
                            return EitherAsync<Error, Unit>.Left(Error.New($"Already downloaded {release.Manifest.Name} {release.Tag}. Skipping"));
                        }

                        return EitherAsync<Error, Unit>.Right(Unit.Default);
                    });
            };

            var prepareDownloadFileWriter = () => {
                logger.Info($"Downloading {release.Manifest.Name} {release.Tag} to {FilePaths.PakDir}/{release.PakFileName}");
                return registry.DownloadPak(release, outputPath + ".tmp");
            };

            return 
                Prelude.Try(File.Exists(outputPath))
                    .ToAsync()
                    .ToEither()
                    .BindTap(checkExistingFileHash)
                    .Bind(_ => prepareDownloadFileWriter());
        }

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
                    return Unit.Default;
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
}
