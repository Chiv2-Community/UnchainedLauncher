using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using LanguageExt;
using log4net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
            set =>
                Application.Current.Dispatcher.Invoke(() => {
                    field = value;
                });
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
                    UserDialogueSpawner.DisplayMessage("Failed to launch Chivalry 2. Check the logs for details.");
                    Logger.Error(error);
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
                    Logger.Error(e);
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

        private void CreateChivalryProcessWatcher(Process process) {
            process.Exited += (_, _) => {
                try {
                    if (process.ExitCode == 0) return;

                    Logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                    UserDialogueSpawner.DisplayMessage(
                        $"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
                }
                catch (Exception e) {
                    Logger.Error("Failure occured while waiting for Chivalry process to exit", e);
                }
                finally {
                    if (!IsReusable())
                        Application.Current.Shutdown(0);
                    else {
                        MainWindowVisibility = Visibility.Visible;
                        Settings.CanClick = true;
                    }
                }
            };
        }
    }
}