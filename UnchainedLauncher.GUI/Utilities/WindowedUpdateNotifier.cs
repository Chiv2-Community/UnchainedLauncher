using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views;

namespace UnchainedLauncher.GUI.Utilities {
    public class WindowedUpdateNotifier: IUpdateNotifier {
        public UserDialogueChoice? Notify(string titleText, string messageText, string yesButtonText, string noButtonText,
            string? cancelButtonText, IEnumerable<DependencyUpdate> updates) {
            var result = UpdatesWindow.Show(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, 
                from update in updates 
                select new DependencyUpdateViewModel(update)
            );

            return result?.ToMessageBoxResult();
        }
    }
}