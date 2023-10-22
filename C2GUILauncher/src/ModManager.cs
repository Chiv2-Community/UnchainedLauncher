using C2GUILauncher.JsonModels.Metadata.V3;
using log4net;
using Newtonsoft.Json;
using Semver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace C2GUILauncher.Mods {

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
        public const string UnchainedPluginLatestURL = $"{GithubBaseURL}/Chiv2-Community/UnchainedPlugin/releases/latest";
        public const string UnchainedPluginURL = $"{UnchainedPluginLatestURL}/download/UnchainedPlugin.dll";

        public const string PackageDBBaseUrl = "https://raw.githubusercontent.com/Chiv2-Community/C2ModRegistry/db/package_db";
        public const string PackageDBPackageListUrl = $"{PackageDBBaseUrl}/mod_list_index.txt";
    }

    public class ModManager {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModManager));

        public string RegistryOrg { get; }
        public string RegistryRepoName { get; }
        public ObservableCollection<Mod> Mods { get; }
        public ObservableCollection<Release> EnabledModReleases { get; }

        public ObservableCollection<ModReleaseDownloadTask> PendingDownloads { get; }
        public ObservableCollection<ModReleaseDownloadTask> FailedDownloads { get; }

        private string PakDir { get; }

        public ModManager(
            string registryOrg,
            string registryRepoName,
            string pakDir,
            ObservableCollection<Mod> baseModList,
            ObservableCollection<Release> enabledMods,
            ObservableCollection<ModReleaseDownloadTask> pendingDownloads,
            ObservableCollection<ModReleaseDownloadTask> failedDownloads) {
            RegistryOrg = registryOrg;
            RegistryRepoName = registryRepoName;
            PakDir = pakDir;
            Mods = baseModList;
            EnabledModReleases = enabledMods;
            PendingDownloads = pendingDownloads;
            FailedDownloads = failedDownloads;
        }

        public static ModManager ForRegistry(string registryOrg, string registryRepoName, string pakDir) {


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
                        return JsonHelpers.Deserialize<JsonModels.Metadata.V2.Release>(s).Select(Release.FromV2);
                    }).RecoverWith(ex => {
                        logger.Warn("Falling back to V1 deserialization" + ex?.Message ?? "unknown failure");
                        return JsonHelpers.Deserialize<JsonModels.Metadata.V1.Release>(s).Select(JsonModels.Metadata.V2.Release.FromV1).Select(Release.FromV2);
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


            return new ModManager(
                registryOrg,
                registryRepoName,
                pakDir,
                new ObservableCollection<Mod>(),
                new ObservableCollection<Release>(enabledModReleases!),
                new ObservableCollection<ModReleaseDownloadTask>(),
                new ObservableCollection<ModReleaseDownloadTask>()
            );
        }

        public Release? GetCurrentlyEnabledReleaseForMod(Mod mod) {
            return EnabledModReleases.FirstOrDefault(x => x.Manifest.RepoUrl == mod.LatestManifest.RepoUrl);
        }

        public void DisableModRelease(Release release) {
            logger.Info("Disabling mod release: " + release.Manifest.Name + " " + release.Tag);

            // Should be doing this when all downloads get done, but cba to do it better right now.
            FileHelpers.DeleteFile(PakDir + "\\" + release.PakFileName);

            var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);
            var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
            var metadataFilePath = orgPath + "\\" + urlParts.Last() + ".json";
            FileHelpers.DeleteFile(metadataFilePath);

            EnabledModReleases.Remove(release);
        }

        public ModReleaseDownloadTask EnableModRelease(Release release) {
            logger.Info("Enabling mod release: " + release.Manifest.Name + " " + release.Tag);
            var associatedMod = this.Mods.First(Mods => Mods.Releases.Contains(release));
            var currentlyEnabledRelease = GetCurrentlyEnabledReleaseForMod(associatedMod);
            if (currentlyEnabledRelease != null) {
                logger.Info("Disabling currently enabled release: " + currentlyEnabledRelease.Manifest.Name + " " + currentlyEnabledRelease.Tag);
                DisableModRelease(currentlyEnabledRelease);
            }

            this.EnabledModReleases.Add(release);
            return DownloadModRelease(release);
        }

        public async Task UpdateModsList() {
            logger.Info("Updating mods list...");
            Mods.Clear();

            logger.Info($"Downloading mod list from registry {RegistryOrg}/{RegistryRepoName}");
            await HttpHelpers.DownloadFileAsync(CoreMods.PackageDBPackageListUrl, CoreMods.ModsCachePackageDBListPath).Task;

            var packageListString = File.ReadAllText(CoreMods.ModsCachePackageDBListPath);
            var packageNameToMetadataPath = (String s) => $"{CoreMods.PackageDBBaseUrl}/packages/{s}.json";
            var packageNameToFilePath = (String s) => $"{CoreMods.ModsCachePackageDBPackagesDir}\\{s}.json";

            var packages = packageListString.Split("\n").Where(s => s.Length > 0);

            logger.Info($"Found {packages.Count()} packages listed...");

            var downloadTasks = packages
                .Select(packageName => HttpHelpers.DownloadFileAsync(packageNameToMetadataPath(packageName), packageNameToFilePath(packageName)))
                .Select(async downloadTask => {
                    await downloadTask.Task;
                    var fileLocation = downloadTask.Target.OutputPath!;

                    var jsonString = await File.ReadAllTextAsync(fileLocation);
                    var deserializationResult =
                        JsonHelpers
                            .Deserialize<Mod>(jsonString)
                            .RecoverWith(e => {
                                logger.Warn("Falling back to V2 deserialization: " + e?.Message ?? "unknown failure");
                                return JsonHelpers.Deserialize<JsonModels.Metadata.V2.Mod>(jsonString).Select(Mod.FromV2);
                            })
                            .RecoverWith(e => {
                                logger.Warn("Falling back to V1 deserialization" + e?.Message ?? "unknown failure");
                                return JsonHelpers.Deserialize<JsonModels.Metadata.V1.Mod>(jsonString).Select(JsonModels.Metadata.V2.Mod.FromV1).Select(Mod.FromV2);
                            });

                    if(deserializationResult.Exception != null)
                        logger.Error("Failed to deserialize mod metadata file " + fileLocation, deserializationResult.Exception);

                    if (deserializationResult.Success) {
                        Mods.Add(deserializationResult.Result!);
                    } else {
                        logger.Error("Failed to deserialize mod metadata file " + fileLocation, deserializationResult.Exception);
                    }
                });

            await Task.WhenAll(downloadTasks);
        }

        public IEnumerable<Tuple<Release, Release>> GetUpdateCandidates() {
            return Mods
                .Select(mod => new Tuple<Release?, Release?>(mod.LatestRelease, GetCurrentlyEnabledReleaseForMod(mod))) // Get the currently enabled release, as well as the latest mod release
                .Where(Release => Release.Item2 != null && Release.Item1 != null) // Filter out mods that aren't enabled
                .Where(tuple => tuple.Item1!.Version.ComparePrecedenceTo(tuple.Item2!.Version) > 0) // Filter out older releases
                .Select(tuple => new Tuple<Release,Release>(tuple.Item1!, tuple.Item2!)); // Get the latest release
        }

        // TODO: Delete this method in 1.x
        public async Task<IEnumerable<ModReleaseDownloadTask>> DownloadModFiles(bool checkForPluginUpdates, Window window) {
            logger.Info("Downloading mod files...");

            logger.Info("Creating mod diretories...");
            Directory.CreateDirectory(CoreMods.EnabledModsCacheDir);
            Directory.CreateDirectory(CoreMods.ModsCachePackageDBDir);
            Directory.CreateDirectory(CoreMods.ModsCachePackageDBPackagesDir);
            Directory.CreateDirectory(FilePaths.PluginDir);

            var DeprecatedLibs = new List<String>()
            {
                CoreMods.AssetLoaderPluginPath,
                CoreMods.ServerPluginPath,
                CoreMods.BrowserPluginPath
            };

            foreach (var depr in DeprecatedLibs)
                FileHelpers.DeleteFile(depr);


            ModReleaseDownloadTask? unchainedPluginDownloadTask = null;

            if (checkForPluginUpdates) {
                var version = await CheckForUnchainedPluginUpdates(window);

                if (version != null) {

                    var target = new DownloadTarget(CoreMods.UnchainedPluginURL, CoreMods.UnchainedPluginPath);
                    var downloadTask = 
                        HttpHelpers.DownloadFileAsync(target.Url, target.OutputPath!)
                            .ContinueWith(() => File.WriteAllText(FilePaths.UnchainedPluginVersionPath, version.ToString()));

                    var unchainedPluginModReleaseDownloadTask =
                        new ModReleaseDownloadTask(
                            // Stubbing this object out so the task can be displayed with the normal mod download tasks
                            new Release(
                                "latest",
                                "",
                                "",
                                DateTime.Now,
                                new ModManifest(
                                    "",
                                    downloadTask.Target.OutputPath!.Split("/").Last(),
                                    "",
                                    "",
                                    "",
                                    ModType.Shared,
                                    new List<string>(),
                                    new List<Dependency>(),
                                    new List<ModTag>(),
                                    new List<string>(),
                                    new OptionFlags(false)
                                )
                            ),
                            downloadTask
                        );
                }
            }

            var tasks = EnabledModReleases.ToList().Select(DownloadModRelease);

            var unchainedPluginDownloadTaskAsList = 
                unchainedPluginDownloadTask != null 
                    ? new List<ModReleaseDownloadTask>() { unchainedPluginDownloadTask } 
                    : new List<ModReleaseDownloadTask>();

            return tasks.Concat(unchainedPluginDownloadTaskAsList);
        }

        /// <summary>
        /// This is all very dirty and will be deleted in 1.x ModManager should never hold a reference to the window, and should never be invoking delegates on the UI thread.
        /// </summary>
        /// <param name="window"></param>
        /// <returns></returns>
        private static async Task<SemVersion?> CheckForUnchainedPluginUpdates(Window window) {
            logger.Info("Checking for UnchainedPlugin.dll updates...");

            SemVersion? currentVersion = null;
            try {
                currentVersion = SemVersion.Parse(File.ReadAllText(FilePaths.UnchainedPluginVersionPath), SemVersionStyles.AllowV);
            } catch (FileNotFoundException) {
                logger.Warn("Failed to find UnchainedPlugin version file. Assuming no version.");
            } catch (FormatException) {
                logger.Warn("Failed to parse UnchainedPlugin version file. Assuming no version.");
            }

            // Check the latest version by looking at the redirect url for the latest release.
            // this will provide us with something like "https://github.com/Chiv2-Community/UnchainedPlugin/releases/tag/v1.0.0"
            var coreModUrl = await HttpHelpers.GetRedirectedUrl(CoreMods.UnchainedPluginLatestURL);
            SemVersion? latestVersion = null;

            try {
                latestVersion = SemVersion.Parse(coreModUrl?.Split("/").Last(), SemVersionStyles.AllowV);
            } catch (FormatException) {
                logger.Warn("Failed to parse latest UnchainedPlugin version from url. Assuming no version.");
            }

            var downloadPlugin = false;

            await window.Dispatcher.BeginInvoke(delegate () {
                if (latestVersion != null) {
                    if (currentVersion != null) {
                        if (currentVersion.ComparePrecedenceTo(latestVersion) <= -1) {
                            var message = $"An update is available for UnchainedPlugin.dll\n- Current Version: {currentVersion}\n- Latest Version: {latestVersion}.\n\nWould you like to download the new version?";

                            message.Split('\n').ToList().ForEach(x => logger.Info(x));

                            var result = MessageBox.Show(message, "Update UnchainedPlugin.dll?", MessageBoxButton.YesNo);

                            logger.Info("User Selects: " + result.ToString());

                            if (result == MessageBoxResult.Yes) {
                                downloadPlugin = true;
                            }
                        } else {
                            logger.Info("UnchainedPlugin.dll is up to date.");
                        }
                    } else {
                        var message = $"An update is available for UnchainedPlugin.dll\n- Latest Version: {latestVersion}.\n\nWould you like to download the new version?";
                        message.Split('\n').ToList().ForEach(x => logger.Info(x));

                        var result = MessageBox.Show(message, "Update UnchainedPlugin.dll?", MessageBoxButton.YesNo);
                        logger.Info("User Selects: " + result.ToString());

                        if (result == MessageBoxResult.Yes) { downloadPlugin = true; }
                    }
                } else {
                    logger.Warn("Failed to find latest version of UnchainedPlugin.dll");
                }
            });

            return downloadPlugin ? latestVersion : null;

        }

        private ModReleaseDownloadTask DownloadModRelease(Release release) {
            // Cleanup the previously failed download if it exists
            if (FailedDownloads.Any(x => x.Release == release))
                FailedDownloads.Remove(FailedDownloads.First(x => x.Release == release));

            var downloadUrl = release.Manifest.RepoUrl + "/releases/download/" + release.Tag + "/" + release.PakFileName;
            var outputPath = PakDir + "\\" + release.PakFileName;

            if (File.Exists(outputPath)) {
                var shaHash = FileHelpers.Sha512(outputPath);
                if (shaHash == release.ReleaseHash) {
                    logger.Info($"Already downloaded {release.Manifest.Name} {release.Tag}. Skipping");

                    // Already downloaded, so returning a completed task.
                    return new ModReleaseDownloadTask(release,
                        new DownloadTask(
                            Task.CompletedTask,
                            new DownloadTarget(downloadUrl, outputPath)
                        )
                    );
                }
            }

            var enabledModJson = JsonConvert.SerializeObject(release);
            var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);

            var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
            var filePath = orgPath + "\\" + urlParts.Last() + ".json";

            logger.Info("Writing enabled mod json metadata to " + filePath);
            Directory.CreateDirectory(orgPath);
            File.WriteAllText(filePath, enabledModJson);

            var downloadTask = new ModReleaseDownloadTask(release, HttpHelpers.DownloadFileAsync(downloadUrl, outputPath));
            PendingDownloads.Add(downloadTask);

            downloadTask.DownloadTask.Task.ContinueWith(task => {
                if (task.IsFaulted) {
                    logger.Error("Download failed: " + task.Exception?.Message);
                    FailedDownloads.Add(downloadTask);
                } else {
                    logger.Info("Download complete: " + outputPath);
                    var shaHash = FileHelpers.Sha512(outputPath);

                    if (shaHash != release.ReleaseHash) {
                        logger.Error("Downloaded file hash does not match expected hash. Expected: " + release.ReleaseHash + " Got: " + shaHash);
                        FailedDownloads.Add(downloadTask);
                        throw new Exception("Downloaded file hash does not match expected hash. Expected: " + release.ReleaseHash + " Got: " + shaHash);
                    }

                    PendingDownloads.Remove(downloadTask);
                }
            });

            return downloadTask;
        }
    }


    public record ResolveReleasesResult(bool Successful, List<Release> Releases, List<DependencyConflict> Conflicts) {
        public static ResolveReleasesResult Success(List<Release> releases) { return new ResolveReleasesResult(true, releases, new List<DependencyConflict>()); }
        public static ResolveReleasesResult Failure(List<DependencyConflict> conflicts) { return new ResolveReleasesResult(false, new List<Release>(), conflicts); }
        public static ResolveReleasesResult operator +(ResolveReleasesResult a, ResolveReleasesResult b) {
            return new ResolveReleasesResult(
                a.Successful && b.Successful,
                a.Releases.Concat(b.Releases).ToList(),
                a.Conflicts.Concat(b.Conflicts).ToList()
            );
        }
    }

    public record DependencyConflict(List<Tuple<Release, Dependency>> Dependents);

    public record ModReleaseDownloadTask(Release Release, DownloadTask DownloadTask);
}
