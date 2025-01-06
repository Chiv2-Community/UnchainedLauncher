using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using log4net;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for MessageBoxEx.xaml
    /// </summary>
    public partial class UpdatesWindow : Window {

        private static readonly ILog logger = LogManager.GetLogger(nameof(SettingsViewModel));

        public UpdatesWindowViewModel ViewModel { get; private set; }

        public UpdatesWindow(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdateViewModel> updates) {
            ViewModel = new UpdatesWindowViewModel(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates, Close);
            DataContext = ViewModel;
            InitializeComponent();
        }

        public static MessageBoxResult? Show(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdateViewModel> updates) {
            if (!updates.Any()) {
                logger.Info("No updates available");
                return null;
            }

            var message = $"Found {updates.Count()} updates available.\n\n";
            message += string.Join("\n", updates.Select(x => $"- {x.Update.Name} {x.Update.CurrentVersion} -> {x.Update.LatestVersion}"));
            message.Split("\n").ToList().ForEach(x => logger.Info(x));

            var window = new UpdatesWindow(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates);
            window.ShowDialog();

            MessageBoxResult result = window.ViewModel.Result;
            logger.Info("User Selects: " + result);
            return result;
        }

        public static MessageBoxResult? Show(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, params DependencyUpdateViewModel[] updates) {
            return Show(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates.AsEnumerable());
        }
    }
}