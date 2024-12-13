using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LanguageExt.UnsafeValueAccess;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for MessageBoxEx.xaml
    /// </summary>
    public partial class UpdatesWindow : Window {

        private static readonly ILog logger = LogManager.GetLogger(nameof(SettingsViewModel));

        public UpdatesWindowViewModel ViewModel { get; private set; }
        public MessageBoxResult Result => ViewModel.Result;

        public UpdatesWindow(string titleText, string messageText, string yesButtonText, string noButtonText, Option<string> cancelButtonText, IEnumerable<DependencyUpdate> updates) {
            ViewModel = new UpdatesWindowViewModel(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates, Close);
            DataContext = ViewModel;
            InitializeComponent();
        }

        public static Option<MessageBoxResult> Show(string titleText, string messageText, string yesButtonText, string noButtonText, Option<string> cancelButtonText, IEnumerable<DependencyUpdate> updates)
        {
            if (!updates.Any()) {
                logger.Info("No updates available");
                return None;
            }

            var message = $"Found {updates.Count()} updates available.\n\n";
            message += string.Join("\n", updates.Select(x => $"- {x.Name} {x.CurrentVersion} -> {x.LatestVersion}"));
            message.Split("\n").ToList().ForEach(x => logger.Info(x));

            var window = new UpdatesWindow(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates);
            window.ShowDialog();

            MessageBoxResult result = window.ViewModel.Result;
            logger.Info("User Selects: " + result);
            return Some(result);
        }
        
        public static MessageBoxResult Show(string titleText, string messageText, string yesButtonText, string noButtonText, Option<string> cancelButtonText, DependencyUpdate update)
        {
            return Show(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, new[] { update }).ValueUnsafe();
        }
    }
}