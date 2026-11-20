using LanguageExt;
using log4net;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Dlls;
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
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using Application = System.Windows.Application;

namespace UnchainedLauncher.GUI {
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private static readonly ILog _log = LogManager.GetLogger(typeof(App));
        private bool _unsafeLoadAllDlls;
        private string[] _launchArguments = [];
        // Retain until process termination, including all late shutdown saves. Never dispose on exit.
        private static InstallationInstanceGuard? _installationGuard;
        private readonly List<Action> _preferenceSaves = new();

        private void SavePreferences() {
            foreach (var save in _preferenceSaves) save();
        }

        private void RegisterPreferenceSave(Action save) {
            _preferenceSaves.Add(save);
            RegisterExitHandler(save);
        }

        protected override async void OnStartup(StartupEventArgs e) {
            if (!InstallationStartup.TryAcquire(out _installationGuard, out var error)) {
                MessageBox.Show(error, "Unchained Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(1);
                return;
            }

            try {
                base.OnStartup(e);
                (_unsafeLoadAllDlls, _launchArguments) = UnsafeDllLoading.ParseGuiArguments(e.Args);

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


                var installationFinder = new Chivalry2InstallationFinder();
                var installer = new UnchainedLauncherInstaller(Shutdown, currentInstallation: _installationGuard);

                // figure out if we need to install by checking our current working directory
                var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
                var needsInstallation = !installationFinder.IsValidInstallation(currentDirectory);

                var forceSkipInstallation = Environment.GetCommandLineArgs().ToList().Contains("--no-install");

                if (forceSkipInstallation && needsInstallation)
                    _log.Info("Skipping installation");

                var window =
                    needsInstallation && !forceSkipInstallation
                        ? InitializeInstallerWindow(installationFinder, installer, unchainedLauncherReleaseLocator)
                        : await InitializeMainWindow(installationFinder, installer, unchainedLauncherReleaseLocator);

                window?.Show();
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error starting application: {ex.Message}");
                if (ex.StackTrace != null)
                    Debug.WriteLine(ex.StackTrace!);

                _log.Fatal("Unhandled exception", ex);
                File.WriteAllText("crash.log", ex.ToString());
                var currentDirectory = Directory.GetCurrentDirectory();
                MessageBox.Show($"An unhandled exception occurred. Please report this to a developer with {currentDirectory}\\crash.log ", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Removed class-level command bindings; handled by Views.UnchainedWindow type.

        private Window? InitializeInstallerWindow(Chivalry2InstallationFinder installationFinder,
            IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator) {
            var installationSelectionVM = new InstallationSelectionPageViewModel(installationFinder);
            var versionSelectionVM = new VersionSelectionPageViewModel(launcherReleaseLocator, new FileInfoVersionExtractor());
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

        private async Task<Window?> InitializeMainWindow(IChivalry2InstallationFinder installationFinder,
            IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator) {
            var userDialogueSpawner = new MessageBoxSpawner();
            var registryWindowService = new RegistryWindowService();

            var modRegistry = InitializeModRegistry(FilePaths.RegistryConfigPath);
            ModManager modManager;
            try {
                modManager = InitializeModManager(FilePaths.ModManagerConfigPath, modRegistry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                _log.Error("Could not load or migrate mod ownership.", ex);
                userDialogueSpawner.DisplayMessage($"Could not load or migrate mod ownership. Files were preserved; resolve the evidence or file permissions before retrying.\n{ex.Message}");
                Shutdown();
                return null;
            }
            RegisterSaveToFileOnExit(modRegistry, ModRegistryCodec.Instance, FilePaths.RegistryConfigPath);
            var registryWindowViewModel = new RegistryWindowVM(modRegistry, registryWindowService);
            var settingsViewModel = InitializeSettingsViewModel(installationFinder, installer, launcherReleaseLocator, registryWindowViewModel, registryWindowService, modManager, userDialogueSpawner);
            var assets = new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix);
            var dllManager = new DllManager(
                FilePaths.PluginDir,
                FilePaths.DllManagerConfigPath, new HttpClient(), () => modManager.GetEnabledAndDependencyReleases(settingsViewModel.AutomaticallyDownloadPakDependencies).ToArray(),
                () => modRegistry.ModRegistries.OfType<GithubModRegistry>()
                    .Select(registry => registry.DllDatabaseUrl).ToArray(), assets.DllCache);

            var dllManagerViewModel = new DllManagerVM(dllManager, settingsViewModel, userDialogueSpawner);
            await dllManagerViewModel.RefreshCommand.ExecuteAsync(null);

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

            var noSigLaunchPreparer = NoSigPreparer.CreateForInstallation(Directory.GetCurrentDirectory(), userDialogueSpawner);
            IChivalry2LaunchPreparer<LaunchOptions> checkpoint = new SavePreferencesPreparer(SavePreferences, userDialogueSpawner);
            var prepareSavedPaks = checkpoint.AndThen(new SavedPaksPreparer(modManager.PakDir, userDialogueSpawner));

            IChivalry2LaunchPreparer<LaunchOptions> pluginInstaller = new DllLaunchPreparer(dllManager, userDialogueSpawner);

            IChivalry2LaunchPreparer<LaunchOptions> pakInstaller = new Chivalry2ModsInstaller(modManager, userDialogueSpawner, settingsViewModel);
            var modInstaller = pluginInstaller.AndThen(
                pakInstaller.Sub(SigPreparer.CreateForProfile(modManager.PakDir, userDialogueSpawner)));

            var vanillaLauncher = new Chivalry2Launcher(
                prepareSavedPaks.AndThen(noSigLaunchPreparer.IgnoreOptions<LaunchOptions>()),
                officialProcessLauncher,
                Directory.GetCurrentDirectory()
            );

            var unchainedLauncher = new UnchainedChivalry2Launcher(
                prepareSavedPaks.AndThen(modInstaller),
                unchainedProcessLauncher,
                Directory.GetCurrentDirectory(),
#if DEBUG_FAKECHIVALRYLAUNCH
                // don't shove dlls into processes that don't need them and cause crashes
                new NullInjector(true)
#else
                new DllInjector(dllManager, FilePaths.PluginDir, _unsafeLoadAllDlls)
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
            var modListViewModel = new ModListVM(modManager, userDialogueSpawner, settingsViewModel);

            if (modManager.NeedsMetadataRefresh || !modManager.Mods.Any())
                await modListViewModel.RefreshModListCommand.ExecuteAsync(null);

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

            var modScanTab = new ModScanTabVM { DefaultPakDirectory = Path.GetFullPath(FilePaths.PakDir) };
            var helpVM = new HelpVM(settingsViewModel, installer, launcherReleaseLocator, modManager.PakDir, userDialogueSpawner, Shutdown);
            var availableModsAndMaps = new AvailableModsAndMapsService(modManager, modScanTab, settingsViewModel);
            availableModsAndMaps.StartWatchingLocalPaks();
            Exit += (_, _) => availableModsAndMaps.Dispose();

            var serverConfigurationVMs =
                InitializeServerConfigurations(FilePaths.ServerConfigurationsFilePath, modManager, modScanTab, availableModsAndMaps);

            var serversTabViewModel = InitializeServersTab(
                FilePaths.ServersTabConfigurationPath,
                settingsViewModel,
                modManager,
                modScanTab,
                userDialogueSpawner,
                unchainedLauncher,
                serverConfigurationVMs,
                chivProcessMonitor,
                availableModsAndMaps
            );

            var mainWindowViewModel = new MainWindowVM(
                homeViewModel,
                modListViewModel,
                settingsViewModel,
                serversTabViewModel,
                modScanTab,
                helpVM,
                dllManagerViewModel
            );

            return new MainWindow(mainWindowViewModel);
        }

        private SettingsVM InitializeSettingsViewModel(IChivalry2InstallationFinder installationFinder,
            IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator,
            RegistryWindowVM registryWindowViewModel, RegistryWindowService registryWindowService, ModManager modManager,
            MessageBoxSpawner userDialogueSpawner) {
            var cliArgs = string.Join(" ",
                _launchArguments
                    .ToList()
                    .Select(ArgumentEscaper.Escape)
            );

            var settingsCodec = new SettingsCodec(
                registryWindowViewModel,
                registryWindowService,
                installationFinder,
                cliArgs
            );

            var settingsViewModel = InitializeFromFileWithCodec(
                settingsCodec,
                FilePaths.LauncherSettingsFilePath,
                () => new SettingsVM(
                    registryWindowViewModel,
                    registryWindowService,
                    SettingsVM.DetectInstallationType(installationFinder),
                    true,
                    false,
                    "",
                    "https://servers.polehammer.net",
                    false,
                    SettingsVM.Version.IsPrerelease,
                    cliArgs
                )
            );

            RegisterSaveToFileOnExit(settingsViewModel, settingsCodec, FilePaths.LauncherSettingsFilePath);
            return settingsViewModel;
        }

        private AggregateModRegistry InitializeModRegistry(string jsonPath) {
            AggregateModRegistry CreateDefaultModRegistry() => new AggregateModRegistry(
                new GithubModRegistry("Chiv2-Community", "C2ModRegistry"));

            var loadedResult =
                InitializeFromFileWithCodec(ModRegistryCodec.Instance, jsonPath, CreateDefaultModRegistry);
            // Ensure that we've got an AggregateModRegistry. The constructor will handle it if we're wrapping
            // another AggregateModRegistry, so no worries there.
            var registry = new AggregateModRegistry(loadedResult);

            return registry;
        }

        private ModManager InitializeModManager(string jsonPath, IModRegistry registry) {
            var (manager, persistence) = LegacyOwnershipMigration.Load(jsonPath, registry,
                ex => MessageBox.Show($"Could not save mod selection. Retry before exiting.\n{ex.Message}"));
            RegisterPreferenceSave(persistence.Save);
            return manager;
        }

        private ObservableCollection<ServerConfigurationVM> InitializeServerConfigurations(string jsonPath,
            IModManager modManager, ModScanTabVM modScanTab, AvailableModsAndMapsService availableModsAndMapsVM) {
            Func<ObservableCollection<ServerConfigurationVM>> initializeDefault =
                () => new ObservableCollection<ServerConfigurationVM>();

            var codec = new ServerConfigurationCodec(modManager, modScanTab, availableModsAndMapsVM);
            var serverConfigurations = InitializeFromFileWithCodec(codec, jsonPath, initializeDefault);

            RegisterSaveToFileOnExit(serverConfigurations, codec, jsonPath);
            RegisterPreferenceSave(() => serverConfigurations.ForEach(x => x.SaveINI()));

            return serverConfigurations;
        }

        private ServersTabVM InitializeServersTab(
            string jsonPath,
            SettingsVM settings,
            IModManager modManager,
            ModScanTabVM modScanTab,
            IUserDialogueSpawner dialogueSpawner,
            IChivalry2Launcher launcher,
            ObservableCollection<ServerConfigurationVM> serverConfigurations,
            IChivalryProcessWatcher processWatcher,
            AvailableModsAndMapsService availableModsAndMaps) {
            Func<ServersTabVM> initializeDefault = () => new ServersTabVM(
                settings,
                modManager,
                modScanTab,
                dialogueSpawner,
                launcher,
                serverConfigurations,
                processWatcher,
                availableModsAndMaps
            );

            var codec = new ServerTabCodec(settings, modManager, modScanTab, dialogueSpawner, launcher, serverConfigurations, processWatcher, availableModsAndMaps);
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
            RegisterPreferenceSave(() => codec.SerializeFile(filePath, t));
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