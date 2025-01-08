using log4net;
using log4net.Core;
using Semver;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    public class UnchainedContentPreparer: IChivalry2LaunchPreparer {
        private readonly ILog logger = LogManager.GetLogger(typeof(UnchainedContentPreparer));

        public UnchainedContentPreparer(Func<bool> getShouldUpdateDependencies, IModManager modManager, IReleaseLocator pluginReleaseLocator, IVersionExtractor fileVersionExtractor, IUserDialogueSpawner userDialogueSpawner) {
            GetShouldUpdateDependencies = getShouldUpdateDependencies;
            ModManager = modManager;
            PluginReleaseLocator = pluginReleaseLocator;
            FileVersionExtractor = fileVersionExtractor;
            UserDialogueSpawner = userDialogueSpawner;
        }

        private Func<bool> GetShouldUpdateDependencies { get; }
        private IModManager ModManager { get; }
        private IReleaseLocator PluginReleaseLocator { get; }
        private IVersionExtractor FileVersionExtractor { get; }
        private IUserDialogueSpawner UserDialogueSpawner { get; }
        
        public async Task<bool> PrepareLaunch() {
            var pluginPath = Path.Combine(Directory.GetCurrentDirectory(), FilePaths.UnchainedPluginPath);
            var pluginExists = File.Exists(pluginPath);
            var isUnchainedModsEnabled = ModManager.EnabledModReleases.Exists(IsUnchainedMods);

            if (!GetShouldUpdateDependencies() && pluginExists && isUnchainedModsEnabled) return true;

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
    }
}