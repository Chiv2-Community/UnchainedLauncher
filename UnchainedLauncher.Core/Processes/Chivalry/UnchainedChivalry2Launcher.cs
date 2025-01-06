using LanguageExt;
using log4net;
using Semver;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Processes.Chivalry {
    using static Prelude;

    public class UnchainedChivalry2Launcher : IUnchainedChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(UnchainedLauncher));

        private IProcessLauncher Launcher { get; }
        private IModManager ModManager { get; }
        private IReleaseLocator PluginReleaseLocator { get; }
        private Func<IEnumerable<string>> FetchDLLs { get; }
        private IVersionExtractor<string> FileVersionExtractor { get; }
        private IUpdateNotifier UpdateNotifier { get; }
        private IUserDialogueSpawner UserDialogueSpawner { get; }
        private string InstallationRootDir { get; }

        public UnchainedChivalry2Launcher(
            IProcessLauncher processLauncher,
            IModManager modManager,
            IReleaseLocator pluginReleaseLocator,
            IVersionExtractor<string> fileVersionExtractor,
            IUpdateNotifier updateNotifier,
            IUserDialogueSpawner userDialogueSpawner,
            string installationRootDir,
            Func<IEnumerable<string>> dlls) {

            FetchDLLs = dlls;
            InstallationRootDir = installationRootDir;
            Launcher = processLauncher;
            ModManager = modManager;
            PluginReleaseLocator = pluginReleaseLocator;
            FileVersionExtractor = fileVersionExtractor;
            UpdateNotifier = updateNotifier;
            UserDialogueSpawner = userDialogueSpawner;
        }

        public Either<UnchainedLaunchFailure, Process> Launch(ModdedLaunchOptions launchOptions, bool updateUnchainedDependencies, string args) {
            logger.Info("Attempting to launch modded game.");

            var moddedLaunchArgs = args;
            var tblLoc = moddedLaunchArgs.IndexOf("TBL", StringComparison.Ordinal);
            var offsetIndex = tblLoc == -1 ? 0 : tblLoc + 3;

            var launchOpts = launchOptions.ToCLIArgs();

            moddedLaunchArgs.Insert(offsetIndex, " " + launchOpts);

            PrepareModdedLaunchSigs();
            var updateTask = PrepareUnchainedLaunch(updateUnchainedDependencies);
            updateTask.Wait();
            var updateResult = updateTask.Result;

            if (updateResult == false) {
                logger.Error("Failed to launch modded game.");
            }


            logger.Info($"Launch args: {moddedLaunchArgs}");

            var launchResult = Launcher.Launch(Path.Combine(InstallationRootDir, FilePaths.BinDir), moddedLaunchArgs);

            return launchResult.Match(
                Left: error => {
                    logger.Error(error);
                    return Left(UnchainedLaunchFailure.LaunchFailed(error));
                },
                Right: proc => {
                    logger.Info("Successfully launched Chivalry 2 Unchained");
                    return InjectDLLs(proc);
                }
            );
        }

        private async Task<bool> PrepareUnchainedLaunch(bool updateDependencies) {
            var pluginPath = Path.Combine(Directory.GetCurrentDirectory(), FilePaths.UnchainedPluginPath);
            var pluginExists = File.Exists(pluginPath);
            var isUnchainedModsEnabled = ModManager.EnabledModReleases.Exists(IsUnchainedMods);

            if (!updateDependencies && pluginExists && isUnchainedModsEnabled) return true;

            var latestPlugin = await PluginReleaseLocator.GetLatestRelease();
            SemVersion? currentPluginVersion = FileVersionExtractor.GetVersion(pluginPath);

            var unchainedModsUpdateCandidate = ModManager.GetUpdateCandidates().Find(x => IsUnchainedMods(x.AvailableUpdate)).FirstOrDefault();

            var pluginDependencyUpdate =
                (currentPluginVersion != null && currentPluginVersion.ComparePrecedenceTo(latestPlugin.Version) >= 0)
                    ? null
                    : new DependencyUpdate(
                        "UnchainedPlugin.dll",
                        Optional(currentPluginVersion?.ToString()),
                        latestPlugin.Version.ToString(),
                        latestPlugin.PageUrl,
                        "Used for hosting and connecting to player owned servers. Required to run Chivalry 2 Unchained."
                    );

            var unchainedModsDependencyUpdate =
                (isUnchainedModsEnabled && unchainedModsUpdateCandidate == null)
                    ? null
                    : DependencyUpdate.FromUpdateCandidate(unchainedModsUpdateCandidate!);

            IEnumerable<DependencyUpdate> updates =
                new List<DependencyUpdate?>() { unchainedModsDependencyUpdate, pluginDependencyUpdate }
                    .Filter(x => x != null)!;



            var titleString = pluginExists
                ? "Update Required Unchained Dependencies"
                : "Install Required Unchained Dependencies";

            var messageText = pluginExists
                ? "Updates for the Unchained Dependencies are available."
                : "The Unchained Dependencies are not installed.";


            var userResponse = UpdateNotifier.Notify(
                titleString,
                messageText,
                "Yes",
                "No",
                "Cancel",
                updates
            );

            // The cases in here are all for exiting early.
            switch (userResponse) {
                // No updates available, continue launch
                case null:
                    return true;

                // Continue launch, don't download or install anything
                case UserDialogueChoice.No:
                    return true;

                // Do not continue launch, don't download or install anything
                case UserDialogueChoice.Cancel:
                    return false;

                // User selected yes/ok. Continue to download
                case UserDialogueChoice.Yes:
                    break;
            }

            logger.Info("Updating Unchained Dependencies");

            if (pluginDependencyUpdate != null) {
                var downloadResult = await HttpHelpers.DownloadReleaseTarget(
                    latestPlugin,
                    asset => (asset.Name == "UnchainedPlugin.dll") ? pluginPath : null
                );

                if (downloadResult == false) {
                    UserDialogueSpawner.DisplayMessage(
                        "Failed to download Unchained Plugin. Aborting launch. Check the logs for more details.");
                    return false;
                }
            }

            if (unchainedModsDependencyUpdate != null) {
                var result = await ModManager.EnableModRelease(unchainedModsUpdateCandidate!.AvailableUpdate, None, CancellationToken.None);

                if (result.IsLeft) {
                    var error = result.LeftToSeq().FirstOrDefault()!;
                    logger.Error("Failed to download latest Unchained-Mods", error);
                    UserDialogueSpawner.DisplayMessage(
                        "Failed to download latest Unchained-Mods. Aborting launch. Check the logs for more details.");
                    return false;
                }
            }

            return true;

            bool IsUnchainedMods(Release release) => release.Manifest.RepoName == "Unchained-Mods" &&
                                                     release.Manifest.Organization == "Chiv2-Community";
        }

        private Either<UnchainedLaunchFailure, Process> InjectDLLs(Process process) {
            IEnumerable<string>? dlls = null;
            try {
                dlls = FetchDLLs();
                if (!dlls.Any()) return Prelude.Right(process);
                logger.LogListInfo("Injecting DLLs:", dlls);
                Inject.InjectAll(process, dlls);
            }
            catch (Exception e) {
                return Left(UnchainedLaunchFailure.InjectionFailed(Optional(dlls), e));
            }
            return Right(process);
        }

        private static void PrepareModdedLaunchSigs() {
            logger.Info("Verifying .sig file presence");
            SigFileHelper.CheckAndCopySigFiles();
            SigFileHelper.DeleteOrphanedSigFiles();
        }
    }
}