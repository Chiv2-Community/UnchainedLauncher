using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.Servers {
    public partial class ServerConfigurationForm : UserControl {
        public ServerConfigurationForm() {
            InitializeComponent();
        }

        private void CheckBox_OnChecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerConfigurationVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is Release release) {
                vm.EnableServerMod(release);
            }
        }

        private void CheckBox_OnUnchecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerConfigurationVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is Release release) {
                vm.DisableServerMod(release);
            }
        }
    }
}