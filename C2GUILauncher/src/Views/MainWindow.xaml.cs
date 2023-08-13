using C2GUILauncher.Mods;
using C2GUILauncher.src;
using C2GUILauncher.src.ViewModels;
using C2GUILauncher.ViewModels;
using PropertyChanged;
using System;
using System.IO;
using System.Windows;

namespace C2GUILauncher {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : Window {

        public ModListViewModel ModManagerViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public LauncherViewModel LauncherViewModel { get; }

        private readonly ModManager ModManager;


        public MainWindow() {
            InitializeComponent();
            var EGSDir = InstallHelpers.FindEGSDir();
            var SteamDir = InstallHelpers.FindSteamDir();

            var needsClose = InstallerViewModel.AttemptInstall();
            if (needsClose)
                this.Close();
            else
            {
                MessageBoxResult dialogResult = MessageBox.Show(
                   $"EGS:\n{EGSDir} \n\n" +
                   $"Steam:\n{SteamDir}\n\n"
                   , "Detected dirs", MessageBoxButton.OK);
            }

            this.ModManager = ModManager.ForRegistry(
                "Chiv2-Community",
                "C2ModRegistry",
                "TBL\\Content\\Paks"
            );

            this.SettingsViewModel = SettingsViewModel.LoadSettings();
            this.ModManagerViewModel = new ModListViewModel(ModManager);
            this.LauncherViewModel = new LauncherViewModel(SettingsViewModel, ModManager);

            this.SettingsTab.DataContext = this.SettingsViewModel;
            this.ModManagerTab.DataContext = this.ModManagerViewModel;
            this.LauncherTab.DataContext = this.LauncherViewModel;

            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            this.SettingsViewModel.SaveSettings();
        }

    }

}
