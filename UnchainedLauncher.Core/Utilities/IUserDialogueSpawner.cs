using log4net;

namespace UnchainedLauncher.Core.Utilities {
    
    
    public interface IUserDialogueSpawner {
        public void DisplayMessage(string message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption);
    }
    
}