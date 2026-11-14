using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.ViewModels.ServersTab;

namespace UnchainedLauncher.GUI.Views.Servers.Sections {
    public partial class ServerModActorsSectionView : UserControl {
        public ServerModActorsSectionView() {
            InitializeComponent();
            Loaded += (_, _) => RefreshLocalMods();
            DataContextChanged += (_, _) => { if (IsLoaded) RefreshLocalMods(); };
        }

        private async void RefreshLocalMods() {
            if (DataContext is ServerConfigurationVM vm) await vm.LocalModScan.RefreshAsync();
        }

        private async void ScanLocalMods_OnClick(object sender, RoutedEventArgs e) {
            e.Handled = true;
            if (DataContext is not ServerConfigurationVM vm) return;
            try { await vm.LocalModScan.ScanAsync(); }
            catch (Exception error) {
                MessageBox.Show($"Pak scan failed: {error.Message}", "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScanLocalMods_OnPreviewMouseButton(object sender, MouseButtonEventArgs e) {
            // Disabled controls don't consume input, so keep it from reaching the card's toggle.
            if (e.ChangedButton == MouseButton.Left && sender is Border { Child: Button { IsEnabled: false } })
                e.Handled = true;
        }

        private void CheckBox_OnChecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerConfigurationVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is ServerModActorVM actor) {
                vm.EnableServerBlueprintMod(actor.Blueprint);
            }
        }

        private void CheckBox_OnUnchecked(object sender, RoutedEventArgs e) {
            if (DataContext is not ServerConfigurationVM vm) return;
            if (sender is CheckBox cb && cb.DataContext is ServerModActorVM actor) {
                vm.DisableServerBlueprintMod(actor.Blueprint);
            }
        }
    }
}