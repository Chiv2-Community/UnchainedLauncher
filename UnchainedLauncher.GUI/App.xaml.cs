using log4net;
using log4net;
using System;
using System;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.CompilerServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core;
using UnchainedLauncher.Core;
using UnchainedLauncher.Core;
using UnchainedLauncher.Core.Installer;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI {
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private readonly ILog _log = LogManager.GetLogger(typeof(App));

        public App() : base() { }
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            var assembly = Assembly.GetExecutingAssembly();
            if (File.Exists("log4net.config")) {
                log4net.Config.XmlConfigurator.Configure(new FileInfo("log4net.config"));
            }
            else if (File.Exists("Resources/log4net.config")) {
                // for running in Visual Studio
                log4net.Config.XmlConfigurator.Configure(new FileInfo("Resources/log4net.config"));
            }
            else {
                using Stream? configStream = assembly.GetManifestResourceStream("UnchainedLauncher.GUI.Resources.log4net.config");
                if (configStream != null) {
                    log4net.Config.XmlConfigurator.Configure(configStream);
                }
            }


            // Init common dependencies
            var githubClient = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("UnchainedLauncher"));
            var unchainedLauncherReleaseLocator = new GithubReleaseLocator(githubClient, "Chiv2-Community", "UnchainedLauncher");
            var pluginReleaseLocator = new GithubReleaseLocator(githubClient, "Chiv2-Community", "UnchainedPlugin");


            var installationFinder = new Chivalry2InstallationFinder();
            var installer = new UnchainedLauncherInstaller(Environment.Exit);

            // figure out if we need to install by checking our current working directory
            var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
            var needsInstallation = !installationFinder.IsValidInstallation(currentDirectory);

            var forceSkipInstallation = Environment.GetCommandLineArgs().ToList().Contains("--no-install");

            if (forceSkipInstallation && needsInstallation)
                _log.Info("Skipping installation");

            Window window =
                needsInstallation && !forceSkipInstallation
                    ? InitializeInstallerWindow(installationFinder, installer, unchainedLauncherReleaseLocator)
                    : InitializeMainWindow(installationFinder, installer, unchainedLauncherReleaseLocator, pluginReleaseLocator);

            window.Show();
        }

        public Window InitializeInstallerWindow(Chivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator) {
            var installationSelectionVM = new InstallationSelectionPageViewModel(installationFinder);
            var versionSelectionVM = new VersionSelectionPageViewModel(launcherReleaseLocator);
            var installationLogVM = new InstallerLogPageViewModel(
                installer,
                () => from chiv2Installations in installationSelectionVM.Installations
                      where chiv2Installations.IsSelected
                      select chiv2Installations.Path
                ,
                () => versionSelectionVM.SelectedVersion!
            );

            var installerPageViewModels = new ObservableCollection<IInstallerPageViewModel> {
                installationSelectionVM,
                versionSelectionVM,
                installationLogVM
            };

            var installerWindowVM = new InstallerWindowViewModel(installerPageViewModels, installationSelectionVM.Installations);
            return new InstallerWindow(installerWindowVM);
        }

        public Window InitializeMainWindow(IChivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator, IReleaseLocator pluginReleaseLocator) {
            var settingsViewModel = SettingsViewModel.LoadSettings(installationFinder, installer, launcherReleaseLocator, Environment.Exit);

            var modManager = ModManager.ForRegistries(
                new GithubModRegistry("Chiv2-Community", "C2ModRegistry", HttpPakDownloader.GithubPakDownloader)
            );


            var chiv2Launcher = new Chivalry2Launcher();
            var serversViewModel = new ServersViewModel(settingsViewModel, null);
            var launcherViewModel = new LauncherViewModel(settingsViewModel, modManager, chiv2Launcher, pluginReleaseLocator);
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