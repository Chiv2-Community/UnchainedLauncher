

using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.UnrealModScanner.ViewModels;
using UnrealModScanner.Services;
//using UnchainedLauncher.UnrealModScanner.ViewModels;

namespace UnchainedLauncher.UnrealModScanner.Views {
    public partial class UnrealModsView : UserControl {
        public AggregateModRegistry? Registry {get; set;}
        public string LastScanPath { get; set;}

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
        private object? GetHiddenProperty(object obj, string name) {
            return obj.GetType()
                      .GetProperty(name, System.Reflection.BindingFlags.Public |
                                         System.Reflection.BindingFlags.NonPublic |
                                         System.Reflection.BindingFlags.Instance)
                      ?.GetValue(obj);
        }

        public UnrealModsView() {
            InitializeComponent();

            //this.DataContextChanged += (s, e) => {
            //    // If we currently have the MainWindowVM, try to "drill down" to the Scanner VM
            //    if (e.NewValue != null && e.NewValue.GetType().Name == "MainWindowVM") {
            //        try {
            //            dynamic dw = e.NewValue;
            //            // Update the DataContext of THIS view to be the sub-ViewModel
            //            if (dw.ModScanTabVM is ModScanTabVM subVM) {
            //                this.DataContext = subVM;
            //                this.Registry = dw.SettingsViewModel.RegistryWindowVM.Registry;
            //            }
            //        }
            //        catch (Exception ex) {
            //            Debug.WriteLine($"Failed to switch DataContext: {ex.Message}");
            //        }
            //    }
            //};

            // This is fucking nasty

            this.DataContextChanged += (s, e) => {
                if (e.NewValue != null && e.NewValue.GetType().Name == "MainWindowVM") {
                    try {
                        dynamic dw = e.NewValue;

                        // 1. Switch the View's DataContext to the Scanner VM
                        if (dw.ModScanTabVM is ModScanTabVM subVM) {
                            this.DataContext = subVM;
                        }

                        // 2. Use Reflection to "Steal" the Registry through the private/internal layers
                        var settings = GetHiddenProperty(e.NewValue, "SettingsViewModel");
                        if (settings != null) {
                            var registryVM = GetHiddenProperty(settings, "RegistryWindowVM");
                            if (registryVM != null) {
                                var registryObj = GetHiddenProperty(registryVM, "Registry");
                                // Cast to the Core type (which is public and accessible)
                                this.Registry = registryObj as AggregateModRegistry;
                            }
                        }
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"Failed to resolve Registry: {ex.Message}");
                    }
                }
            };

        }
        private async void Scan_UnrealMods_Click(object sender, RoutedEventArgs e) {
            if (ModScannerVM == null) return;
            var dlg = new OpenFolderDialog { Title = "Select Pak Directory" };
            if (dlg.ShowDialog() == true) {
                LastScanPath = dlg.FolderName;
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

        private async void PushToRegistry_Click(object sender, RoutedEventArgs e) {
            var vm = ModScannerVM;
            var registry = Registry; // Use our new smart property

            if (vm == null || registry == null) {
                MessageBox.Show("Could not find ViewModel or Registry Service.");
                return;
            }

            var local_Reg  = Registry.ModRegistries.Where(reg => reg is LocalModRegistry).FirstOrDefault();
            if (local_Reg is not LocalModRegistry local_mod_registry) {
                return;
            }
            foreach (var reg in Registry.ModRegistries) {
                    Debug.WriteLine("Reg: {0}", reg.Name);
            }

            // Example: Pushing the first pak found in the latest scan
            //var pak = vm.ScanManifest.Paks.FirstOrDefault();
            var selected_paks = vm.ScanResults.Where(res => res.IsChecked == true).Select(pak => pak.PakPath);
            var paks = vm.ScanManifest.Paks.Where(pak => selected_paks.Contains(pak.PakPath));
            //var pak = selected_paks.FirstOrDefault();
            foreach (var pak in paks) {
                if (pak != null) {
                    string repoUrl = String.Format("https://github.com/LocalOrganization/{0}", pak.PakName.Split(".").First()); // Usually from a TextBox

                    var releaseV3 = V3MetadataGenerator.CreateRelease(pak, repoUrl, "3.3.1");

                    await local_mod_registry.AddRelease(releaseV3, Path.Combine(LastScanPath, pak.PakPath));

                    MessageBox.Show("Mod successfully added to local registry!");
                }
            }
        }
    }
}
