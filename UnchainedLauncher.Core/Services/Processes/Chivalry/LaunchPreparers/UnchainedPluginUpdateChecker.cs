using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    using static LanguageExt.Prelude;
    public class UnchainedPluginUpdateChecker : IChivalry2LaunchPreparer<ModdedLaunchOptions> {
        private readonly ILog _logger = LogManager.GetLogger(typeof(UnchainedPluginUpdateChecker));
        private readonly string _pluginPath;
        
        private IReleaseLocator PluginReleaseLocator { get; }
        private IVersionExtractor FileVersionExtractor { get; }
        private IUserDialogueSpawner _userDialogueSpawner { get; }
        
        public static IChivalry2LaunchPreparer<ModdedLaunchOptions> Create(
            IReleaseLocator pluginReleaseLocator,
            IVersionExtractor fileVersionExtractor,
            IUserDialogueSpawner userDialogueSpawner
        ) => new UnchainedPluginUpdateChecker(pluginReleaseLocator, fileVersionExtractor, userDialogueSpawner);

        public UnchainedPluginUpdateChecker(
            IReleaseLocator pluginReleaseLocator,
            IVersionExtractor fileVersionExtractor,
            IUserDialogueSpawner userDialogueSpawner) {
            PluginReleaseLocator = pluginReleaseLocator;
            FileVersionExtractor = fileVersionExtractor;
            _userDialogueSpawner = userDialogueSpawner;
            _pluginPath = Path.Combine(Directory.GetCurrentDirectory(), FilePaths.UnchainedPluginPath);
        }
        
        public async Task<Option<string>> UpdatePlugin(ReleaseTarget latestPlugin) {
            var downloadResult = await HttpHelpers.DownloadReleaseTarget(
                latestPlugin,
                asset => (asset.Name == "UnchainedPlugin.dll") ? _pluginPath : null
            );

            if (!downloadResult) {
                return "Failed to download Unchained Plugin. Check the logs for more details.";
            }

            return None;
        }

        public async Task<Option<ModdedLaunchOptions>> PrepareLaunch(ModdedLaunchOptions options) {
            if (!options.CheckForDependencyUpdates) {
                return options;
            }
            
            var latestPlugin = await PluginReleaseLocator.GetLatestRelease();
            if (latestPlugin == null) {
                _logger.Warn("Could not find latest plugin");
                return None;
            }

            var currentVersion = FileVersionExtractor.GetVersion(_pluginPath);

            if (currentVersion?.ComparePrecedenceTo(latestPlugin.Version) >= 0)
                return options;

            var update = new DependencyUpdate(
                "UnchainedPlugin.dll",
                currentVersion?.ToString(),
                latestPlugin.Version.ToString(),
                latestPlugin.PageUrl,
                "Used for hosting and connecting to player owned servers. Required to run Chivalry 2 Unchained."
            );
            
            var action = update.CurrentVersion == null ? "Install" : "Update";
            var choice = _userDialogueSpawner.DisplayUpdateMessage(
                $"{action} plugin",
                $"Would you like to {action.ToLower()} the unchained plugin?",
                "yes",
                "no",
                null,
                update);

            if (choice != UserDialogueChoice.Yes) {
                return options;
            }

            var updateResult = await UpdatePlugin(latestPlugin);
            return updateResult.Match(
                (err) => {
                    _logger.Warn($"Failed to update plugin: {err}");
                    return None;
                },
                () => Some(options)
            );
        }
    }
}