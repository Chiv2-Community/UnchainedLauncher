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
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unchained.ServerBrowser.Api;
using UnchainedLauncher.Core.Services.Server;
using UnchainedLauncher.Core.Services.Server.A2S;

// using Unchained.ServerBrowser.Client; // avoid Option<> name collision with LanguageExt

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
                (e) => e.configuration == SelectedConfiguration ? e.live : LanguageExt.Option<ServerVM>.None
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
                LanguageExt.Option<string>.Some(formData.Password).Map(pw => pw.Trim()).Filter(pw => pw != ""),
                formData.SelectedMap,
                formData.GamePort,
                formData.PingPort,
                formData.A2SPort,
                formData.RconPort,
                nextMapModActors
            );
        }

        private LaunchOptions BuildLaunchOptions(ServerConfiguration formData, bool headless, IEnumerable<ReleaseCoordinates> enabledCoordinates) {
            var releaseCoordinatesEnumerable = enabledCoordinates as ReleaseCoordinates[] ?? enabledCoordinates.ToArray();
            var serverLaunchOptions = BuildServerLaunchOptions(formData, headless, releaseCoordinatesEnumerable);
            return new LaunchOptions(
                releaseCoordinatesEnumerable,
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
        public ServerRegistrationService RegisterWithBackend(ServerConfiguration formData, IEnumerable<Release> enabledMods) {
            var ports = formData.ToPublicPorts();

            var options = new ServerRegistrationOptions(
                Name: formData.Name,
                Description: formData.Description,
                PasswordProtected: formData.Password.Length != 0,
                Ports: new ServerPorts(ports.Game, ports.A2S, ports.Ping),
                Mods: enabledMods.Select(release => release.Manifest.Name).ToList()
            );

            // Build generated API client (lightweight, per-registration instance)
            var loggerFactory = LoggerFactory.Create(builder => { });
            var logger = loggerFactory.CreateLogger<DefaultApi>();
            var httpClient = new HttpClient { BaseAddress = new Uri(Settings.ServerBrowserBackend) };
            var jsonProvider = new Unchained.ServerBrowser.Client.JsonSerializerOptionsProvider(new JsonSerializerOptions());
            var events = new DefaultApiEvents();
            IDefaultApi api = new DefaultApi(logger, loggerFactory, httpClient, jsonProvider, events);

            var service = new ServerRegistrationService(api);
            // Use 5 second timeout per request; A2SWatcher will retry indefinitely until server starts
            var a2s = new A2S(new IPEndPoint(IPAddress.Loopback, ports.A2S), timeOutMillis: 5000);
            service.Start(options, a2s, formData.LocalIp);
            return service;
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