using System.IO;
using System.Reflection;
using System.Windows;
using UnchainedLauncher.GUI.ViewModels.Installer;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views.Installer;
using UnchainedLauncher.GUI.Views;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnchainedLauncher.Core.Installer;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Mods.Registry.Downloader;
using UnchainedLauncher.Core;
using System.Runtime.CompilerServices;
using UnchainedLauncher.Core.Mods;
using System;
using log4net;
using UnchainedLauncher.Core.JsonModels;

namespace UnchainedLauncher.GUI {
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private readonly ILog _log = LogManager.GetLogger(typeof(App));
        
        public App() : base() {}
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            var assembly = Assembly.GetExecutingAssembly();
            if (File.Exists("log4net.config")) {
                log4net.Config.XmlConfigurator.Configure(new FileInfo("log4net.config"));
            } else if (File.Exists("Resources/log4net.config")) {
                // for running in Visual Studio
                log4net.Config.XmlConfigurator.Configure(new FileInfo("Resources/log4net.config"));
            } else {
                using Stream? configStream = assembly.GetManifestResourceStream("UnchainedLauncher.GUI.Resources.log4net.config");
                if (configStream != null) {
                    log4net.Config.XmlConfigurator.Configure(configStream);
                }
            }


            // Init common dependencies
            var githubClient = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("UnchainedLauncher"));
            var installationFinder = new Chivalry2InstallationFinder();
            var installer = new UnchainedLauncherInstaller(githubClient, Current.Shutdown);

            // figure out if we need to install by checking our current working directory
            var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
            var needsInstallation = !installationFinder.IsValidInstallation(currentDirectory);
            
            var forceSkipInstallation = Environment.GetCommandLineArgs().ToList().Contains("--no-install");
            
            _log.Info(Environment.GetCommandLineArgs().ToList());
            
            Window window = 
                needsInstallation && !forceSkipInstallation
                    ? InitializeInstallerWindow(installationFinder, installer) 
                    : InitializeMainWindow(installationFinder, installer);

            window.Show();
        }

        public Window InitializeInstallerWindow(Chivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer) {
            var installationSelectionVM = new InstallationSelectionPageViewModel(installationFinder);
            var versionSelectionVM = new VersionSelectionPageViewModel(installer);
            var installationLogVM = new InstallerLogPageViewModel(
                installer,
                () => from chiv2Installations in installationSelectionVM.Installations
                      where chiv2Installations.IsSelected
                      select chiv2Installations.Path
                ,
                () => versionSelectionVM.SelectedVersion!
            );

            ObservableCollection<IInstallerPageViewModel> installerPageViewModels = new ObservableCollection<IInstallerPageViewModel> {
                installationSelectionVM,
                versionSelectionVM,
                installationLogVM
            };

            var installerWindowVM = new InstallerWindowViewModel(installerPageViewModels, installationSelectionVM.Installations);
            return new InstallerWindow(installerWindowVM);
        }

        public Window InitializeMainWindow(IChivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer) {
            var curDir = new DirectoryInfo(Directory.GetCurrentDirectory());

            var settingsViewModel = SettingsViewModel.LoadSettings(installationFinder, installer, Environment.Exit);

            var modManager = ModManager.ForRegistries(
                new GithubModRegistry("Chiv2-Community", "C2ModRegistry", HttpPakDownloader.GithubPakDownloader)
            );

            var chiv2Launcher = new Chivalry2Launcher();
            var serversViewModel = new ServersViewModel(settingsViewModel, null);
            var launcherViewModel = new LauncherViewModel(settingsViewModel, modManager, chiv2Launcher);
            var serverLauncherViewModel = ServerLauncherViewModel.LoadSettings(launcherViewModel, settingsViewModel, serversViewModel, modManager);
            var modListViewModel = new ModListViewModel(modManager);

            modListViewModel.RefreshModListCommand.Execute(null);

            var envArgs = Environment.GetCommandLineArgs().ToList();

            if (envArgs.Contains("--startvanilla"))
                launcherViewModel.LaunchVanilla(false);
            else if (envArgs.Contains("--startmodded"))
                launcherViewModel.LaunchVanilla(true);
            else if (envArgs.Contains("--startunchained"))
                launcherViewModel.LaunchUnchained(None);

            var mainWindowViewModel = new MainWindowViewModel(
                launcherViewModel,
                modListViewModel,
                settingsViewModel,
                serverLauncherViewModel,
                serversViewModel
            );

            return new MainWindow(mainWindowViewModel);
        }
    }
}
