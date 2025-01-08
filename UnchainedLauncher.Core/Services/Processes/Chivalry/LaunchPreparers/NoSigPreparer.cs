using log4net;
using log4net.Core;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public class NoSigPreparer: IChivalry2LaunchPreparer {
        private readonly ILog logger = LogManager.GetLogger(nameof(NoSigPreparer));
        
        private IUserDialogueSpawner UserDialogueSpawner;

        public NoSigPreparer(IUserDialogueSpawner userDialogueSpawner) {
            UserDialogueSpawner = userDialogueSpawner;
        }
        
        public async Task<bool> PrepareLaunch() {
            logger.Info("Ensuring sigs are removed for all modded paks.");
            var result = await Task.Run(SigFileHelper.RemoveAllNonDefaultSigFiles);
            if (result) return true;
            
            UserDialogueSpawner.DisplayMessage("Failed to remove mod sig files. Check the logs for more details.");
            return false;
        }
    }
}