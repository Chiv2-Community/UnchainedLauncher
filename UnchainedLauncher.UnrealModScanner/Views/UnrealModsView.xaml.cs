

using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.UnrealModScanner.ViewModels;
//using UnchainedLauncher.UnrealModScanner.ViewModels;

namespace UnchainedLauncher.UnrealModScanner.Views {
    public partial class UnrealModsView : UserControl {
        public ModScanTabVM? ModScannerVM {
            get {
                if (DataContext is ModScanTabVM vm) return vm;

                if (DataContext != null) {
                    try {
                        dynamic context = DataContext;
                        return context.ModScanTabVM as ModScanTabVM;
                    }
                    catch {
                        return null;
                    }
                }

                return null;
            }
        }
        public UnrealModsView() {
            InitializeComponent();
        }
        private async void Scan_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;

            var dlg = new OpenFolderDialog { Title = "Select Pak Directory" };
            if (dlg.ShowDialog() == true) {
                await ModScannerVM.ScanAsync(dlg.FolderName);
            }
        }

        private void Collapse_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;
            foreach (var res in ModScannerVM.ScanResults) {
                res.IsExpanded = false;
            }
        }

        private void Expand_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;
            foreach (var res in ModScannerVM.ScanResults) {
                res.IsExpanded = true;
            }
        }

        private void Export_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;

            var dlg = new SaveFileDialog {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "mods.json"
            };

            if (dlg.ShowDialog() == true) {
                ModScannerVM.ExportJson(dlg.FileName);
                MessageBox.Show("Export completed", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        //private void Expand_UnrealMods_Click(object sender, RoutedEventArgs e) { /* move logic here */ }
        //private void Collapse_UnrealMods_Click(object sender, RoutedEventArgs e) { /* move logic here */ }
        //private void Scan_UnrealMods_Click(object sender, RoutedEventArgs e) { /* move logic here */ }
        //private void Export_UnrealMods_Click(object sender, RoutedEventArgs e) { /* move logic here */ }
    }
}
