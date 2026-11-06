using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public partial class ModListVM {
        private readonly ILog _logger = LogManager.GetLogger(nameof(ModListVM));
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        public readonly IModManager _modManager;
        private ObservableCollection<ModVM> UnfilteredModView { get; }

        // UI state: filtering and sorting
        public string? SearchTerm { get; set; }
        public bool ShowEnabledOnly { get; set; }
        public ModSortMode SelectedSortMode { get; set; } = ModSortMode.EnabledFirst;
        public IReadOnlyList<ModSortMode> SortModes { get; } = [ModSortMode.EnabledFirst, ModSortMode.Alphabetical, ModSortMode.LatestReleaseDateFirst, ModSortMode.NewestModsFirst];

        public ModVM? SelectedMod { get; set; }
        public ObservableCollection<ModVM> DisplayMods { get; }

        private IUserDialogueSpawner UserDialogueSpawner { get; }

        public IUnmanagedPakSettings? Settings { get; }

        public ModListVM(IModManager modManager, IUserDialogueSpawner userDialogueSpawner, IUnmanagedPakSettings? settings = null) {
            Settings = settings;
            this._modManager = modManager;

            UserDialogueSpawner = userDialogueSpawner;


            this.UnfilteredModView = new ObservableCollection<ModVM>();
            this.DisplayMods = new ObservableCollection<ModVM>();

            PopulateModsFromModManager();

            // Watch the unfiltered mod view and mod filters for changes, and update our view accordingly
            this.UnfilteredModView.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;
            _modManager.ModEnabled += (_, _) => SynchronizeEnabledReleases();
            _modManager.ModDisabled += _ => SynchronizeEnabledReleases();
            _modManager.MetadataRefreshed += (_, _) => SynchronizeEnabledReleases();
            if (settings is INotifyPropertyChanged notifyingSettings)
                PropertyChangedEventManager.AddHandler(notifyingSettings, (_, _) => SynchronizeEnabledReleases(), nameof(IUnmanagedPakSettings.AutomaticallyDownloadPakDependencies));
        }

        private void SynchronizeEnabledReleases() {
            void Update() {
                var effective = _modManager.GetEnabledAndDependencyReleases(includeDependencies: Settings?.AutomaticallyDownloadPakDependencies ?? true).ToList();
                foreach (var mod in UnfilteredModView) {
                    mod.EnabledRelease = _modManager.GetCurrentlyEnabledReleaseForMod(mod.Mod).FirstOrDefault();
                    mod.DependencyRelease = effective.FirstOrDefault(release => ModIdentifier.FromMod(mod.Mod) == ModIdentifier.FromRelease(release));
                }
                RebuildDisplay();
            }
            if (_context != null && _context != SynchronizationContext.Current) _context.Post(_ => Update(), null);
            else Update();
        }

        // Property change hooks (PropertyChanged.Fody will call these)
        private void OnSearchTermChanged() => RebuildDisplay();
        private void OnSelectedSortModeChanged() => RebuildDisplay();

        [RelayCommand]
        public async Task RefreshModList() {
            try {
                _logger.Info("Refreshing mod list...");
                var (errors, updatedModsList) = await _modManager.UpdateModsList();

                if (errors.Any()) {
                    _logger.Warn("Errors encountered while refreshing mod list:");
                    errors.ToList().ForEach(error => _logger.Warn(error));
                    UserDialogueSpawner.DisplayMessage("Errors encountered while refreshing mod list. Check the logs for details.");
                }
                UnfilteredModView.Clear();
                PopulateModsFromModManager();
                _logger.Info("Mod list refreshed.");
            }
            catch (Exception ex) {
                _logger.Error(ex);
            }
        }

        [RelayCommand]
        private async Task UpdateMods() {
            try {
                await RefreshModList();

                _logger.Info("Checking for Mod updates...");

                var pendingUpdates =
                    DisplayMods
                        .Map(displayMod => displayMod.CheckForUpdate().Map(update => (displayMod, update)))
                        .Collect(x => x.AsEnumerable()).ToList();

                var res =
                    UserDialogueSpawner.DisplayUpdateMessage(
                        "Update Mods?",
                        $"Mod updates available.",
                        "Yes", "No", null,
                        pendingUpdates.Select(x => DependencyUpdate.FromUpdateCandidate(x.Item2))
                    );

                if (res != UserDialogueChoice.Yes) {
                    if (res == null) {
                        UserDialogueSpawner.DisplayMessage("No updates available.");
                    }
                    else {
                        UserDialogueSpawner.DisplayMessage("Mods not updated");
                    }
                }
                else {
                    var failedUpdates =
                        pendingUpdates.Select(x =>
                                (x.Item1, x.Item1.UpdateCurrentlyEnabledVersion(x.Item2.AvailableUpdate))
                            ).Filter(x => !x.Item2)
                             .Select(x => x.Item1).ToList();



                    if (failedUpdates.Any()) {
                        var failureNames =
                            string.Join(", ", failedUpdates.Select(vm => vm.Mod.LatestReleaseInfo.Name));
                        UserDialogueSpawner.DisplayMessage($"Failed to enable mods: {failureNames}\n\nCheck the logs for more details.");
                    }

                    _logger.Info("Mods updated successfully");
                    UserDialogueSpawner.DisplayMessage("Mods updated successfully");
                }
            }
            catch (Exception ex) {
                _logger.Error(ex.ToString());
                UserDialogueSpawner.DisplayMessage("Failed to check for updates. Check the logs for details.");
            }
        }

        private void UnfilteredModViewOrModFilters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildDisplay();

        private void RebuildDisplay() {
            IEnumerable<ModVM> query = this.UnfilteredModView;

            // Apply tag-based filters if any exist
            // Enabled only toggle
            if (ShowEnabledOnly)
                query = query.Where(m => m.IsEnabled);

            // Text search against name and tags
            var term = (SearchTerm ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(term)) {
                var t = term.ToLowerInvariant();
                query = query.Where(m =>
                    m.Mod.LatestReleaseInfo.Name.ToLowerInvariant().Contains(t)
                );
            }

            // Sorting
            query = SelectedSortMode switch {
                ModSortMode.EnabledFirst => query
                    .OrderByDescending(m => m.EnabledRelease != null)
                    .ThenByDescending(m => m.IsDependency)
                    .ThenBy(m => m.Mod.LatestReleaseInfo.Name, StringComparer.OrdinalIgnoreCase),
                ModSortMode.LatestReleaseDateFirst =>
                    query.OrderByDescending(m => m.Mod.LatestRelease.Map(r => r.ReleaseDate).IfNone(DateTime.MinValue)),
                ModSortMode.NewestModsFirst =>
                    query.OrderByDescending(m => m.Mod.Releases.LastOrDefault()?.ReleaseDate),
                _ =>
                    query.OrderBy(m => m.Mod.LatestReleaseInfo.Name, StringComparer.OrdinalIgnoreCase),
            };

            var desired = query.ToList();
            // Preserve existing card controls when dependency state or sort order changes.
            foreach (var stale in DisplayMods.Where(mod => !desired.Contains(mod)).ToList())
                DisplayMods.Remove(stale);
            for (var index = 0; index < desired.Count; index++) {
                var current = DisplayMods.IndexOf(desired[index]);
                if (current < 0) DisplayMods.Insert(index, desired[index]);
                else if (current != index) DisplayMods.Move(current, index);
            }
        }

        private void ModManager_ModList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                    foreach (Mod mod in e.NewItems!) {
                        this.UnfilteredModView.Add(new ModVM(mod, _modManager.GetCurrentlyEnabledReleaseForMod(mod), _modManager));
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (Mod mod in e.OldItems!) {
                        var removeElem = this.UnfilteredModView.FirstOrDefault(x => x.Mod.LatestReleaseInfo.RepoUrl == mod.LatestReleaseInfo.RepoUrl);
                        if (removeElem != null)
                            this.UnfilteredModView.Remove(removeElem);
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    this.UnfilteredModView.Clear();
                    break;
                default:
                    throw new Exception("Unhandled NotifyCollectionChangedAction: " + e.Action);
            }
        }

        public void PopulateModsFromModManager() {
            _modManager.Mods.ToList().ForEach(mod =>
                UnfilteredModView.Add(
                    new ModVM(
                        mod,
                        _modManager.GetCurrentlyEnabledReleaseForMod(mod),
                        _modManager
                    )
                )
            );

            SynchronizeEnabledReleases();
        }
    }

    public enum ModSortMode {
        EnabledFirst,
        Alphabetical,
        LatestReleaseDateFirst,
        NewestModsFirst
    }
}