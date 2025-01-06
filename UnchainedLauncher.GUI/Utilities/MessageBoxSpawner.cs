using System.Windows;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Utilities {
    public class MessageBoxSpawner: IUserDialogueSpawner {
        public void DisplayMessage(string message) => MessageBox.Show(message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) => 
            MessageBox.Show(message, caption, MessageBoxButton.YesNo).ToMessageBoxResult();
    }
}