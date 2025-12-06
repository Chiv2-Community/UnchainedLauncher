using log4net;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    ///     Interaction logic for MessageBoxEx.xaml
    /// </summary>
    public partial class UpdatesWindow : UnchainedWindow {
        private static readonly ILog logger = LogManager.GetLogger(nameof(UpdatesWindow));

        public UpdatesWindow(string titleText, string messageText, string yesButtonText, string noButtonText,
            string? cancelButtonText, IEnumerable<DependencyUpdateViewModel> updates) {
            ViewModel = new UpdatesWindowVM(titleText, messageText, yesButtonText, noButtonText, cancelButtonText,
                updates, Close);
            DataContext = ViewModel;
            InitializeComponent();
        }

        public UpdatesWindowVM ViewModel { get; }

        public static MessageBoxResult? Show(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdateViewModel> updates) {
            if (!updates.Any()) {
                logger.Info("No updates available");
                return null;
            }

            var message = $"Found {updates.Count()} updates available.\n\n";
            message += string.Join("\n",
                updates.Select(x => $"- {x.Update.Name} {x.Update.CurrentVersion} -> {x.Update.LatestVersion}"));
            message.Split("\n").ToList().ForEach(x => logger.Info(x));

            var window = new UpdatesWindow(titleText, messageText, yesButtonText, noButtonText, cancelButtonText,
                updates);
            window.ShowDialog();

            var result = window.ViewModel.Result;
            logger.Info("User Selects: " + result);
            return result;
        }

        public static MessageBoxResult? Show(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, params DependencyUpdateViewModel[] updates) {
            return Show(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates.AsEnumerable());
        }
    }
}