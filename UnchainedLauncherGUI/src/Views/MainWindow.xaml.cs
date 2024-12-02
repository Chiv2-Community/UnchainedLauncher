using UnchainedLauncher.GUI.ViewModels;
using log4net;
using PropertyChanged;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.Core;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Mods.Registry.Resolver;
using System.Linq;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Installer;

namespace UnchainedLauncher.GUI.Views
{
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : Window {

        public ModListViewModel ModManagerViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public LauncherViewModel LauncherViewModel { get; }
        public ServerLauncherViewModel ServerSettingsViewModel { get; }
        public ServersViewModel ServersViewModel { get; }
        public bool DisableSaveSettings { get; set; }

        private readonly ModManager ModManager;

        private static readonly ILog logger = LogManager.GetLogger(nameof(MainWindow));

        public MainWindow(IChivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer) {
            try {

                InitializeComponent();

                Assembly assembly = Assembly.GetExecutingAssembly();
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);

                logger.Info("Checking if installation is necessary...");
                var curDir = Directory.GetParent(assembly.Location);
                var exeName = Process.GetCurrentProcess().ProcessName;


                this.ModManager = ModManager.ForRegistries(
                    new GithubModRegistry("Chiv2-Community", "C2ModRegistry", HttpPakDownloader.GithubPakDownloader)
                );

                var chiv2Launcher = new Chivalry2Launcher();

                this.SettingsViewModel = SettingsViewModel.LoadSettings(this, installer);
                if (this.SettingsViewModel.InstallationType == InstallationType.NotSet && curDir != null) {
                    if (installationFinder.IsEGSDir(curDir))
                        this.SettingsViewModel.InstallationType = InstallationType.EpicGamesStore;
                    else if (installationFinder.IsSteamDir(curDir))
                        this.SettingsViewModel.InstallationType = InstallationType.Steam;
                }
                this.ServersViewModel = new ServersViewModel(SettingsViewModel, null);

                this.LauncherViewModel = new LauncherViewModel(this, SettingsViewModel, ModManager, chiv2Launcher);
                this.ServerSettingsViewModel = ServerLauncherViewModel.LoadSettings(LauncherViewModel, SettingsViewModel, ServersViewModel, ModManager);

                this.ModManagerViewModel = new ModListViewModel(ModManager);

                this.ServersTab.DataContext = this.ServersViewModel;
                this.SettingsTab.DataContext = this.SettingsViewModel;
                this.ModManagerTab.DataContext = this.ModManagerViewModel;
                this.LauncherTab.DataContext = this.LauncherViewModel;
                this.ServerSettingsTab.DataContext = this.ServerSettingsViewModel;

                DisableSaveSettings = false;
                this.Closed += MainWindow_Closed;

            } catch (Exception e) {
                logger.Error("Initialization Failed", e);
                throw;
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e) {
            this.ServersViewModel.Dispose();

            if (DisableSaveSettings) return;

            this.SettingsViewModel.Dispose();
            this.ServerSettingsViewModel.Dispose();
        }

        private bool modManagerLoaded = false;
        private void TabSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (Tabs.SelectedItem == null) return;

            logger.Info("Opened Tab: " + ((TabItem)Tabs.SelectedItem).Header.ToString());
            if (!modManagerLoaded && ModManagerTab.IsSelected) {
                try {
                    ModManagerViewModel.RefreshModListCommand.Execute(null);
                    modManagerLoaded = true;
                } catch (Exception ex) {
                    logger.Error(ex);
                }
            }
        }
    }

}
