using LanguageExt;
using log4net;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.ViewModels.Installer;
using UnchainedLauncher.GUI.ViewModels.Registry;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.GUI.Views;
using UnchainedLauncher.GUI.Views.Installer;
using Application = System.Windows.Application;

namespace UnchainedLauncher.GUI {
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private static readonly ILog _log = LogManager.GetLogger(typeof(App));
        
        protected override void OnStartup(StartupEventArgs e) {
            try {
                base.OnStartup(e);
                
                AppDomain.CurrentDomain.UnhandledException +=
                    (sender, args) => {
                        var ex = (Exception)args.ExceptionObject;
                        _log.Fatal("Unhandled exception", ex);
                        
                        File.WriteAllText("crash.log", ex.ToString());
                        var currentDirectory = Directory.GetCurrentDirectory();
                        MessageBox.Show($"An unhandled exception occurred. Please report this to a developer with {currentDirectory}\\crash.log ", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    };

                // Capture the UI thread's SynchronizationContext for use throughout the application
                UISynchronizationContext.Initialize();

                var assembly = Assembly.GetExecutingAssembly();
                if (File.Exists("log4net.config")) {
                    log4net.Config.XmlConfigurator.Configure(new FileInfo("log4net.config"));
                }
                else if (File.Exists("Resources/log4net.config")) {
                    // for running in Visual Studio
                    log4net.Config.XmlConfigurator.Configure(new FileInfo("Resources/log4net.config"));
                }
                else {
                    using var configStream =
                        assembly.GetManifestResourceStream("UnchainedLauncher.GUI.Resources.log4net.config");
                    if (configStream != null) {
                        log4net.Config.XmlConfigurator.Configure(configStream);
                    }
                }
                
                // Init common dependencies
                var githubClient = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("UnchainedLauncher"));
                var unchainedLauncherReleaseLocator =
                    new GithubReleaseLocator(githubClient, "Chiv2-Community", "UnchainedLauncher");
                var pluginReleaseLocator = new GithubReleaseLocator(githubClient, "Chiv2-Community", "UnchainedPlugin");


                var installationFinder = new Chivalry2InstallationFinder();
                var installer = new UnchainedLauncherInstaller(Shutdown);

                // figure out if we need to install by checking our current working directory
                var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
                var needsInstallation = !installationFinder.IsValidInstallation(currentDirectory);

                var forceSkipInstallation = Environment.GetCommandLineArgs().ToList().Contains("--no-install");

                if (forceSkipInstallation && needsInstallation)
                    _log.Info("Skipping installation");

                var window =
                    needsInstallation && !forceSkipInstallation
                        ? InitializeInstallerWindow(installationFinder, installer, unchainedLauncherReleaseLocator)
                        : InitializeMainWindow(installationFinder, installer, unchainedLauncherReleaseLocator,
                            pluginReleaseLocator);

                window?.Show();
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error starting application: {ex.Message}");
                if (ex.StackTrace != null)
                    Debug.WriteLine(ex.StackTrace!);

                MessageBox.Show($"Failed to start application. Please report this to a developer: {ex.Message}", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Removed class-level command bindings; handled by Views.UnchainedWindow type.

        private Window? InitializeInstallerWindow(Chivalry2InstallationFinder installationFinder,
            IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator) {
            var installationSelectionVM = new InstallationSelectionPageViewModel(installationFinder);
            var versionSelectionVM = new VersionSelectionPageViewModel(launcherReleaseLocator);
            var installationLogVM = new InstallerLogPageViewModel(
                installer,
                () =>
                    from chiv2Installations in installationSelectionVM.Installations
                    where chiv2Installations.IsSelected
                    select chiv2Installations.Path
                ,
                () => versionSelectionVM.SelectedVersion!
            );

            var installerPageViewModels = new ObservableCollection<IInstallerPageViewModel> {
                installationSelectionVM, versionSelectionVM, installationLogVM
            };

            var installerWindowVM =
                new InstallerWindowViewModel(installerPageViewModels, installationSelectionVM.Installations);
            return new InstallerWindow(installerWindowVM);
        }

        private Window? InitializeMainWindow(IChivalry2InstallationFinder installationFinder,
            IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator,
            IReleaseLocator pluginReleaseLocator) {
            var userDialogueSpawner = new MessageBoxSpawner();
            var registryWindowService = new RegistryWindowService();

            var modRegistry = InitializeModRegistry(FilePaths.RegistryConfigPath);
            var modManager = InitializeModManager(FilePaths.ModManagerConfigPath, modRegistry);

            var registryWindowViewModel = new RegistryWindowVM(modRegistry, registryWindowService);
            var settingsViewModel = SettingsVM.LoadSettings(registryWindowViewModel, registryWindowService,
                installationFinder, installer, launcherReleaseLocator, modManager.PakDir, userDialogueSpawner, Shutdown);

#if DEBUG_FAKECHIVALRYLAUNCH
            var officialProcessLauncher = new PowershellProcessLauncher(
                "Official Chivalry 2"
            );
            var unchainedProcessLauncher = new PowershellProcessLauncher(
                "Unchained Chivalry 2"
            );
#else
            var originalLauncherPath = File.Exists(FilePaths.OriginalLauncherPath)
                ? FilePaths.OriginalLauncherPath
                : FilePaths.LauncherPath;
            var officialProcessLauncher =
                new ProcessLauncher(Path.Combine(Directory.GetCurrentDirectory(), originalLauncherPath));
            var unchainedProcessLauncher =
                new ProcessLauncher(Path.Combine(Directory.GetCurrentDirectory(), FilePaths.GameBinPath));
#endif

            var noSigLaunchPreparer = NoSigPreparer.Create(modManager.PakDir, userDialogueSpawner);
            var sigLaunchPreparer = SigPreparer.Create(modManager.PakDir, userDialogueSpawner);

            IChivalry2LaunchPreparer<LaunchOptions> pluginInstaller = new UnchainedPluginUpdateChecker(
                pluginReleaseLocator,
                new FileInfoVersionExtractor(),
                userDialogueSpawner);

            IChivalry2LaunchPreparer<LaunchOptions> modInstaller = new Chivalry2ModsInstaller(
                modManager, userDialogueSpawner
            );

            var vanillaLauncher = new Chivalry2Launcher(
                noSigLaunchPreparer.IgnoreOptions<LaunchOptions>(),
                officialProcessLauncher,
                Directory.GetCurrentDirectory()
            );

            var clientsideModdedLauncher = new Chivalry2Launcher(
                modInstaller
                    .Sub(sigLaunchPreparer),
                officialProcessLauncher,
                Directory.GetCurrentDirectory()
            );

            var unchainedLauncher = new UnchainedChivalry2Launcher(
                pluginInstaller
                    .AndThen(modInstaller)
                    .Sub(sigLaunchPreparer),
                unchainedProcessLauncher,
                Directory.GetCurrentDirectory(),
#if DEBUG_FAKECHIVALRYLAUNCH
                // don't shove dlls into processes that don't need them and cause crashes
                new NullInjector(true)
#else
                new DllInjector(Path.Combine(Directory.GetCurrentDirectory(), FilePaths.PluginDir))
#endif
            );

            var chivProcessMonitor = new ChivalryProcessWatcher();

            var homeViewModel = new HomeVM(
                settingsViewModel,
                modManager,
                vanillaLauncher,
                unchainedLauncher,
                userDialogueSpawner,
                chivProcessMonitor);
            var modListViewModel = new ModListVM(modManager, userDialogueSpawner);

            if (!modManager.Mods.Any())
                modListViewModel.RefreshModListCommand.ExecuteAsync(null);

            // Rebuild the arguments used to launch the application
            var envArgs = Environment.GetCommandLineArgs().Skip(1).ToList();

            // TODO: Replace this if/else chain with a real CLI
            if (envArgs.Contains("--startvanilla")) {
                homeViewModel.LaunchVanilla().Wait();
                return null;
            }

            if (envArgs.Contains("--startunchained")) {
                homeViewModel.LaunchUnchained().Wait();
                return null;
            }

            var serverConfigurationVMs =
                InitializeServerConfigurations(FilePaths.ServerConfigurationsFilePath, modManager);

            var serversTabViewModel = InitializeServersTab(
                FilePaths.ServersTabConfigurationPath,
                settingsViewModel,
                modManager,
                userDialogueSpawner,
                unchainedLauncher,
                serverConfigurationVMs,
                chivProcessMonitor);

            var mainWindowViewModel = new MainWindowVM(
                homeViewModel,
                modListViewModel,
                settingsViewModel,
                serversTabViewModel
            );

            return new MainWindow(mainWindowViewModel);
        }

        private AggregateModRegistry InitializeModRegistry(string jsonPath) {
            AggregateModRegistry CreateDefaultModRegistry() => new AggregateModRegistry(
                new GithubModRegistry("Chiv2-Community", "C2ModRegistry"));

            var loadedResult =
                InitializeFromFileWithCodec(ModRegistryCodec.Instance, jsonPath, CreateDefaultModRegistry);
            // Ensure that we've got an AggregateModRegistry. The constructor will handle it if we're wrapping 
            // another AggregateModRegistry, so no worries there.
            var registry = new AggregateModRegistry(loadedResult);

            RegisterSaveToFileOnExit(registry, ModRegistryCodec.Instance, jsonPath);
            return registry;
        }

        private ModManager InitializeModManager(string jsonPath, IModRegistry registry) {
            Func<ModManager> initializeDefaultModManager = () =>
                new ModManager(
                    registry,
                    new PakDir(FilePaths.PakDir, Enumerable.Empty<ManagedPak>()),
                    Enumerable.Empty<ReleaseCoordinates>()
                );

            var codec = new ModManagerCodec(registry);
            var modManager = InitializeFromFileWithCodec(codec, jsonPath, initializeDefaultModManager);

            RegisterSaveToFileOnExit(modManager, codec, jsonPath);
            return modManager;
        }

        private ObservableCollection<ServerConfigurationVM> InitializeServerConfigurations(string jsonPath,
            IModManager modManager) {
            Func<ObservableCollection<ServerConfigurationVM>> initializeDefault =
                () => new ObservableCollection<ServerConfigurationVM>();

            var codec = new ServerConfigurationCodec(modManager);
            var serverConfigurations = InitializeFromFileWithCodec(codec, jsonPath, initializeDefault);

            RegisterSaveToFileOnExit(serverConfigurations, codec, jsonPath);
            Exit += (_, _) => serverConfigurations.ForEach(x => x.SaveINI());

            return serverConfigurations;
        }

        private ServersTabVM InitializeServersTab(string jsonPath, SettingsVM settings, IModManager modManager, IUserDialogueSpawner dialogueSpawner, IChivalry2Launcher launcher, ObservableCollection<ServerConfigurationVM> serverConfigurations, IChivalryProcessWatcher processWatcher) {
            Func<ServersTabVM> initializeDefault = () => new ServersTabVM(
                settings,
                modManager,
                dialogueSpawner,
                launcher,
                serverConfigurations,
                processWatcher
            );

            var codec = new ServerTabCodec(settings, modManager, dialogueSpawner, launcher, serverConfigurations, processWatcher);
            var serversTab = InitializeFromFileWithCodec(codec, jsonPath, initializeDefault);
            RegisterSaveToFileOnExit(serversTab, codec, jsonPath);
            return serversTab;
        }

        private T InitializeFromFileWithCodec<T>(ICodec<T> codec, string filePath, Func<T> initializeDefault) {
            _log.Info($"Loading {typeof(T).Name} from {filePath} using {codec.GetType().Name}({codec})...");
            return codec.DeserializeFile(filePath).Match(
                None: initializeDefault,
                Some: deserializationResult => Optional(deserializationResult.Result).IfNone(() => {
                    _log.Error(
                        $"Failed to deserialize saved {typeof(T).Name} data from {filePath} using {codec.GetType().Name}({codec}). Falling back to default.",
                        deserializationResult.Exception);
                    return initializeDefault();
                }
                ));
        }

        private void RegisterSaveToFileOnExit<T>(T t, ICodec<T> codec, string filePath) {
            RegisterExitHandler(() => {
                try {
                    _log.Info($"Saving {typeof(T).Name} to {filePath} using {codec.GetType().Name}({codec})...");
                    codec.SerializeFile(filePath, t);
                }
                catch (Exception ex) {
                    _log.Error(
                        $"Failed to save configuration for {typeof(T).Name} to {filePath} using {codec.GetType().Name}({codec}).",
                        ex);
                }
            });
        }
        
        /// <summary>
        /// Registers exit handlers for multiple shutdown scenarios to ensure reliable saving.
        /// This should be called once during startup after all components are initialized.
        /// </summary>
        private void RegisterExitHandler(Action exitAction) {
            Exit += (_, _) => ExecuteExitAction("Application.Exit", exitAction);
            SessionEnding += (_, args) => ExecuteExitAction("SessionEnding", exitAction);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ExecuteExitAction("ProcessExit", exitAction);
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => ExecuteExitAction("UnhandledException", exitAction);
            Console.CancelKeyPress += (_, args) => ExecuteExitAction("Console.CancelKeyPress", exitAction);
        }

        /// <summary>
        /// Executes all registered exit actions exactly once, regardless of how many
        /// exit events fire. This ensures data is saved but prevents duplicate saves.
        /// </summary>
        /// <param name="source">The event source triggering the exit actions (for logging).</param>
        /// <param name="exitAction"></param>
        private void ExecuteExitAction(string source, Action exitAction) {
            try {
                exitAction();
            }
            catch (Exception ex) {
                _log.Error($"Exit action failed during {source}", ex);
            }
        }
    }
}