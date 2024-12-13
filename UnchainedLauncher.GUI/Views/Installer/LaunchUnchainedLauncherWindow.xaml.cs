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
using UnchainedLauncher.GUI.ViewModels.Installer;

namespace UnchainedLauncher.GUI.Views.Installer {
    /// <summary>
    /// Interaction logic for LaunchUnchainedLauncherWindow.xaml
    /// </summary>
    public partial class LaunchUnchainedLauncherWindow : Window {
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