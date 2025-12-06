using System.Collections.Generic;
using System.Linq;
using System.Windows;
using UnchainedLauncher.GUI.Views;
using UnchainedLauncher.GUI.ViewModels.Installer;

namespace UnchainedLauncher.GUI.Views.Installer {
    /// <summary>
    /// Interaction logic for LaunchUnchainedLauncherWindow.xaml
    /// </summary>
    public partial class LaunchUnchainedLauncherWindow : UnchainedWindow {
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