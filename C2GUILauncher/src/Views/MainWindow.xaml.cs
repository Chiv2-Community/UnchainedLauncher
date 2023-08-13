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

        public int ShowInstallRequestFor(string TargetDir, string InstallType)
        {
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

            if (exeName != "Chivalry2Launcher")
            {
                int EGSRes = ShowInstallRequestFor(EGSDir, "Epic Games");
                int SteamRes = -1;
                if (EGSRes >= 0)
                    SteamRes = ShowInstallRequestFor(SteamDir, "Steam");
                needsClose = EGSRes > 0 || SteamRes > 0;
            }

            if (!needsClose)
                needsClose = InstallerViewModel.AttemptInstall("","");

            if (needsClose)
                this.Close();

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
