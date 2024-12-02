using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Pipes;
using LanguageExt.Thunks;
using LanguageExt.TypeClasses;
using LanguageExt.UnsafeValueAccess;
using log4net;
using Octokit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Utilities;
using Semver;

namespace UnchainedLauncher.Core.Installer {
    using static LanguageExt.Prelude;

    public interface IUnchainedLauncherInstaller {

        /// <summary>
        /// Returns the latest stable release of the launcher.
        /// </summary>
        /// <returns></returns>
        public Task<Option<VersionedRelease>> GetLatestRelease();

        /// <summary>
        /// Returns all releases of the launcher, including pre-releases.
        /// </summary>
        /// <returns></returns>
        public Task<IEnumerable<VersionedRelease>> GetAllReleases();
        // public Task<Option<VersionedRelease>> CheckForUpdate();


        public Task<bool> Install(DirectoryInfo targetDir, VersionedRelease release, bool relaunch, Action<string>? logProgress = null);
    }

    public class UnchainedLauncherInstaller: IUnchainedLauncherInstaller {
        public static readonly ILog logger = LogManager.GetLogger(nameof(UnchainedLauncherInstaller));

        private static readonly long REPO_ID = 667470779;
        // private static readonly Version CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version!;

        public GitHubClient GitHubClient { get; }
        private Action EndProgram { get; }

        private Option<IEnumerable<VersionedRelease>> ReleaseCache { get; set; } = None;
        private Option<VersionedRelease> LatestRelease { get; set; } = None;

        public UnchainedLauncherInstaller(GitHubClient gitHubClient, Action endProgram) {
            GitHubClient = gitHubClient;
            EndProgram = endProgram;
        }

        public async Task<Option<VersionedRelease>> GetLatestRelease() {
            if(LatestRelease.IsSome) {
                return LatestRelease;
            }

            if(ReleaseCache.IsSome) {
                var sortedReleases =
                    from release in ReleaseCache.ValueUnsafe()
                    orderby release.Version descending
                    where release.Version.IsRelease
                    select release;
                    
                LatestRelease = Optional(sortedReleases?.FirstOrDefault());
                return LatestRelease;
            }

            try {
                var latestRelease = await GitHubClient.Repository.Release.GetLatest(REPO_ID);

                LatestRelease =
                    from release in Optional(latestRelease)
                    from version in ParseTag(release.TagName)
                    select new VersionedRelease(release, version);

                return LatestRelease;
            } catch(Exception e) {
                logger.Error("Failed to connect to github to retrieve latest release information", e);
                return None;
            }
        }

        public async Task<IEnumerable<VersionedRelease>> GetAllReleases() {
            if (ReleaseCache.IsSome) {
                return ReleaseCache.ValueUnsafe();
            }

            var repoCall = GitHubClient.Repository.Release.GetAll(REPO_ID);
            try {
                var maybeReleases = Optional(await repoCall);
                var results =
                    from releases in maybeReleases
                    from release in releases
                    from version in ParseTag(release.TagName)
                    select new VersionedRelease(release, version);

                ReleaseCache = Optional(results);
                return ReleaseCache.ToList().Flatten();
            } catch (Exception e) {
                logger.Error("Failed to connect to github to retrieve version information", e);
                return ImmutableList.CreateBuilder<VersionedRelease>().ToImmutableList();
            }
        }

        /// <summary>
        /// Installs the launcher by downloading the latest release and replacing the current executable with the new one.
        /// </summary>
        /// <param name="targetDir"></param>
        /// <param name="release"></param>
        /// <param name="replaceCurrent">If true, closes the current executable and launches the installed executable with the same args used to launch the current executable.</param>
        /// <returns>
        /// </returns>
        public async Task<bool> Install(DirectoryInfo targetDir, VersionedRelease release, bool replaceCurrent, Action<string>? logProgress = null) {
            var log = new Action<string>(s => {
                logProgress?.Invoke(s);
                logger.Info(s);
            });

            try {
                var url =
                    (from releaseAssets in release.Release.Assets
                     where releaseAssets.Name.Contains("Launcher.exe")
                     select releaseAssets.BrowserDownloadUrl).First();


                var fileName = $"UnchainedLauncher-{release.Version}.exe";
                var downloadFilePath = Path.Combine(targetDir.FullName, fileName);

                log($"Downloading release {release.Release.TagName}\n    from {url}\n    to {downloadFilePath}");

                // We only want to download the Launcher executable, even if the release contains multiple assets
                var assetFilter = (ReleaseAsset asset) => asset.Name.Contains("Launcher.exe") ? Some(downloadFilePath) : None;
                var downloadResult = await DownloadRelease(release, assetFilter, log);

                if (!downloadResult) {
                    log($"Failed to download the launcher version {release.Version}.");
                    return false;
                }
                
                var launcherPath = Path.Combine(targetDir.FullName, FilePaths.LauncherPath);

                if (replaceCurrent) {
                    var currentExecutableName = Process.GetCurrentProcess().ProcessName;

                    if (!currentExecutableName.EndsWith(".exe")) {
                        currentExecutableName += ".exe";
                    }

                    log($"Replacing current executable {currentExecutableName} with downloaded launcher {downloadFilePath}");

                    var commandLinePass = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));

                    var powershellCommand = new List<string>() {
                        $"Wait-Process -Id {Environment.ProcessId}",
                        $"Start-Sleep -Milliseconds 1000",
                        $"Move-Item -Force {downloadFilePath} {currentExecutableName}",
                        $"Start-Sleep -Milliseconds 500",
                        $"{launcherPath} {commandLinePass}"
                    };

                    MoveExistingLauncher(targetDir, log);

                    log($"Executing powershell command: \n  {string.Join(";\n  ", powershellCommand)};");
                    PowerShell.Run(powershellCommand);

                    log("Exitting current process to launch new launcher");
                    EndProgram();
                } else {
                    MoveExistingLauncher(targetDir, log);

                    log($"Replacing launcher \n    at {launcherPath} \n    with downloaded launcher from {downloadFilePath}");
                    File.Move(downloadFilePath, launcherPath, true);
                    log($"Successfully installed launcher version {release.Version}");
                }


