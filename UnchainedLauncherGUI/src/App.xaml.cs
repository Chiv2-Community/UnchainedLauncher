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

namespace UnchainedLauncher.GUI {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        public App() : base() {
            var assembly = Assembly.GetExecutingAssembly();
            if (File.Exists("log4net.config")) {
                log4net.Config.XmlConfigurator.Configure(new FileInfo("log4net.config"));
            } else if (File.Exists("Resources/log4net.config")) {
                // for running in Visual Studio
                log4net.Config.XmlConfigurator.Configure(new FileInfo("Resources/log4net.config"));
            } else {
                using Stream? configStream = assembly.GetManifestResourceStream("UnchainedLauncherGUI.Resources.log4net.config");
                if (configStream != null) {
                    log4net.Config.XmlConfigurator.Configure(configStream);
                }
            }
        }
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);


            // Init common dependencies
            Octokit.GitHubClient githubClient = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("UnchainedLauncher"));

            var installationFinder = new Chivalry2InstallationFinder();
            IUnchainedLauncherInstaller installer = new UnchainedLauncherInstaller(githubClient, Current.Shutdown);

            // figure out if we need to install
            var currentDirectory = Directory.GetParent(Assembly.GetExecutingAssembly().Location);
            var needsInstallation = currentDirectory != null && !installationFinder.IsValidInstallation(currentDirectory);

            // initialize the window
            Window window = 
                needsInstallation 
                    ? InitializeInstallerWindow(installationFinder, installer) 
                    : InitializeMainWindow(installationFinder, installer);

            window.Show();
        }

        public static Window InitializeInstallerWindow(Chivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer) {
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

            var installerWindowVM = new InstallerWindowViewModel(installerPageViewModels);
            return new InstallerWindow(installerWindowVM);
        }

        public static Window InitializeMainWindow(IChivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer) {
            return new MainWindow(installationFinder, installer);
        }
    }
}
