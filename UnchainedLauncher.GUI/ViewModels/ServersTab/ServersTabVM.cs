using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using LanguageExt.Pipes;
using UnchainedLauncher.Core.API.A2S;
using System.Net;
using System.Windows;
using System.Threading;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    using static LanguageExt.Prelude;
    [AddINotifyPropertyChangedInterface]
    public class ServersTabVM {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ServersTabVM));
        public SettingsVM Settings { get; }
        public readonly IUnchainedChivalry2Launcher Launcher;
        public Func<IModManager> ModManagerCreator;
        public IUserDialogueSpawner DialogueSpawner;
        public ObservableCollection<ServerTemplateVM> ServerTemplates { get; }
        public ObservableCollection<(ServerTemplateVM template, ServerVM live)> RunningTemplates { get; } = new();
        private ServerTemplateVM? _SelectedTemplate;
        public ServerTemplateVM? SelectedTemplate { 
            get => _SelectedTemplate; 
            set {
                _SelectedTemplate = value;
                UpdateVisibility();
            }
        }
        public ServerVM? SelectedLive { get; private set; }
        
        public Visibility TemplateEditorVisibility { get; private set; }
        public Visibility LiveServerVisibility { get; private set; }

        public ICommand Add_Template_Command { get; }
        public ICommand Remove_Template_Command { get; }
        public ICommand Launch_Server { get; }
        public ICommand Launch_Headless { get; }
        public ICommand Shutdown_Server { get; }

        public void UpdateVisibility() {
            SelectedLive = RunningTemplates.Choose(
                    (e) => e.template == SelectedTemplate ? e.live : Option<ServerVM>.None
                ).FirstOrDefault();
            bool isSelectedRunning = SelectedLive != null;

            TemplateEditorVisibility = isSelectedRunning || ServerTemplates.Length() == 0 ? Visibility.Hidden : Visibility.Visible;
            LiveServerVisibility = !isSelectedRunning ? Visibility.Hidden : Visibility.Visible;
        }

        public ServersTabVM(SettingsVM settings,
                            Func<IModManager> modManagerCreator,
                            IUserDialogueSpawner dialogueSpawner,
                            IUnchainedChivalry2Launcher launcher,
                            ObservableCollection<ServerTemplateVM>? templates = null) {
            ServerTemplates = templates ?? new();
            ServerTemplates.CollectionChanged += (_, _) => UpdateVisibility();
            RunningTemplates.CollectionChanged += (_, _) => UpdateVisibility();
            SelectedTemplate = ServerTemplates.FirstOrDefault();
            Settings = settings;
            Launcher = launcher;
            DialogueSpawner = dialogueSpawner;
            ModManagerCreator = modManagerCreator;
            Add_Template_Command = new RelayCommand(Add_Template);
            Remove_Template_Command = new RelayCommand(() => {
                if (SelectedTemplate != null) {
                    ServerTemplates.Remove(SelectedTemplate);
                }
                SelectedTemplate = ServerTemplates.FirstOrDefault();
            });
            Shutdown_Server = new RelayCommand(async () => {
                // this might take a while, so it is actually feasible that
                // the user could switch selections by the time this completes
                var heldLive = SelectedLive;
                if (heldLive == null) {
                    return;
                }
                await heldLive.SendCommand("exit");
                
                if (heldLive.ServerProcess != null) {
                    try {
                        var cts = new CancellationTokenSource(2000);
                        await heldLive.ServerProcess.WaitForExitAsync(cts.Token);
                        return;
                    }
                    catch (TaskCanceledException e) {
                        if (!heldLive.ServerProcess.HasExited) {
                            heldLive.ServerProcess?.Kill(true);
                        }
                    }
                }
            });
            Launch_Server = new RelayCommand(async () => await LaunchSelected(false));
            Launch_Headless = new RelayCommand(async () => await LaunchSelected(true));
            UpdateVisibility();
        }

        private void Add_Template() {
            var newTemplate = new ServerTemplateVM(ModManagerCreator());
            var occupiedPorts = ServerTemplates.Select(
                (e) => new Set<int>( new List<int> {
                    e.Form.A2sPort,
                    e.Form.RconPort,
                    e.Form.PingPort,
                    e.Form.GamePort
                })
            ).Aggregate(Set<int>(), (s1, s2) => s1.AddRange(s2));

            // try to make the new template nice
            if (SelectedTemplate != null) {
                // increment ports so that added server is not incompatible with other templates
                var oldForm = SelectedTemplate.Form;
                var newForm = newTemplate.Form;
                (newForm.GamePort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.GamePort, occupiedPorts);
                (newForm.PingPort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.PingPort, occupiedPorts);
                (newForm.A2sPort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.A2sPort, occupiedPorts);
                (newForm.RconPort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.RconPort, occupiedPorts);

                // increment name in a similar way, so the user doesn't get things confused
                newForm.Name = TextualSuccessor(oldForm.Name);
            }

            ServerTemplates.Add(newTemplate);
            SelectedTemplate = newTemplate;
        }

        public async Task LaunchSelected(bool headless = false) {
            if (SelectedTemplate == null) return;

            ServerInfoFormData formData = SelectedTemplate.Form.Data;
            var enabledMods = SelectedTemplate.ModManager.EnabledModReleases;
            var maybeProcess = await LaunchProcessForSelected(formData, headless);
            maybeProcess.IfSome(process => {
                var server = new Chivalry2Server(RegisterWithBackend(formData, enabledMods));
                var serverVm = new ServerVM(server, process, formData.RconPort);
                var RunningTuple = (SelectedTemplate, serverVm);
                process.Exited += (_, _) => {
                    RunningTemplates.Remove(RunningTuple);
                    RunningTuple.serverVm.Dispose();
                };
                RunningTemplates.Add(RunningTuple);
            });
        }

        // TODO: this should really be a part of Chivalry2Server
        private async Task<Option<Process>> LaunchProcessForSelected(ServerInfoFormData formData, bool headless) {
            if (!Settings.IsLauncherReusable()) {
                Settings.CanClick = false;
            }

            if (SelectedTemplate == null) return None;

            var serverLaunchOptions = formData.ToServerLaunchOptions(headless);
            var options = new ModdedLaunchOptions(
                Settings.ServerBrowserBackend,
                None,
                Some(serverLaunchOptions)
            );

            var launchResult = await Launcher.Launch(options, Settings.EnablePluginAutomaticUpdates, Settings.CLIArgs);
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
                        logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                        DialogueSpawner.DisplayMessage($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
                    };
                    return Some(process);
                }
            );
        }

        // TODO: this should really be a part of Chivalry2Server
        public A2SBoundRegistration RegisterWithBackend(ServerInfoFormData formData, IEnumerable<Release> enabledMods) {
            var ports = formData.ToPublicPorts();
            var serverInfo = new C2ServerInfo {
                Ports = ports,
                Name = formData.Name,
                Description = formData.Description,
                PasswordProtected = formData.Password.Length != 0,
                Mods = enabledMods.Select(release =>
                    new ServerBrowserMod(
                        release.Manifest.Name,
                        release.Version.ToString(),
                        release.Manifest.Organization
                    )
                ).ToArray()
            };

            return new A2SBoundRegistration(
                new ServerBrowser(new Uri(Settings.ServerBrowserBackend + "/api/v1")),
                new A2S(new IPEndPoint(IPAddress.Loopback, ports.A2s)),
                serverInfo,
                formData.LocalIp);
        }

        public static (int, Set<int>) ReserveRestrictedSuccessor(int number, Set<int> excluded) {
            int next = RestrictedSuccessor(number, excluded);
            return (next, excluded.Add(next));
        }

        /// <summary>
        /// gets a number's successor, excluding any number in excluded
        /// </summary>
        /// <param name="number">the number to get the successor of</param>
        /// <param name="excluded">numbers that should not be returned</param>
        /// <returns>number's next successor not in excluded</returns>
        public static int RestrictedSuccessor(int number, Set<int> excluded) {
            while (excluded.Contains(++number)) ;
            return number;
        }

        /// <summary>
        /// Return a string which is the textual successor. 
        /// This means incrementing any counting numbers in the string.
        /// Ignores numbers not in parentheses, and preserves intervening whitespace.
        /// Examples:
        ///     "Test 1 string (1)" => "Test 1 string (2)"
        ///     "Test 1 string" => "Test 1 string (2)"
        /// </summary>
        /// <param name="text">The text to get a successor for</param>
        /// <returns>The successor text</returns>
        public static string TextualSuccessor(string text) {
            if (Regex.IsMatch(text, "\\((\\s*)(\\d+)(\\s*)\\)")) {
                return Regex.Replace(
                    text,
                    "\\((\\s*)(\\d+)(\\s*)\\)",
                    (Match match) =>
                        $"({match.Groups[1].Value}{int.Parse(match.Groups[2].Value) + 1}{match.Groups[3].Value})"
                );
            }
            return $"{text} (1)";
        }
    }
}
