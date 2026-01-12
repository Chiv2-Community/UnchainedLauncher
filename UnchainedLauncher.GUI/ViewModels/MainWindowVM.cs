using System;
using System.ComponentModel;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.UnrealModScanner.Scanning;

namespace UnchainedLauncher.GUI.ViewModels {
    public partial class MainWindowVM : INotifyPropertyChanged, IDisposable {
        public HomeVM HomeVM { get; }
        public ModListVM ModListViewModel { get; }
        public ModScanTabVM ModScanTabVM { get; }
    //    public MainWindowVM(
    //    ModListVM modListViewModel,
    //    HomeVM homeVM
    //    /* other existing deps */
    //) {
    //        HomeVM = homeVM;
    //        ModListViewModel = modListViewModel;

    //        // 👇 Create scanners explicitly
    //    }
        public SettingsVM SettingsViewModel { get; }
        public ServersTabVM ServersTab { get; }

        public MainWindowVM(HomeVM launcherVM,
                            ModListVM modListViewModel,
                            SettingsVM settingsViewModel,
                            ServersTabVM serversTab) {
            HomeVM = launcherVM;
            ModListViewModel = modListViewModel;
            SettingsViewModel = settingsViewModel;
            ServersTab = serversTab;


            var replacementScanner = new AssetReplacementScanner(
                    [
                        "Abilities",
                        "AI",
                        "Animation",
                        "Audio",
                        "Blueprint",
                        "Characters",
                        "Cinematics",
                        "Collections",
                        "Config",
                        "Custom_Lens_Flare_VFX",
                        "Customization",
                        "Debug",
                        "Developers",
                        "Environments",
                        "FX",
                        "Game",
                        "GameModes",
                        "Gameplay",
                        "Interactables",
                        "Inventory",
                        "Localization",
                        "MapGen",
                        "Maps",
                        "MapsTest",
                        "Materials",
                        "Meshes",
                        "Trailer_Cinematic",
                        "UI",
                        "Weapons",
                        "Engine",
                        "Mannequin",
                    ]
                );

            var exportScanner = new PackageExportScanner();

            var modScanner = new UnchainedLauncher.UnrealModScanner.Scanning.ModScanner(replacementScanner, exportScanner);
            ModScanTabVM = new ModScanTabVM(modScanner);
        }

        public void Dispose() {
            SettingsViewModel.Dispose();
            // TODO: Servers tab needs to be disposable
        }
    }
}