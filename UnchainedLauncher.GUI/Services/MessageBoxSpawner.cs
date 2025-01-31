using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views;

namespace UnchainedLauncher.GUI.Services {
    public class MessageBoxSpawner : IUserDialogueSpawner {
        public void DisplayMessage(string message) => MessageBox.Show(message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) =>
            MessageBox.Show(message, caption, MessageBoxButton.YesNo).ToMessageBoxResult();

        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText, string noButtonText,
            string? cancelButtonText, IEnumerable<DependencyUpdate> updates) {
            var result = UpdatesWindow.Show(titleText, messageText, yesButtonText, noButtonText, cancelButtonText,
                from update in updates
                select new DependencyUpdateViewModel(update)
            );

            return result?.ToMessageBoxResult();
        }
    }

    internal static class MessageBoxResultExtensions {
        public static UserDialogueChoice ToMessageBoxResult(this MessageBoxResult result) =>
            result switch {
                MessageBoxResult.Yes or MessageBoxResult.OK => UserDialogueChoice.Yes,
                MessageBoxResult.Cancel or MessageBoxResult.None => UserDialogueChoice.Cancel,
                MessageBoxResult.No => UserDialogueChoice.No,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
            };
    }
}