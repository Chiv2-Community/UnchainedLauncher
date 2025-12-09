using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.Servers {
    /// <summary>
    ///     Interaction logic for ServerTemplate.xaml
    /// </summary>
    public partial class ServerTemplate : UserControl {
        public ServerTemplate() {
            InitializeComponent();
        }

        private void CheckBox_OnChecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerTemplateVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is Release release) {
                vm.EnableServerMod(release);
            }
        }

        private void CheckBox_OnUnchecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerTemplateVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is Release release) {
                vm.DisableServerMod(release);
            }
        }
    }
}