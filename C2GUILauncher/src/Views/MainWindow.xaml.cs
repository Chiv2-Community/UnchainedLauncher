using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using C2GUILauncher.src;
using C2GUILauncher.src.ViewModels;
using C2GUILauncher.ViewModels;
using PropertyChanged;
using System;
using System.Diagnostics;
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

        public int ShowInstallRequestFor(string TargetDir, InstallationType InstallType)
        {
            var InstallTypeStr = InstallType == InstallationType.Steam ? "Steam" : "Epic Games";
            if (TargetDir.Length > 0) {
                MessageBoxResult res = MessageBox.Show(
                   $"Detected install location ({InstallType}):\n\n" +
                   $"{TargetDir} \n\n" +
                   $"Install the launcher for {InstallType} at this location?\n\n"
                   , $"Install Launcher ({InstallType})", MessageBoxButton.YesNoCancel);

                if (res == MessageBoxResult.Yes) {
                    InstallerViewModel.AttemptInstall(TargetDir, InstallType);
                    return 1;
                }
                else if (res == MessageBoxResult.No) {
                    return 0;
                }
            }
            return -1;
        }

        public MainWindow() {
            InitializeComponent();
            var EGSDir = InstallHelpers.FindEGSDir();
            var SteamDir = InstallHelpers.FindSteamDir();
            bool needsClose = false;
            int res = 0;
            string exeName = Process.GetCurrentProcess().ProcessName;
            string CurDir = System.IO.Directory.GetCurrentDirectory();

            if (exeName != "Chivalry2Launcher" && !Path.Equals(CurDir, SteamDir))
            {
                int SteamRes = ShowInstallRequestFor(SteamDir, InstallationType.Steam);
                int EGSRes = -1;
                if (SteamRes >= 0)
                    EGSRes = ShowInstallRequestFor(EGSDir, InstallationType.EpicGamesStore);
                needsClose = EGSRes > 0 || SteamRes > 0;

                if (!needsClose)
                    needsClose = InstallerViewModel.AttemptInstall("", InstallationType.NotSet);
            }

            if (needsClose)
            {
                MessageBox.Show($"The launcher will now close to perform the operation. It should restart itself in 1 second.");
                this.Close();
            }

            this.ModManager = ModManager.ForRegistry(
                "Chiv2-Community",
                "C2ModRegistry",
                "TBL\\Content\\Paks"
            );

            this.SettingsViewModel = SettingsViewModel.LoadSettings();
            if (this.SettingsViewModel.InstallationType == InstallationType.NotSet)
            {
                if (Path.Equals(CurDir, EGSDir))
                    this.SettingsViewModel.InstallationType = InstallationType.EpicGamesStore;
                else if (Path.Equals(CurDir, SteamDir))
                    this.SettingsViewModel.InstallationType = InstallationType.Steam;
            }
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
