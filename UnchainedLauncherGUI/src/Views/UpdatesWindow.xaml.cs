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
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    /// Interaction logic for MessageBoxEx.xaml
    /// </summary>
    public partial class UpdatesWindow : Window {
        private static readonly ILog logger = LogManager.GetLogger(nameof(SettingsViewModel));

        public UpdatesWindowViewModel ViewModel { get; private set; }
        public MessageBoxResult Result => ViewModel.Result;

        public UpdatesWindow(string titleText, string messageText, string yesButtonText, string noButtonText, Option<string> cancelButtonText, IEnumerable<DependencyUpdate> updates) {
            InitializeComponent();
            ViewModel = new UpdatesWindowViewModel(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates, Close);
            DataContext = ViewModel;
        }

        public static MessageBoxResult Show(string titleText, string messageText, string yesButtonText, string noButtonText, Option<string> cancelButtonText, IEnumerable<DependencyUpdate> updates) {
            MessageBoxResult result;
            if (updates.Count() == 0) {
                result = MessageBox.Show("No updates available");
                logger.Info("No updates available");
            } else {
                var message = $"Found {updates.Count()} updates available.\n\n";
                message += string.Join("\n", updates.Select(x => $"- {x.Name} {x.CurrentVersion} -> {x.LatestVersion}"));
                message.Split("\n").ToList().ForEach(x => logger.Info(x));

                var window = new UpdatesWindow(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates);
                window.ShowDialog();

                result = window.ViewModel.Result;
                logger.Info("User Selects: " + result.ToString());
            }

            return result;
        }
    }
}