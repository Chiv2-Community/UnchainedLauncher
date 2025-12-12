using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public partial class HomeVM : INotifyPropertyChanged {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(HomeVM));

        public SettingsVM Settings { get; }

        public string ButtonToolTip =>
            (!Settings.CanClick && !IsReusable())
                ? "Unchained cannot launch an EGS installation more than once.  Restart the launcher if you wish to launch the game again."
                : "";
        public IChivalry2Launcher VanillaLauncher { get; }
        public IChivalry2Launcher ClientSideModdedLauncher { get; }
        public IChivalry2Launcher UnchainedLauncher { get; }

        private IUserDialogueSpawner UserDialogueSpawner { get; }
        private IModManager ModManager { get; }

        public Visibility MainWindowVisibility {
            get;
            set;
        }

        public bool IsReusable() => Settings.InstallationType == InstallationType.Steam;

        public HomeVM(SettingsVM settings, IModManager modManager, IChivalry2Launcher vanillaLauncher, IChivalry2Launcher clientSideModdedLauncher, IChivalry2Launcher moddedLauncher, IUserDialogueSpawner dialogueSpawner) {
            Settings = settings;
            ModManager = modManager;
            VanillaLauncher = vanillaLauncher;
            ClientSideModdedLauncher = clientSideModdedLauncher;
            UnchainedLauncher = moddedLauncher;
            UserDialogueSpawner = dialogueSpawner;
            _ = LoadWhatsNew();
            MainWindowVisibility = Visibility.Visible;
        }

        public partial class WhatsNewItem {
            public required string Title { get; init; }
            public required DateTime Date { get; init; }
            public required string Html { get; init; }
            public required string Url { get; init; }

            [RelayCommand]
            public void OpenUrl() => Process.Start(new ProcessStartInfo {
                FileName = Url,
                UseShellExecute = true
            });
        }

        public System.Collections.ObjectModel.ObservableCollection<WhatsNewItem> WhatsNew { get; } = new();

        private async Task LoadWhatsNew() {
            try {
                await ModManager.UpdateModsList();

                var latestFive = ModManager.Mods
                    .SelectMany(m => m.Releases)
                    .OrderByDescending(r => r.ReleaseDate)
                    .Take(20)
                    .ToList();


                // Build items off-UI-thread, then marshal collection updates to UI thread
                var items = latestFive.Select(r => {
                    var markdown = "## Mod Description\n\n" + r.Manifest.Description;

                    markdown += r.ReleaseNotesMarkdown != null
                        ? $"\n\n---\n\n## {r.Tag} Release Notes\n\n{r.ReleaseNotesMarkdown}"
                        : "\n\n---\n\nNo release notes provided.";

                    return new WhatsNewItem {
                        Title = $"{r.Manifest.Name} {r.Tag}",
                        Date = r.ReleaseDate,
                        Html = MarkdownRenderer.RenderHtml(markdown,
                            $"<br /><hr /><a style='float:right;' href='{r.ReleaseUrl}'>View on GitHub</a>"),
                        Url = r.ReleaseUrl
                    };
                }).ToList();

                Application.Current.Dispatcher.Invoke(() => {
                    WhatsNew.Clear();
                    foreach (var item in items) WhatsNew.Add(item);
                });
            }
            catch (Exception e) {
                Logger.Warn("Failed to load What's New section", e);
            }
        }

        [RelayCommand]
        public Task<Option<Process>> LaunchVanilla() => InternalLaunchVanilla(false);

        [RelayCommand]
        public Task<Option<Process>> LaunchModdedVanilla() => InternalLaunchVanilla(true);

        private async Task<Option<Process>> InternalLaunchVanilla(bool enableMods) {
            var withOrWithout = enableMods ? "with" : "without";

            Logger.Info($"Launching vanilla {withOrWithout} mods");
            
            // For a vanilla launch we need to pass the args through to the vanilla launcher.
            // Skip the first arg which is the path to the exe.
            var launchResult = enableMods
                ? await ClientSideModdedLauncher.Launch(
                        new LaunchOptions(
                            ModManager.GetEnabledAndDependencies(),
                            "",
                            Settings.CLIArgs,
                            false,
                            None,
                            None
                        )
                    )
                : await VanillaLauncher.Launch(
                    new LaunchOptions(
                        new List<ReleaseCoordinates>(),
                        "",
                        Settings.CLIArgs,
                        false,
                        None,
                        None)
                    );

            if (!IsReusable())
                Settings.CanClick = false;

            return launchResult.Match(
                Left: error => {
                    Logger.Error("Failed to launch Chivalry 2: ", error);
                    UserDialogueSpawner.DisplayMessage("Failed to launch Chivalry 2. Check the logs for details.");
                    Settings.CanClick = true;
                    return None;
                },
                Right: process => {
                    MainWindowVisibility = Visibility.Hidden;
                    CreateChivalryProcessWatcher(process);
                    return Some(process);
                }
            );
        }

        [RelayCommand]
        public async Task<Option<Process>> LaunchUnchained() {
            if (!IsReusable()) Settings.CanClick = false;
            
            Logger.Info("Launching Unchained");

            var options = new LaunchOptions(
                ModManager.GetEnabledAndDependencies(),
                Settings.ServerBrowserBackend,
                Settings.CLIArgs,
                Settings.EnablePluginAutomaticUpdates,
                None,
                None
            );

            var launchResult = await UnchainedLauncher.Launch(options);

            return launchResult.Match(
                Left: e => {
                    Logger.Error("Failed to launch Chivalry 2 Unchained", e);
                    if (e.Underlying is not UnchainedLaunchFailure.LaunchCancelledError)
                        UserDialogueSpawner.DisplayMessage($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");

                    Settings.CanClick = true;
                    return None;
                },
                Right: process => {
                    MainWindowVisibility = Visibility.Hidden;
                    CreateChivalryProcessWatcher(process);
                    return Some(process);
                }
            );
        }

        private static LanguageExt.HashSet<int> AcceptableExitCodes = new LanguageExt.HashSet<int>([
            0, // Normal Exit,
            -1073741510 // Exit via DLL Window
        ]);

        private const string TargetProcNameNoExt = "Chivalry2-Win64-Shipping";
        
        private async Task CreateChivalryProcessWatcher(Process process) {
            // When launching vanilla, an EAC process is launched before launching chiv 2.
            // If the process passed in here isn't the actual game process, try to locate the real one and bind this event to it instead.
            var name = process.ProcessName;
            var isTarget = string.Equals(name, TargetProcNameNoExt, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(name, TargetProcNameNoExt + ".exe", StringComparison.OrdinalIgnoreCase);
            
            if (!isTarget) {
                Logger.Info($"EAC MiddleMan ({name}) detected... Polling for Chiv2.");
                var maybeChiv2Proc = await FindChivalry2Process();
                if (maybeChiv2Proc is not null) {
                    Logger.Info($"Found actual Chivalry 2 process: {maybeChiv2Proc.ProcessName}");
                    process = maybeChiv2Proc;
                    process.EnableRaisingEvents = true;
                } else {
                    Logger.Warn($"Failed to locate actual vanilla game process '{TargetProcNameNoExt}'. Launcher exiting. User can open a new one later.");
                    Application.Current.Shutdown(0);
                    return;
                }
            }
            
            process.Exited += (_, _) => {
                Logger.Info($"Chivalry 2 exited. ({process.ExitCode})");
                if (!AcceptableExitCodes.Contains(process.ExitCode)) {
                    UserDialogueSpawner.DisplayMessage(
                        $"Chivalry 2 exited unexpectedly with code {process.ExitCode}. Check the logs for details.");
                }

                if (!IsReusable()) {
                    Logger.Debug("Launcher is not reusable. Exiting.");
                    Application.Current.Shutdown(0);
                }
                else {
                    Logger.Debug("Launcher is reusable. Showing main window.");
                    Application.Current.Dispatcher.Invoke(() => {
                        MainWindowVisibility = Visibility.Visible;
                        Settings.CanClick = true;
                    });
                }
            };
        }
        
        private async Task<Process?> FindChivalry2Process()
        {
            Process? gameProc = null;
            try {
                // Poll briefly for the actual game process after the middle-man exits
                for (var i = 0; i < 50; i++) {
                    // ~25 seconds total
                    var candidates = Process.GetProcessesByName(TargetProcNameNoExt);
                    gameProc = candidates
                        .OrderByDescending(c => {
                            try { return c.StartTime; }
                            catch { return DateTime.MinValue; }
                        })
                        .FirstOrDefault();
                    if (gameProc is not null) return gameProc;
                    await Task.Delay(500);
                }
            }
            catch (Exception ex) {
                Logger.Warn("Failed to poll for game process", ex);
            }

            return null;
        }
    }
}