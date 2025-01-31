using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {

    /// <summary>
    /// Removes all non-vanilla sig files.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class NoSigPreparer : IChivalry2LaunchPreparer<Unit> {
        private readonly ILog _logger = LogManager.GetLogger(nameof(NoSigPreparer));

        private readonly IUserDialogueSpawner _userDialogueSpawner;

        public static IChivalry2LaunchPreparer<Unit> Create(IUserDialogueSpawner userDialogueSpawner) =>
            new NoSigPreparer(userDialogueSpawner);

        private NoSigPreparer(IUserDialogueSpawner userDialogueSpawner) {
            _userDialogueSpawner = userDialogueSpawner;
        }

        public async Task<Option<Unit>> PrepareLaunch(Unit input) {
            _logger.Info("Ensuring sigs are removed for all modded paks.");
            var result = await Task.Run(SigFileHelper.RemoveAllNonDefaultSigFiles);
            if (result) return Some(input);

            _userDialogueSpawner.DisplayMessage("Failed to remove mod sig files. Check the logs for more details.");
            return None;
        }
    }
}