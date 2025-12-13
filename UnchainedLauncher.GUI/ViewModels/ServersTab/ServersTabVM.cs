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
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    using static LanguageExt.Prelude;
    using static Successors;

    [AddINotifyPropertyChangedInterface]
    public partial class ServersTabVM : IDisposable, INotifyPropertyChanged {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ServersTabVM));
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
                CreateNewConfig().Wait();
            }

            SelectedConfiguration = ServerConfigs?.FirstOrDefault();
            UpdateVisibility();
        }

        [RelayCommand]
        public async Task LaunchHeadless() => await LaunchSelected(true);

        [RelayCommand]
        public async Task LaunchServer() => await LaunchSelected(false);

        [RelayCommand]
        public Task ShutdownServer() => Task.Run(() => SelectedServer?.Dispose());

        [RelayCommand]
        public async Task CreateNewConfig() {
            var newConfig = new ServerConfigurationVM(ModManager);
            var occupiedPorts = ServerConfigs.Select(
                (e) => new Set<int>(new List<int> {
                    e.A2SPort,
                    e.RconPort,
                    e.PingPort,
                    e.GamePort
                })
            ).Aggregate(Set<int>(), (s1, s2) => s1.AddRange(s2));

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
            var enabledCoordinates = SelectedConfiguration.EnabledServerModList.ToList();
            var enabledModActors =
                enabledCoordinates
                    .Select(rc => ModManager.GetRelease(rc))
                    .Collect(x => x.AsEnumerable());

            var maybeProcess = await LaunchServerWithOptions(formData, headless, enabledCoordinates);
            maybeProcess.IfSome(process => {
                var server = new Chivalry2Server(
                    process,
                    RegisterWithBackend(formData, enabledModActors),
                    new RCON(new IPEndPoint(IPAddress.Loopback, formData.RconPort))
                );
                var serverVm = new ServerVM(server);
                var runningTuple = (SelectedConfiguration, serverVm);
                Application.Current.Dispatcher.Invoke(() => RunningServers.Add(runningTuple));

                _ = AttachServerExitWatcher(process, formData, enabledCoordinates, headless, runningTuple);
            });
        }

        public void UpdateVisibility() {
            SelectedServer = RunningServers.Choose(
                (e) => e.configuration == SelectedConfiguration ? e.live : Option<ServerVM>.None
            ).FirstOrDefault();
            var isSelectedRunning = SelectedServer != null;

            ConfigurationEditorVisibility = isSelectedRunning || ServerConfigs.Length() == 0 ? Visibility.Hidden : Visibility.Visible;
            LiveServerVisibility = !isSelectedRunning ? Visibility.Hidden : Visibility.Visible;
        }

        // Helper to create a filesystem-friendly save dir suffix
        private static string SanitizeSaveddirSuffix(string s) {
            var substitutedUnderscores = s.Trim()
                .Replace(' ', '_')
                .Replace('(', '_')
                .Replace(')', '_')
                .ReplaceLineEndings("_");

            var illegalCharsRemoved = string.Join("_", substitutedUnderscores.Split(Path.GetInvalidFileNameChars()));
            return illegalCharsRemoved;
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
                Option<string>.Some(formData.Password).Map(pw => pw.Trim()).Filter(pw => pw != ""),
                formData.SelectedMap,
                formData.GamePort,
                formData.PingPort,
                formData.A2SPort,
                formData.RconPort,
                nextMapModActors
            );
        }

        private LaunchOptions BuildLaunchOptions(ServerConfiguration formData, bool headless, IEnumerable<ReleaseCoordinates> enabledCoordinates) {
            var serverLaunchOptions = BuildServerLaunchOptions(formData, headless, enabledCoordinates);
            return new LaunchOptions(
                enabledCoordinates,
                Settings.ServerBrowserBackend,
                Settings.CLIArgs,
                Settings.EnablePluginAutomaticUpdates,
                Some(SanitizeSaveddirSuffix(formData.Name)),
                Some(serverLaunchOptions)
            );
        }

        private async Task AttachServerExitWatcher(
            Process process,
            ServerConfiguration formData,
            IEnumerable<ReleaseCoordinates> enabledCoordinates,
            bool headless,
            (ServerConfigurationVM configuration, ServerVM live) runningTuple) {
            var attached = await ProcessWatcher.OnExit(process, async (exitCode, acceptable) => {
                Application.Current.Dispatcher.Invoke(() => {
                    RunningServers.Remove(runningTuple);
                    runningTuple.live.Dispose();
                });

                if (!acceptable) {
                    Logger.Error($"Server exited unexpectedly with code {exitCode}. Attempting automatic restart...");
                    DialogueSpawner.DisplayMessage($"Server exited with code {exitCode}. Attempting automatic restart...");

                    // Small delay to avoid tight restart loops
                    await Task.Delay(2000);

                    var relaunched = await LaunchServerWithOptions(formData, headless, enabledCoordinates);
                    relaunched.IfSome(newProc => {
                        var enabledModActors = enabledCoordinates
                            .Select(rc => ModManager.GetRelease(rc))
                            .Collect(x => x.AsEnumerable());
                        var newServer = new Chivalry2Server(
                            newProc,
                            RegisterWithBackend(formData, enabledModActors),
                            new RCON(new IPEndPoint(IPAddress.Loopback, formData.RconPort))
                        );
                        var newVm = new ServerVM(newServer);
                        var newTuple = (runningTuple.configuration, newVm);
                        Application.Current.Dispatcher.Invoke(() => RunningServers.Add(newTuple));
                        _ = AttachServerExitWatcher(newProc, formData, enabledCoordinates, headless, newTuple);
                    });
                }
            });

            if (!attached) {
                Logger.Warn($"Failed to attach exit watcher to server process. No automatic restart will occur.");
            }
        }

        private async Task<Option<Process>> LaunchServerWithOptions(ServerConfiguration formData, bool headless, IEnumerable<ReleaseCoordinates> enabledCoordinates) {
            var options = BuildLaunchOptions(formData, headless, enabledCoordinates);
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
        }

        // TODO: this should really be a part of Chivalry2Server
        public A2SBoundRegistration RegisterWithBackend(ServerConfiguration formData, IEnumerable<Release> enabledMods) {
            var ports = formData.ToPublicPorts();
            var serverInfo = new C2ServerInfo {
                Ports = ports,
                Name = formData.Name,
                Description = formData.Description,
                PasswordProtected = formData.Password.Length != 0,
                Mods = enabledMods.Select(release =>
                    new ServerBrowserMod(
                        release.Manifest.Name,
                        release.Manifest.Organization,
                        release.Tag.ToString()
                    )
                ).ToArray()
            };

            return new A2SBoundRegistration(
                new ServerBrowser(new Uri(Settings.ServerBrowserBackend + "/api/v1")),
                new A2S(new IPEndPoint(IPAddress.Loopback, ports.A2S)),
                serverInfo,
                formData.LocalIp);
        }

        public void Dispose() {
            SelectedServer?.Dispose();
            foreach (var runningServer in RunningServers) {
                runningServer.live.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}