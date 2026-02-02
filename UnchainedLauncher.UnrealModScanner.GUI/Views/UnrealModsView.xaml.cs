

using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
//using UnchainedLauncher.UnrealModScanner.ViewModels;

namespace UnchainedLauncher.UnrealModScanner.GUI.Views {
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

            this.DataContextChanged += (s, e) => {
                // If we currently have the MainWindowVM, try to "drill down" to the Scanner VM
                if (e.NewValue != null && e.NewValue.GetType().Name == "MainWindowVM") {
                    try {
                        dynamic dw = e.NewValue;
                        // Update the DataContext of THIS view to be the sub-ViewModel
                        if (dw.ModScanTabVM is ModScanTabVM subVM) {
                            this.DataContext = subVM;
                        }
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"Failed to switch DataContext: {ex.Message}");
                    }
                }
            };
        }
        private async void Scan_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;

            var dlg = new OpenFolderDialog { Title = "Select Pak Directory" };
            if (dlg.ShowDialog() == true) {
                await ModScannerVM.ScanAsync(dlg.FolderName);
            }
        }
        private async void Save_UnrealMods_Config_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;

            var dlg = new SaveFileDialog { Title = "Save config as", FileName = "modscan_config.json" };
            if (dlg.ShowDialog() == true) {
                //string configPath = Path.Combine(dlg.FolderName, "modscan_config.json");
                var configPath = dlg.FileName;
                Console.WriteLine("Config not found. Generating default template...");
                ConfigTemplateGenerator.GenerateDefaultConfig(configPath);
                Console.WriteLine($"Template created at {configPath}. Please edit it and restart.");
            }
        }
        private async void Load_UnrealMods_Config_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;

            var dlg = new OpenFileDialog() { Title = "Load Config File", Filter = "JSON Files (*.json)|*.json" };
            if (dlg.ShowDialog() == true) {
                try {
                    var jsonContent = File.ReadAllText(dlg.FileName);
                    var config = System.Text.Json.JsonSerializer.Deserialize<ScanOptions>(jsonContent, ConfigTemplateGenerator.SerializerSettings);

                    if (config != null) {
                        ModScannerVM.LoadedConfig = config;
                        MessageBox.Show($"Config loaded successfully from {dlg.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else {
                        MessageBox.Show("Failed to deserialize config file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex) {
                    MessageBox.Show($"Error loading config: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Collapse_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;
            ModScannerVM.ResultsVisual.SetChildrenExpanded(false);
        }

        private void Expand_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;
            ModScannerVM.ResultsVisual.SetChildrenExpanded(true);
        }

        private void Export_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;

            var dlg = new SaveFileDialog {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "mods.json"
            };

            if (dlg.ShowDialog() == true) {
                ModScannerVM.ExportJson(dlg.FileName, false);
                MessageBox.Show("Export completed", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Export_UnrealMods_Manifest_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;

            var dlg = new SaveFileDialog {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "modmanifest.json"
            };

            if (dlg.ShowDialog() == true) {
                ModScannerVM.ExportJson(dlg.FileName, true);
                MessageBox.Show("Export completed", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}