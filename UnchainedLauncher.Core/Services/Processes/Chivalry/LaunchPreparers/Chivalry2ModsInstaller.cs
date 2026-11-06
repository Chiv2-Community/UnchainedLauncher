using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Extensions;
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
            var recovered = await new PakReleaseRecovery(_modManager, _userDialogueSpawner, _settings?.AlwaysKeepUnmanagedPaks == true).RecoverPlan();
            if (recovered.IsNone) return None;
            var releasesToInstall = recovered.IfNone([]);
            _logger.LogListInfo("Installing the following releases:", releasesToInstall.Select(ReleaseCoordinates.FromRelease).ToList());

            // Install mods via ModManager
            var progresses = new AccumulatedMemoryProgress(taskName: "Installing releases");
            var closeProgressWindow = _userDialogueSpawner.DisplayProgress(progresses);

            var installResults = await _modManager.InstallMods(releasesToInstall, progresses).ToListAsync();

            var (errors, successes) =
                installResults.Partition()
                    .MapFirst(x => x.ToList())
                    .MapSecond(x => x.ToList());

            successes.ForEach(succ => _logger.Debug($"Successfully installed {succ.Coordinates.ModuleName} version {succ.Coordinates.Version}"));

            if (errors.Count != 0) {
                errors.ToList().ForEach(e => _logger.Error($"Failed to install releases: {e.Message}"));
                var shouldContinue = _userDialogueSpawner.DisplayYesNoMessage(
                    $"There were errors while installing releases:\n " +
                    $"{errors.Aggregate((a, b) => $"{a}\n\n{b}")}\n" +
                    "Would you like to continue anyway?", "Errors encountered"
                    );
                if (shouldContinue == UserDialogueChoice.No) return None;
            }

            closeProgressWindow();

            return Some(options);
        }
    }
}