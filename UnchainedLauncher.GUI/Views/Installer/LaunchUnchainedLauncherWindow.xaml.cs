using System.Collections.Generic;
using System.Linq;
using UnchainedLauncher.GUI.ViewModels.Installer;
using Wpf.Ui.Controls;

namespace UnchainedLauncher.GUI.Views.Installer {
    /// <summary>
    /// Interaction logic for LaunchUnchainedLauncherWindow.xaml
    /// </summary>
    public partial class LaunchUnchainedLauncherWindow : FluentWindow {
        public LaunchUnchainedLauncherWindow() {
            InitializeComponent();
        }

        public static void Show(IEnumerable<InstallationTargetViewModel> targets) {
            var window = new LaunchUnchainedLauncherWindow();
            var vm = new LaunchUnchainedLauncherWindowViewModel(targets, window.Close);

            // Don't even show the window if there's only 1 valid launch candidate
            if (targets.Filter(x => x.IsSelected).Count() == 1) {
                vm.Launch();
                window.Close();
            }
            else {
                window.DataContext = vm;
                window.ShowDialog();
            }
        }
    }
}