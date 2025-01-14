using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public class NoSigPreparer {
        private static readonly ILog logger = LogManager.GetLogger(nameof(NoSigPreparer));

        public static KleisliTask<Unit, bool> PrepareLaunch(IUserDialogueSpawner userDialogueSpawner) => async _ => {
            logger.Info("Ensuring sigs are removed for all modded paks.");
            var result = await Task.Run(SigFileHelper.RemoveAllNonDefaultSigFiles);
            if (result) return true;

            userDialogueSpawner.DisplayMessage("Failed to remove mod sig files. Check the logs for more details.");
            return false;
        };
    }
}