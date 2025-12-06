using System.Windows;
using UnchainedLauncher.GUI.ViewModels.Installer;

namespace UnchainedLauncher.GUI.Views.Installer {
    public partial class InstallerWindow : UnchainedLauncher.GUI.Views.UnchainedWindow {
        public InstallerWindow(InstallerWindowViewModel installerWindowViewModel) {
            DataContext = installerWindowViewModel;
            InitializeComponent();

            installerWindowViewModel.PropertyChanged += (sender, args) => {
                if (args.PropertyName == nameof(installerWindowViewModel.Finished)) {
                    if (installerWindowViewModel.Finished) {
                        Close();
                    }
                }
                else if (args.PropertyName == nameof(installerWindowViewModel.WindowVisibility)) {
                    // This is a janky hack because my visibility binding isn't working
                    if (installerWindowViewModel.WindowVisibility == Visibility.Hidden) {
                        Hide();
                    }
                }
            };
        }
    }
}