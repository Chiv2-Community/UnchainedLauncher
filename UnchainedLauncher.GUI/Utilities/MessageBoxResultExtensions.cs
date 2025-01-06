using System.Windows;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Utilities {
    internal static class MessageBoxResultExtensions {
        public static UserDialogueChoice ToMessageBoxResult(this MessageBoxResult result) =>
            result switch {
                MessageBoxResult.Yes or MessageBoxResult.OK => UserDialogueChoice.Yes,
                MessageBoxResult.Cancel or MessageBoxResult.None => UserDialogueChoice.Cancel,
                MessageBoxResult.No => UserDialogueChoice.No,
            };
    }
}