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
            (!Settings.CanLaunch)
                ? "Unchained cannot launch an EGS installation more than once.  Restart the launcher if you wish to launch the game again."
                : "";
        public IChivalry2Launcher VanillaLauncher { get; }
        public IChivalry2Launcher UnchainedLauncher { get; }

        private IUserDialogueSpawner UserDialogueSpawner { get; }
        private IModManager ModManager { get; }
        private IChivalryProcessWatcher ProcessWatcher { get; }

        public Visibility MainWindowVisibility {
            get;
            set;
        }

        public bool IsReusable() => Settings.InstallationType == InstallationType.Steam;

        public HomeVM(SettingsVM settings, IModManager modManager, IChivalry2Launcher vanillaLauncher, IChivalry2Launcher moddedLauncher, IUserDialogueSpawner dialogueSpawner, IChivalryProcessWatcher processWatcher) {
            Settings = settings;
            ModManager = modManager;
            VanillaLauncher = vanillaLauncher;
            UnchainedLauncher = moddedLauncher;
            UserDialogueSpawner = dialogueSpawner;
            ProcessWatcher = processWatcher;
            _ = LoadWhatsNew();
            MainWindowVisibility = Visibility.Visible;
        }

        public partial class WhatsNewItem {
            public required string Title { get; init; }
            public required DateTime Date { get; init; }
            public required string Markdown { get; init; }
            public string? AppendHtml { get; init; }
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
                        Markdown = markdown,
                        AppendHtml = $"<br /><hr /><a style='float:right;' href='{r.ReleaseUrl}'>View on GitHub</a>",
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
        public async Task<Option<Process>> LaunchVanilla() {
            Logger.Info($"Launching Vanilla");
            Settings.HasLaunched = true;

            // For a vanilla launch we need to pass the args through to the vanilla launcher.
            // Skip the first arg which is the path to the exe.
            var launchResult =
                await VanillaLauncher.Launch(
                    new LaunchOptions(
                        new List<ReleaseCoordinates>(),
                        "",
                        Settings.CLIArgs,
                        false,
                        None,
                        None
                    )
                );

            
            return launchResult.Match(
                Left: error => {
                    Logger.Error("Failed to launch Chivalry 2: ", error);
                    UserDialogueSpawner.DisplayMessage("Failed to launch Chivalry 2. Check the logs for details.");
                    return None;
                },
                Right: process => {
                    if (!IsReusable()) {
                        Logger.Debug("Launcher is not reusable. Exiting.");
                        Application.Current.Shutdown(0);
                        return None;
                    }

                    MainWindowVisibility = Visibility.Hidden;
                    _ = CreateChivalryProcessWatcher(process);
                    return Some(process);
                }
            );
        }

        [RelayCommand]
        public async Task<Option<Process>> LaunchUnchained() {
            Logger.Info("Launching Unchained");

            var options = new LaunchOptions(
                ModManager.GetEnabledAndDependencies(),
                Settings.ServerBrowserBackend,
                Settings.CLIArgs,
                Settings.EnablePluginAutomaticUpdates,
                None,
                None
            );
            
            Settings.HasLaunched = true;

            var launchResult = await UnchainedLauncher.Launch(options);

            
            return launchResult.Match(
                Left: e => {
                    Logger.Error("Failed to launch Chivalry 2 Unchained", e);
                    if (e.Underlying is not UnchainedLaunchFailure.LaunchCancelledError)
                        UserDialogueSpawner.DisplayMessage($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");

                    return None;
                },
                Right: process => {
                    if (!IsReusable()) {
                        Logger.Debug("Launcher is not reusable. Exiting.");
                        Application.Current.Shutdown(0);
                        return None;
                    }

                    MainWindowVisibility = Visibility.Hidden;
                    _ = CreateChivalryProcessWatcher(process);
                    return Some(process);
                }
            );
        }

        private async Task CreateChivalryProcessWatcher(Process process) {
            var attached = await ProcessWatcher.OnExit(process, (exitCode, acceptable) => {
                if (!acceptable) {
                    UserDialogueSpawner.DisplayMessage(
                        $"Chivalry 2 exited unexpectedly with code {exitCode}. Check the logs for details.");
                }

                Application.Current.Dispatcher.Invoke(() => {
                    MainWindowVisibility = Visibility.Visible;
                });
            });

            if (!attached) {
                Logger.Warn($"Failed to locate actual vanilla game process '{ProcessWatcher.TargetProcessNameNoExt}'. Launcher exiting. User can open a new one later.");
                Application.Current.Shutdown(0);
            }
        }
    }
}