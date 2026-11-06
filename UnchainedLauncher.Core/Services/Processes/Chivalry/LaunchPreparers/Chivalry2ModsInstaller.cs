using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    using static LanguageExt.Prelude;

    public class Chivalry2ModsInstaller : IChivalry2LaunchPreparer<LaunchOptions> {
        private readonly ILog _logger = LogManager.GetLogger(typeof(Chivalry2ModsInstaller));
        private readonly IUserDialogueSpawner _userDialogueSpawner;
        private readonly IUnmanagedPakSettings? _settings;
        private IModManager _modManager { get; }

        public Chivalry2ModsInstaller(IModManager modManager, IUserDialogueSpawner dialogueSpawner, IUnmanagedPakSettings? settings = null) {
            _modManager = modManager;
            _userDialogueSpawner = dialogueSpawner;
            _settings = settings;
        }

        public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
            // Get all releases that will be installed (enabled mods + dependencies)
            var includeDependencies = _settings?.AutomaticallyDownloadPakDependencies ?? true;
            var releasesToInstall = _modManager.SortByDependencies(_modManager.GetEnabledAndDependencyReleases(includeDependencies)).ToList();
            _logger.LogListInfo("Installing the following releases:", releasesToInstall.Select(ReleaseCoordinates.FromRelease).ToList());

            var adopted = new List<Release>();
            if (!await _modManager.PakDir.RecoverReleases(releasesToInstall,
                _modManager.Mods.SelectMany(mod => mod.Releases), _userDialogueSpawner, adopted, _settings?.AlwaysKeepUnmanagedPaks == true)) return None;
            foreach (var release in adopted) _modManager.EnableModRelease(ReleaseCoordinates.FromRelease(release));
            releasesToInstall = _modManager.SortByDependencies(_modManager.GetEnabledAndDependencyReleases(includeDependencies)).ToList();

            // Install mods via ModManager
            var progresses = new AccumulatedMemoryProgress(taskName: "Installing releases");
            var closeProgressWindow = _userDialogueSpawner.DisplayProgress(progresses);

            List<Either<LanguageExt.Common.Error, ManagedPak>> installResults;
            try {
                installResults = await _modManager.InstallMods(releasesToInstall, progresses).ToListAsync();
            }
            finally {
                closeProgressWindow();
            }

            var (errors, successes) =
                installResults.Partition()
                    .MapFirst(x => x.ToList())
                    .MapSecond(x => x.ToList());

            successes.ForEach(succ => _logger.Debug($"Successfully installed {succ.Coordinates.ModuleName} version {succ.Coordinates.Version}"));

            if (errors.Count != 0) {
                errors.ForEach(e => _logger.Error($"Failed to install releases: {e.Message}"));
                _userDialogueSpawner.DisplayMessage(
                    $"There were errors while installing releases:\n " +
                    $"{string.Join("\n\n", errors)}\n" +
                    "Launch cancelled. Resolve the pak setup errors before launching."
                    );
                return None;
            }

            return Some(options);
        }
    }
}