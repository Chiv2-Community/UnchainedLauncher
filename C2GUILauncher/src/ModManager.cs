using C2GUILauncher.JsonModels;
using Newtonsoft.Json;
using NLog;
using Semver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
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

        //public const string AssetLoaderPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin.dll";
        //public const string ServerPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2ServerPlugin/releases/latest/download/C2ServerPlugin.dll";
        //public const string BrowserPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin.dll";

        public const string UnchainedPluginPath = $"{FilePaths.PluginDir}\\UnchainedPlugin.dll";
        public const string UnchainedPluginURL = $"{GithubBaseURL}/Chiv2-Community/UnchainedPlugin/releases/latest/download/UnchainedPlugin.dll";

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

            // Should be doing this when all downloads get done, but cba to do it better right now.
            FileHelpers.DeleteFile(PakDir + "\\" + release.PakFileName);

            EnabledModReleases.Remove(release);
        }

        public void EnableModRelease(Release release) {
            logger.Debug("Enabling mod release: " + release.Manifest.Name + " @" + release.Tag);
            var associatedMod = this.Mods.First(Mods => Mods.Releases.Contains(release));
            var currentlyEnabledRelease = GetCurrentlyEnabledReleaseForMod(associatedMod);
            if (currentlyEnabledRelease != null) {
                  DisableModRelease(currentlyEnabledRelease);
            }

            this.EnabledModReleases.Add(release);
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

        public IEnumerable<ModReleaseDownloadTask> DownloadModFiles(bool debug) {
            Directory.CreateDirectory(FilePaths.PluginDir);
            var downloadFileSuffix = debug ? "_dbg.dll" : ".dll";

            var DeprecatedLibs = new List<String>()
            {
                CoreMods.AssetLoaderPluginPath,
                CoreMods.ServerPluginPath,
                CoreMods.BrowserPluginPath
            };

            foreach (var depr in DeprecatedLibs)
                FileHelpers.DeleteFile(depr);


            var coreMods = new List<DownloadTarget>() {
                new DownloadTarget(CoreMods.UnchainedPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.UnchainedPluginPath)
            };

            var coreModsDownloads = HttpHelpers.DownloadAllFiles(coreMods);

            var coreModsModReleaseDownloadTasks = 
                coreModsDownloads.Select(downloadTask => new ModReleaseDownloadTask(
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
                            new List<string>()
                        )
                    ),
                    downloadTask
                ));

            var tasks = EnabledModReleases.ToList().Select(DownloadModRelease);

            return coreModsModReleaseDownloadTasks.Concat(tasks);
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
