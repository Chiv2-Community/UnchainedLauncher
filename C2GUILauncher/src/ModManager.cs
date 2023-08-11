using C2GUILauncher.JsonModels;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

        public const string AssetLoaderPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin.dll";
        public const string ServerPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2ServerPlugin/releases/latest/download/C2ServerPlugin.dll";
        public const string BrowserPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin.dll";

        public const string PackageDBBaseUrl = "https://raw.githubusercontent.com/Chiv2-Community/C2ModRegistry/db/package_db";
        public const string PackageDBPackageListUrl = $"{PackageDBBaseUrl}/mod_list_index.txt";
    }

    public class ModManager {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

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
            // Create mod cache dirs if they don't exist
            Directory.CreateDirectory(CoreMods.EnabledModsCacheDir);
            Directory.CreateDirectory(CoreMods.ModsCachePackageDBDir);
            Directory.CreateDirectory(CoreMods.ModsCachePackageDBPackagesDir);

            // List everything in the EnabledModsCacheDir and its direct subdirs, then deserialize and filter out any failures (null)
            var enabledModReleases =
                Directory.GetDirectories(CoreMods.EnabledModsCacheDir)
                    .SelectMany(x => Directory.GetFiles(x))
                    .Select(x => JsonConvert.DeserializeObject<Release>(File.ReadAllText(x)))
                    .Where(x => x != null);

            enabledModReleases ??= new List<Release>();

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
            logger.Info("Disabling mod release: " + release.Manifest.Name + " @" + release.Tag);
            EnabledModReleases.Remove(release);

            var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);

            var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
            var filePath = orgPath + "\\" + urlParts.Last() + ".json";

            //EnabledModReleases
            //    .Where(otherRelease => otherRelease.Manifest.Dependencies.Any(dep => dep.RepoUrl == release.Manifest.RepoUrl))

            File.Delete(filePath);
            File.Delete(PakDir + "\\" + release.PakFileName);
        }

        public ModEnableResult EnableModRelease(Release release) {
            logger.Debug("Enabling mod release: " + release.Manifest.Name + " @" + release.Tag);
            var associatedMod = this.Mods.First(Mods => Mods.Releases.Contains(release));

            if (associatedMod == null)
                return ModEnableResult.Fail("Selected release not found in mod list: " + release.Manifest.Name + " @" + release.Tag);

            var result = ModEnableResult.Success;

            var enabledModRelease = GetCurrentlyEnabledReleaseForMod(associatedMod);

            if (enabledModRelease != null) {
                // If its already enabled and the download was successful, just return success
                if (enabledModRelease == release && !FailedDownloads.Any(x => x.Release == release))
                    return ModEnableResult.Success;

                result += ModEnableResult.Warn("Mod already enabled with different version: " + enabledModRelease.Manifest.Name + " @" + enabledModRelease.Tag);
            }

            foreach (var dependency in release.Manifest.Dependencies) {
                var dependencyRelease =
                    this.Mods
                        .FirstOrDefault(mod => mod.LatestManifest.RepoUrl == dependency.RepoUrl)?.Releases
                        .FirstOrDefault(release => release.Tag == dependency.Version);

                if (dependencyRelease == null)
                    result += ModEnableResult.Fail("Dependency not found: " + dependency.RepoUrl + " @" + dependency.Version);
                else
                    result += EnableModRelease(dependencyRelease);
            }


            EnabledModReleases.Add(release);
            PendingDownloads.Add(DownloadModRelease(release));

            return result;
        }

        private ModReleaseDownloadTask DownloadModRelease(Release release) {
            // Cleanup the previously failed download if it exists
            if (FailedDownloads.Any(x => x.Release == release))
                FailedDownloads.Remove(FailedDownloads.First(x => x.Release == release));

            var downloadUrl = release.Manifest.RepoUrl + "/releases/download/" + release.Tag + "/" + release.PakFileName;
            var outputPath = PakDir + "\\" + release.PakFileName;

            logger.Info("Beginning download of " + downloadUrl + " to " + outputPath);
            var downloadTask = new ModReleaseDownloadTask(release, HttpHelpers.DownloadFileAsync(downloadUrl, outputPath));
            PendingDownloads.Add(downloadTask);

            downloadTask.DownloadTask.Task.ContinueWith(task => {
                if (task.IsFaulted) {
                    logger.Error("Download failed: " + task.Exception?.Message);
                    FailedDownloads.Add(downloadTask);
                } else {
                    logger.Info("Download complete: " + outputPath);
                    var enabledModJson = JsonConvert.SerializeObject(release);
                    var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);

                    var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
                    var filePath = orgPath + "\\" + urlParts.Last() + ".json";

                    Directory.CreateDirectory(orgPath);
                    File.WriteAllText(filePath, enabledModJson);

                    PendingDownloads.Remove(downloadTask);
                }
            });

            return downloadTask;
        }

        public async Task UpdateModsList() {
            Mods.Clear();

            await HttpHelpers.DownloadFileAsync(CoreMods.PackageDBPackageListUrl, CoreMods.ModsCachePackageDBListPath).Task;

            var packageListString = File.ReadAllText(CoreMods.ModsCachePackageDBListPath);
            var packageNameToMetadataPath = (String s) => $"{CoreMods.PackageDBBaseUrl}/packages/{s}.json";
            var packageNameToFilePath = (String s) => $"{CoreMods.ModsCachePackageDBPackagesDir}\\{s}.json";

            var packages = packageListString.Split("\n").Where(s => s.Length > 0);

            var downloadTasks = packages
                .Select(packageName => HttpHelpers.DownloadFileAsync(packageNameToMetadataPath(packageName), packageNameToFilePath(packageName)))
                .Select(async downloadTask => {
                    await downloadTask.Task;
                    var fileLocation = downloadTask.Target.OutputPath!;
                    var mod = JsonConvert.DeserializeObject<Mod>(await File.ReadAllTextAsync(fileLocation));
                    if (mod != null)
                        Mods.Add(mod);
                    return mod;
                })
                .ToList();

            await Task.WhenAll(downloadTasks);
        }

        // TODO: Somehow move this around. this function downloads the plugins, not the mods
        public IEnumerable<DownloadTask> DownloadModFiles(bool debug) {
            // Create plugins dir. This method does nothing if the directory already exists.
            Directory.CreateDirectory(FilePaths.PluginDir);

            // All Chiv2-Community dll releases have an optional _dbg suffix for debug builds.
            var downloadFileSuffix = debug ? "_dbg.dll" : ".dll";

            // These are the core mods necessary for asset loading, server hosting, server browser usage, and the injector itself.
            // Please forgive the jank debug dll implementation. It'll be less jank after we aren't using hardcoded paths
            var coreMods = new List<DownloadTarget>() {
                new DownloadTarget(CoreMods.AssetLoaderPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.AssetLoaderPluginPath),
                new DownloadTarget(CoreMods.ServerPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.ServerPluginPath),
                new DownloadTarget(CoreMods.BrowserPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.BrowserPluginPath)
            };

            return HttpHelpers.DownloadAllFiles(coreMods);
        }
    }


    public record ModEnableResult(bool Successful, List<string> Failures, List<string> Warnings) {
        public static ModEnableResult Success => new ModEnableResult(true, new List<string>(), new List<string>());
        public static ModEnableResult Fail(string failure) => new ModEnableResult(false, new List<string>() { failure }, new List<string>());
        public static ModEnableResult Fails(List<string> failures) => new ModEnableResult(false, failures, new List<string>());
        public static ModEnableResult Warn(string warning) => new ModEnableResult(false, new List<string>(), new List<string>() { warning });
        public static ModEnableResult Warns(List<string> warnings) => new ModEnableResult(false, new List<string>(), warnings);

        public static ModEnableResult operator +(ModEnableResult a, ModEnableResult b) {
            return new ModEnableResult(
                a.Successful && b.Successful,
                a.Failures.Concat(b.Failures).ToList(),
                a.Warnings.Concat(b.Warnings).ToList()
            );
        }
    }

    public record ModReleaseDownloadTask(Release Release, DownloadTask DownloadTask);
}
