using Microsoft.Win32;
using PropertyChanged;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : UnchainedWindow {
        public MainWindow(MainWindowVM vm) {
            DataContext = ViewModel = vm;
            InitializeComponent();

            // Bridge VM visibility intent to actual Window.Show/Hide calls.
            // Binding to Window.Visibility is not reliable for showing/hiding a Window in WPF.
            vm.HomeVM.PropertyChanged += HomeVmOnPropertyChanged;

            Closed += MainWindow_Closed;
        }
        private async void Scan_UnrealMods_Click(object sender, RoutedEventArgs e) {
            var dlg = new OpenFolderDialog { Title = "Select Pak Directory" };
            if (dlg.ShowDialog() == true)
                await ViewModel.ModScanTabVM.ScanAsync(dlg.FolderName);
        }
        private async void Collapse_UnrealMods_Click(object sender, RoutedEventArgs e) {
            foreach (var res in ViewModel.ModScanTabVM.ScanResults) {
                res.IsExpanded = false;
            }
        }
        private async void Expand_UnrealMods_Click(object sender, RoutedEventArgs e) {
            foreach (var res in ViewModel.ModScanTabVM.ScanResults) {
                res.IsExpanded = true;
            }
        }

        private void Export_UnrealMods_Click(object sender, RoutedEventArgs e) {
            var dlg = new SaveFileDialog {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "mods.json"
            };
            if (dlg.ShowDialog() == true) {
                ViewModel.ModScanTabVM.ExportJson(dlg.FileName);
                MessageBox.Show("Export completed", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public MainWindowVM ViewModel { get; }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            ViewModel.Dispose();
        }

        private void HomeVmOnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(HomeVM.MainWindowVisibility)) return;

            var desired = ViewModel.HomeVM.MainWindowVisibility;
            if (desired == Visibility.Visible) {
                if (!IsVisible) Show();
            }
            else {
                if (IsVisible) Hide();
            }
        }
    }
}