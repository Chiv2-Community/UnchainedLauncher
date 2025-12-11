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
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;

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
        public ObservableCollection<(ServerConfigurationVM template, ServerVM live)> RunningServers { get; } = new();

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
                            ObservableCollection<ServerConfigurationVM> serverConfigs) {
            ServerConfigs = serverConfigs;
            
            ServerConfigs.CollectionChanged += (_, _) => {
                UpdateVisibility();
            };
            
            RunningServers.CollectionChanged += (_, _) => UpdateVisibility();
            Settings = settings;
            Launcher = launcher;
            DialogueSpawner = dialogueSpawner;
            ModManager = modManager;

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

            var formData = SelectedConfiguration.ToServerConfiguration();

            // Resolve selected releases from the template's EnabledServerModList
            var enabledModActors =
                SelectedConfiguration.EnabledServerModList
                    .Select(rc => ModManager.GetRelease(rc))
                    .Collect(x => x.AsEnumerable());

            var maybeProcess = await LaunchProcessForSelected(formData, headless);
            maybeProcess.IfSome(process => {
                var server = new Chivalry2Server(
                    process,
                    RegisterWithBackend(formData, enabledModActors),
                    new RCON(new IPEndPoint(IPAddress.Loopback, formData.RconPort))
                );
                var serverVm = new ServerVM(server);
                var runningTuple = (SelectedTemplate: SelectedConfiguration, serverVm);
                process.Exited += (_, _) => {
                    RunningServers.Remove(runningTuple);
                    runningTuple.serverVm.Dispose();
                };
                RunningServers.Add(runningTuple);
            });
        }

        public void UpdateVisibility() {
            SelectedServer = RunningServers.Choose(
                (e) => e.template == SelectedConfiguration ? e.live : Option<ServerVM>.None
            ).FirstOrDefault();
            var isSelectedRunning = SelectedServer != null;

            ConfigurationEditorVisibility = isSelectedRunning || ServerConfigs.Length() == 0 ? Visibility.Hidden : Visibility.Visible;
            LiveServerVisibility = !isSelectedRunning ? Visibility.Hidden : Visibility.Visible;
        }

        // TODO: this should really be a part of Chivalry2Server
        private async Task<Option<Process>> LaunchProcessForSelected(ServerConfiguration formData, bool headless) {
            if (!Settings.IsLauncherReusable()) {
                Settings.CanClick = false;
            }

            Func<string, string> sanitizeSaveddirSuffix = s => {
                var substitutedUnderscores = s.Trim()
                    .Replace(' ', '_')
                    .Replace('(', '_')
                    .Replace(')', '_')
                    .ReplaceLineEndings("_");

                var illegalCharsRemoved =
                    string.Join("_", substitutedUnderscores.Split(Path.GetInvalidFileNameChars()));

                return illegalCharsRemoved;
            };

            if (SelectedConfiguration == null) return None;

            var nextMapModActors = 
                SelectedConfiguration.EnabledServerModList
                    .SelectMany(rc => ModManager.GetRelease(rc))
                    .Where(release => release.Manifest.OptionFlags.ActorMod)
                    .Select(release => release.Manifest.Name.Replace(" ", ""));

            var serverLaunchOptions = new ServerLaunchOptions(
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

            var options = new LaunchOptions(
                SelectedConfiguration.EnabledServerModList,
                Settings.ServerBrowserBackend,
                Settings.CLIArgs,
                Settings.EnablePluginAutomaticUpdates,
                Some(sanitizeSaveddirSuffix(formData.Name)),
                Some(serverLaunchOptions)
            );

            var launchResult = await Launcher.Launch(options);
            return launchResult.Match(
                Left: _ => {
                    DialogueSpawner.DisplayMessage($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                    Settings.CanClick = true;
                    return None;
                },
                Right: process => {
                    process.EnableRaisingEvents = true;
                    process.Exited += (sender, e) => {
                        if (process.ExitCode == 0) return;
                        Logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                        DialogueSpawner.DisplayMessage($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
                    };
                    return Some(process);
                }
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