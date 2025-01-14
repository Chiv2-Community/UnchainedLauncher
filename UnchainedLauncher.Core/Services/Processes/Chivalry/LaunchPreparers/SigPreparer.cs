using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    public static class SigPreparer {
        private static readonly ILog logger = LogManager.GetLogger(nameof(SigPreparer));

        public static KleisliTask<Unit, bool> PrepareLaunch(IUserDialogueSpawner userDialogueSpawner) => async _ => {
            logger.Info("Ensuring sigs are set up for all pak files.");
            var result = await Task.Run(() =>
                SigFileHelper.CheckAndCopySigFiles() && SigFileHelper.DeleteOrphanedSigFiles()
            );

            if (result) return true;

            userDialogueSpawner.DisplayMessage("Failed to prepare sig files. Check the logs for more information.");
            return false;
        };
    }
}