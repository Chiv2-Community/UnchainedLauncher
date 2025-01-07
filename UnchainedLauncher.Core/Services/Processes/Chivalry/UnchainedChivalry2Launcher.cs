using LanguageExt;
using log4net;
using Semver;
using System.Diagnostics;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    using static Prelude;

    public class UnchainedChivalry2Launcher : IUnchainedChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(UnchainedLauncher));

        private IProcessLauncher Launcher { get; }
        private IModManager ModManager { get; }
        private IReleaseLocator PluginReleaseLocator { get; }
        private IVersionExtractor<string> FileVersionExtractor { get; }
        private IUserDialogueSpawner UserDialogueSpawner { get; }
        private string InstallationRootDir { get; }
        private IProcessInjector ProcessInjector { get; }

        public UnchainedChivalry2Launcher(
            IProcessLauncher processLauncher,
            IModManager modManager,
            IReleaseLocator pluginReleaseLocator,
            IVersionExtractor<string> fileVersionExtractor,
            IUserDialogueSpawner userDialogueSpawner,
            string installationRootDir,
            IProcessInjector processInjector) {

            InstallationRootDir = installationRootDir;
            Launcher = processLauncher;
            ModManager = modManager;
            PluginReleaseLocator = pluginReleaseLocator;
            FileVersionExtractor = fileVersionExtractor;
            UserDialogueSpawner = userDialogueSpawner;
            ProcessInjector = processInjector;
        }

        public async Task<Either<UnchainedLaunchFailure, Process>> Launch(ModdedLaunchOptions launchOptions, bool updateUnchainedDependencies, string args) {
            logger.Info("Attempting to launch modded game.");

            var moddedLaunchArgs = args;
            var tblLoc = moddedLaunchArgs.IndexOf("TBL", StringComparison.Ordinal);
            var offsetIndex = tblLoc == -1 ? 0 : tblLoc + 3;

            var launchOpts = launchOptions.ToCLIArgs();

            moddedLaunchArgs.Insert(offsetIndex, " " + launchOpts);

            var updateResult = await PrepareUnchainedLaunch(updateUnchainedDependencies);
            if (updateResult == false) {
                return Left(UnchainedLaunchFailure.LaunchCancelled());
            }

            PrepareModdedLaunchSigs();

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

            var latestUnchainedMods = ModManager.Mods.SelectMany(x => x.LatestRelease).Find(IsUnchainedMods).FirstOrDefault();


            var latestPlugin = await PluginReleaseLocator.GetLatestRelease();
            SemVersion? currentPluginVersion = FileVersionExtractor.GetVersion(pluginPath);


            var pluginDependencyUpdate =
                (currentPluginVersion != null && currentPluginVersion.ComparePrecedenceTo(latestPlugin.Version) >= 0)
                    ? null
                    : new DependencyUpdate(
                        "UnchainedPlugin.dll",
                        currentPluginVersion?.ToString(),
                        latestPlugin.Version.ToString(),
                        latestPlugin.PageUrl,
                        "Used for hosting and connecting to player owned servers. Required to run Chivalry 2 Unchained."
                    );

            DependencyUpdate? unchainedModsDependencyUpdate = null;
            if (isUnchainedModsEnabled) {
                var unchainedModsUpdateCandidate = ModManager.GetUpdateCandidates().Find(x => IsUnchainedMods(x.AvailableUpdate)).FirstOrDefault();
                if (unchainedModsUpdateCandidate != null)
                    unchainedModsDependencyUpdate = DependencyUpdate.FromUpdateCandidate(unchainedModsUpdateCandidate);
            }
            else if (latestUnchainedMods == null) {
                logger.Warn("Could not find any unchained mods release.");
            }
            else {
                unchainedModsDependencyUpdate =
                    new DependencyUpdate(
                        latestUnchainedMods.Manifest.Name,
                        null,
                        latestUnchainedMods.Version.ToString(),
                        latestUnchainedMods.ReleaseUrl,
                        "Adds necessary Unchained content to Chivalry 2"
                    );
            }

            IEnumerable<DependencyUpdate> updates =
                new List<DependencyUpdate?>() { unchainedModsDependencyUpdate, pluginDependencyUpdate }
                    .Filter(x => x != null)!;



            var titleString = pluginExists
                ? "Update Required Unchained Dependencies"
                : "Install Required Unchained Dependencies";

            var messageText = pluginExists
                ? "Updates for the Unchained Dependencies are available."
                : "The Unchained Dependencies are not installed.";


            var userResponse = UserDialogueSpawner.DisplayUpdateMessage(
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
                    logger.Info("User cancelled chivalry 2 launch");
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

            if (latestUnchainedMods != null) {
                var result = await ModManager.EnableModRelease(latestUnchainedMods, None, CancellationToken.None);

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
            if (ProcessInjector.Inject(process)) return Right(process);
            else return Left(UnchainedLaunchFailure.InjectionFailed());
        }

        private static void PrepareModdedLaunchSigs() {
            logger.Info("Verifying .sig file presence");
            SigFileHelper.CheckAndCopySigFiles();
            SigFileHelper.DeleteOrphanedSigFiles();
        }
    }
}