                return true;
            } catch (Exception ex) {
                log(ex.ToString());
                logger.Error(ex);
            }

            return false;
        }

        private static void MoveExistingLauncher(DirectoryInfo targetDir, Action<string> log) {
            var launcherPath = Path.Combine(targetDir.FullName, FilePaths.LauncherPath);
            var originalLauncherPath = Path.Combine(targetDir.FullName, FilePaths.OriginalLauncherPath);

            log("Checking if the existing launcher needs to be moved...");

            // Only if the Product Name of the file at the launcher path is not the same as the current executable
            if (File.Exists(launcherPath)) {
                var launcherProductName = FileVersionInfo.GetVersionInfo(launcherPath)?.ProductName;
                var currentExecutableProductName = Assembly.GetExecutingAssembly().GetName().Name;

                if (launcherProductName != currentExecutableProductName) {
                    log($"Existing launcher is not {currentExecutableProductName}. Moving existing launcher to {originalLauncherPath}");
                    File.Move(launcherPath, originalLauncherPath, true);
                } else {
                    log("Existing launcher is a modified launcher. Overwriting with version selected in the installer..");
                }
            }
        }

        /// <summary>
        /// Downloads the assets of a release to the specified download targets.
        /// 
        /// The assetDownloadTargets thing is probably overkill, but may come in handy if we ever split out the installer and launcher in to separate exes.
        /// </summary>
        /// <param name="download">The release to download files from</param>
        /// <param name="assetDownloadTargets">A function returning Some(filePath) whenever an asset should be downloaded</param>
        /// <returns></returns>
        private static async Task<bool> DownloadRelease(VersionedRelease download, Func<ReleaseAsset, Option<string>> assetDownloadTargets, Action<string> log) {
            var results =
                await download.Release.Assets.ToList().Select(async asset => {
                    var downloadTarget = assetDownloadTargets(asset);

                    if (downloadTarget.IsNone) {
                        logger.Debug($"Skipping asset with no download target {asset.Name}");
                        return true;
                    }

                    try {
                        await HttpHelpers.DownloadFileAsync(asset.BrowserDownloadUrl, downloadTarget.ValueUnsafe()).Task;
                        log($"Downloaded {asset.Name} to {downloadTarget.ValueUnsafe()}");
                        return true;
                    } catch (Exception e) {
                        log($"Failed to download launcher\n    from {asset.BrowserDownloadUrl}\n    to {downloadTarget.ValueUnsafe()}");
                        log(e.ToString());
                        return false;
                    }
                })
                .SequenceParallel();

            return results.ForAll(identity);
        }


        private static Option<SemVersion> ParseTag(string tag) {
            try {
                var versionString = tag;
                if (versionString.StartsWith("v")) {
                    versionString = versionString[1..];
                }
                return Some(SemVersion.Parse(versionString, SemVersionStyles.Any));
            } catch {
                logger.Info($"Failed to parse version tag {tag}");
                return None;
            }
        }
    }
    public class MockInstaller: IUnchainedLauncherInstaller {
        public IEnumerable<VersionedRelease> MockReleases { get; set; } = VersionedRelease.DefaultMockReleases;
        
        public MockInstaller(): this(VersionedRelease.DefaultMockReleases) { }
        public MockInstaller(IEnumerable<VersionedRelease> mockReleases) {
            MockReleases = mockReleases;
        }


        public Task<Option<VersionedRelease>> GetLatestRelease() => Task.FromResult(Optional(MockReleases.MaxBy(x => x.Version)));
        public Task<IEnumerable<VersionedRelease>> GetAllReleases() => Task.FromResult(MockReleases);
        public Task<bool> Install(DirectoryInfo targetDir, VersionedRelease release, bool relaunch, Action<string>? logProgress = null) => Task.FromResult(true);
    }
}
