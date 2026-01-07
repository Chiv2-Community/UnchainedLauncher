using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using LanguageExt.Pipes;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Server;
using UnchainedLauncher.Core.Services.Server.A2S;
using UnchainedLauncher.Core.Utilities;

// using Unchained.ServerBrowser.Client; // avoid Option<> name collision with LanguageExt

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    using static LanguageExt.Prelude;
    using static Successors;

    [AddINotifyPropertyChangedInterface]
    public partial class ServersTabVM : IDisposable, INotifyPropertyChanged {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ServersTabVM));

        private static Dispatcher UiDispatcher => Application.Current.Dispatcher;

        private static Task UiInvokeAsync(Action action) => UiDispatcher.InvokeAsync(action).Task;

        private static Task<T> UiInvokeAsync<T>(Func<Task<T>> func) => UiDispatcher.InvokeAsync(func).Task.Unwrap();

        public SettingsVM Settings { get; }
        public readonly IChivalry2Launcher Launcher;
        public IModManager ModManager { get; }
        public IUserDialogueSpawner DialogueSpawner;
        public ObservableCollection<ServerConfigurationVM> ServerConfigs { get; }
        public ObservableCollection<(ServerConfigurationVM configuration, ServerVM live)> RunningServers { get; } = new();
        private IChivalryProcessWatcher ProcessWatcher { get; }

        public ServerConfigurationVM? SelectedConfiguration {
            get;
            set {
                field = value;
                UpdateVisibility();
            }
        }

        public ServerVM? SelectedServer { get; private set; }

        public Visibility ConfigurationEditorVisibility { get; private set; }
        public Visibility LiveServerVisibility { get; private set; }

        public ServersTabVM(SettingsVM settings,
                            IModManager modManager,
                            IUserDialogueSpawner dialogueSpawner,
                            IChivalry2Launcher launcher,
                            ObservableCollection<ServerConfigurationVM> serverConfigs,
                            IChivalryProcessWatcher processWatcher) {
            ServerConfigs = serverConfigs;

            ServerConfigs.CollectionChanged += (_, _) => {
                UpdateVisibility();
            };

            RunningServers.CollectionChanged += (_, _) => UpdateVisibility();
            Settings = settings;
            Launcher = launcher;
            DialogueSpawner = dialogueSpawner;
            ModManager = modManager;
            ProcessWatcher = processWatcher;

            if (ServerConfigs?.Count == 0) {
                CreateNewConfig();
            }

            SelectedConfiguration = ServerConfigs?.FirstOrDefault();
            UpdateVisibility();
        }

        [RelayCommand]
        public async Task LaunchHeadless() => await LaunchSelected(true);

        [RelayCommand]
        public async Task LaunchServer() => await LaunchSelected(false);

        [RelayCommand]
        public Task ShutdownServer() {
            if (SelectedServer == null) return Task.CompletedTask;
            return Task.Run(() => SelectedServer.DisposeServer());
        }

        [RelayCommand]
        public void CreateNewConfig() {
            var newConfig = new ServerConfigurationVM(ModManager);

            var occupiedPorts = new Set<int>().AddRange(
                ServerConfigs.SelectMany(conf => new[] { conf.A2SPort, conf.GamePort, conf.PingPort, conf.RconPort })
            );

            // try to make the new template nice
            if (SelectedConfiguration != null) {
                // increment ports so that added server is not incompatible with other templates
                var oldConfig = SelectedConfiguration;
                (newConfig.GamePort, occupiedPorts) = ReserveRestrictedSuccessor(oldConfig.GamePort, occupiedPorts);
                (newConfig.PingPort, occupiedPorts) = ReserveRestrictedSuccessor(oldConfig.PingPort, occupiedPorts);
                (newConfig.A2SPort, occupiedPorts) = ReserveRestrictedSuccessor(oldConfig.A2SPort, occupiedPorts);
                (newConfig.RconPort, _) = ReserveRestrictedSuccessor(oldConfig.RconPort, occupiedPorts);

                newConfig.Name = TextualSuccessor(oldConfig.Name);
            }

            ServerConfigs.Add(newConfig);
            SelectedConfiguration = newConfig;
        }

        [RelayCommand]
        public void RemoveConfiguration() {
            if (SelectedConfiguration != null) {
                ServerConfigs.Remove(SelectedConfiguration);
            }
            SelectedConfiguration = ServerConfigs.FirstOrDefault();
        }

        public async Task LaunchSelected(bool headless = false) {
            if (SelectedConfiguration == null) return;

            if (!Settings.IsLauncherReusable()) {
                Settings.CanClick = false;
            }

            var formData = SelectedConfiguration.ToServerConfiguration();

            // Resolve selected releases from the template's EnabledServerModList
            var enabledCoordinates = SelectedConfiguration.EnabledServerModList.ToArray();
            
            SelectedConfiguration.SaveINI();

            var maybeProcess = await LaunchServerWithOptions(formData, headless, enabledCoordinates);
            maybeProcess.IfSome(process => {
                var server = CreateServer(process, formData, headless, enabledCoordinates);
                var serverVm = new ServerVM(server, formData.Name, SelectedConfiguration.AvailableMaps);
                serverVm.StartUpdateLoop();
                var runningTuple = (SelectedConfiguration, serverVm);
                UiDispatcher.Invoke(() => RunningServers.Add(runningTuple));

                _ = AttachServerExitWatcher(process, formData, enabledCoordinates, headless, runningTuple);
            });
        }

        public void UpdateVisibility() {
            SelectedServer = RunningServers.FirstOrDefault(e => e.configuration == SelectedConfiguration).live;
            var isSelectedRunning = SelectedServer != null;

            ConfigurationEditorVisibility = isSelectedRunning || ServerConfigs.Length() == 0 ? Visibility.Hidden : Visibility.Visible;
            LiveServerVisibility = !isSelectedRunning ? Visibility.Hidden : Visibility.Visible;
        }

        private ServerLaunchOptions BuildServerLaunchOptions(ServerConfiguration formData, bool headless, IEnumerable<ReleaseCoordinates> enabledCoordinates) {
            var nextMapModActors =
                enabledCoordinates
                    .SelectMany(rc => ModManager.GetRelease(rc))
                    .Where(release => release.Manifest.OptionFlags.ActorMod)
                    .Select(release => release.Manifest.Name.Replace(" ", ""));

            return new ServerLaunchOptions(
                headless,
                formData.Name,
                formData.Description,
                Optional(formData.Password.Trim()).Filter(pw => pw.Length != 0),
                formData.SelectedMap,
                formData.GamePort,
                formData.PingPort,
                formData.A2SPort,
                formData.RconPort,
                formData.LocalIp,
                nextMapModActors
            );
        }

        private LaunchOptions BuildLaunchOptions(ServerConfiguration formData, bool headless, ReleaseCoordinates[] enabledCoordinates) {
            var serverLaunchOptions = BuildServerLaunchOptions(formData, headless, enabledCoordinates);
            return new LaunchOptions(
                enabledCoordinates,
                Settings.ServerBrowserBackend,
                Settings.CLIArgs,
                Settings.EnablePluginAutomaticUpdates,
                Some(formData.SavedDirSuffix),
                Some(serverLaunchOptions)
            );
        }

        private Chivalry2Server CreateServer(Process process,
                                             ServerConfiguration formData,
                                             bool headless,
                                             ReleaseCoordinates[] enabledCoordinates) {
            var serverLaunchOptions = BuildServerLaunchOptions(formData, headless, enabledCoordinates);
            var a2s = new A2S(new IPEndPoint(IPAddress.Loopback, formData.A2SPort));
            var rcon = new RCON(new IPEndPoint(IPAddress.Loopback, formData.RconPort));
            return new Chivalry2Server(process, serverLaunchOptions, a2s, rcon);
        }

        private async Task AttachServerExitWatcher(
            Process process,
            ServerConfiguration formData,
            ReleaseCoordinates[] enabledCoordinates,
            bool headless,
            (ServerConfigurationVM configuration, ServerVM live) runningTuple) {
            var attached = await ProcessWatcher.OnExit(process, async (exitCode, acceptable) => {
                if (acceptable) {
                    await UiInvokeAsync(() => {
                        RunningServers.Remove(runningTuple);
                        runningTuple.live.DisposeServer();
                    });
                    return;
                }

                // Keep the existing ServerVM around so UI can show downtime + restart timeline.
                await UiInvokeAsync(() => { runningTuple.live.IsUp = false; });

                Logger.Error($"Server exited unexpectedly with code {exitCode}. Attempting automatic restart...");

                // Small delay to avoid tight restart loops
                await Task.Delay(2000);

                var relaunched = await LaunchServerWithOptions(formData, headless, enabledCoordinates);
                relaunched.IfSome(newProc => {
                    var newServer = CreateServer(newProc, formData, headless, enabledCoordinates);
                    UiDispatcher.Invoke(() => runningTuple.live.ReplaceServer(newServer, countAsRestart: true));
                    _ = AttachServerExitWatcher(newProc, formData, enabledCoordinates, headless, runningTuple);
                });
            });

            if (!attached) {
                Logger.Warn($"Failed to attach exit watcher to server process. No automatic restart will occur.");
            }
        }

        private async Task<Option<Process>> LaunchServerWithOptions(ServerConfiguration formData, bool headless, ReleaseCoordinates[] enabledCoordinates) {
            var options = BuildLaunchOptions(formData, headless, enabledCoordinates);
            return await UiInvokeAsync(async () => {
                var launchResult = await Launcher.Launch(options);
                return launchResult.Match(
                    Left: _ => {
                        Logger.Error("Failed to launch server. Check logs for details.");
                        DialogueSpawner.DisplayMessage("Failed to launch server. Check logs for details.");
                        Settings.CanClick = true;
                        return None;
                    },
                    Right: process => Some(process)
                );
            });
        }

        public void Dispose() {
            SelectedServer?.DisposeServer();
            foreach (var runningServer in RunningServers) {
                runningServer.live.DisposeServer();
            }

            GC.SuppressFinalize(this);
        }
    }
}