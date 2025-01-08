using log4net;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    public class SigPreparer : IChivalry2LaunchPreparer {
        private readonly ILog logger = LogManager.GetLogger(nameof(SigPreparer));

        private IUserDialogueSpawner UserDialogueSpawner;

        public SigPreparer(IUserDialogueSpawner userDialogueSpawner) {
            UserDialogueSpawner = userDialogueSpawner;
        }

        public async Task<bool> PrepareLaunch() {
            logger.Info("Ensuring sigs are set up for all pak files.");
            var result = await Task.Run(() =>
                SigFileHelper.CheckAndCopySigFiles() && SigFileHelper.DeleteOrphanedSigFiles()
            );

            if (result) return true;

            UserDialogueSpawner.DisplayMessage("Failed to prepare sig files. Check the logs for more information.");
            return false;
        }
    }
}