using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.Views.Servers.Sections {
    public partial class ServerModActorsSectionView : UserControl {
        public ServerModActorsSectionView() {
            InitializeComponent();
        }

        private void CheckBox_OnChecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerConfigurationVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is BlueprintDto blueprint) {
                vm.EnableServerBlueprintMod(blueprint);
            }
        }

        private void CheckBox_OnUnchecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerConfigurationVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is BlueprintDto blueprint) {
                vm.DisableServerBlueprintMod(blueprint);
            }
        }
    }
}