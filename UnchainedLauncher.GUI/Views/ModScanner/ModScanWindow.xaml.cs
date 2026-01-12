
//using Microsoft.Win32;
//using System.Windows;
//using UnchainedLauncher.GUI.ViewModels.Scanning.ModScanner;


//namespace UnchainedLauncher.GUI.Views.ModScanner {
//    public partial class ModScanWindow : Window {
//        private readonly ModScanViewModel _vm = new();

//        public ModScanWindow() {
//            InitializeComponent();
//            DataContext = _vm;
//        }

//        private async void Scan_Click(object sender, RoutedEventArgs e) {
//            var dlg = new OpenFolderDialog {
//                Title = "Select Pak Directory"
//            };

//            if (dlg.ShowDialog() == true) {
//                ScanButton.IsEnabled = false;
//                await _vm.ScanAsync(dlg.FolderName);
//                ScanButton.IsEnabled = true;
//            }
//        }

//        private void Export_Click(object sender, RoutedEventArgs e) {
//            var dlg = new SaveFileDialog {
//                Filter = "JSON Files (*.json)|*.json",
//                FileName = "mods.json"
//            };

//            if (dlg.ShowDialog() == true) {
//                _vm.ExportJson(dlg.FileName);
//                MessageBox.Show("Export complete.", "Export",
//                    MessageBoxButton.OK, MessageBoxImage.Information);
//            }
//        }
//    }

//}
