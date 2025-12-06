using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public partial class ModListVM {
        private readonly ILog _logger = LogManager.GetLogger(nameof(ModListVM));
        public readonly IModManager _modManager;
        private ObservableCollection<ModVM> UnfilteredModView { get; }
        private ObservableCollection<ModFilter> ModFilters { get; }

        // UI state: filtering and sorting
        public string? SearchTerm { get; set; }
        public bool ShowEnabledOnly { get; set; }
        public ModSortMode SelectedSortMode { get; set; } = ModSortMode.EnabledFirst;
        public IReadOnlyList<ModSortMode> SortModes { get; } = [ModSortMode.EnabledFirst, ModSortMode.Alphabetical, ModSortMode.LatestReleaseDateFirst, ModSortMode.NewestModsFirst];

        public ModVM? SelectedMod { get; set; }
        public ObservableCollection<ModVM> DisplayMods { get; }

        private IUserDialogueSpawner UserDialogueSpawner { get; }

        public ModListVM(IModManager modManager, IUserDialogueSpawner userDialogueSpawner) {
            this._modManager = modManager;

            UserDialogueSpawner = userDialogueSpawner;


            this.UnfilteredModView = new ObservableCollection<ModVM>();
            this.DisplayMods = new ObservableCollection<ModVM>();

            this.ModFilters = new ObservableCollection<ModFilter>();

            // Watch the unfiltered mod view and mod filters for changes, and update our view accordingly
            this.UnfilteredModView.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;
            this.ModFilters.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;
        }

        // Property change hooks (PropertyChanged.Fody will call these)
        private void OnSearchTermChanged() => RebuildDisplay();
        private void OnShowEnabledOnlyChanged() => RebuildDisplay();
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

                updatedModsList.ToList().ForEach(mod =>
                    UnfilteredModView.Add(
                        new ModVM(
                            mod,
                            _modManager.GetCurrentlyEnabledReleaseForMod(mod),
                            _modManager
                        )
                    )
                );

                _logger.Info("Mod list refreshed.");

            }
            catch (Exception ex) {
                _logger.Error(ex);
                UserDialogueSpawner.DisplayMessage(ex.ToString());
            }
        }

        [RelayCommand]
        private async Task UpdateMods() {
            try {
                await RefreshModList();

                _logger.Info("Checking for Mod updates...");

                IEnumerable<(ModVM, UpdateCandidate)> pendingUpdates =
                    DisplayMods
                        .Map(displayMod => displayMod.CheckForUpdate().Map(update => (displayMod, update)))
                        .Collect(x => x.AsEnumerable());

                var res =
                    UserDialogueSpawner.DisplayUpdateMessage(
                        "Update Mods?",
                        $"Mod updates available.",
                        "Yes", "No", null,
                        pendingUpdates.Select(x => DependencyUpdate.FromUpdateCandidate(x.Item2))
                    );

                if (res != UserDialogueChoice.Yes) {
                    if (res == null) {
                        MessageBox.Show("No updates available.");
                    }
                    else {
                        MessageBox.Show("Mods not updated");
                    }
                }
                else {
                    var failedUpdates =
                        pendingUpdates.Select(x =>
                                (x.Item1, x.Item1.UpdateCurrentlyEnabledVersion(x.Item2.AvailableUpdate))
                            ).Filter(x => x.Item2 == false)
                            .Select(x => x.Item1);



                    if (failedUpdates.Any()) {
                        var failureNames =
                            string.Join(", ", failedUpdates.Select(vm => vm.Mod.LatestManifest.Name));
                        MessageBox.Show($"Failed to enable mods: {failureNames}\n\nCheck the logs for more details.");
                    }

                    _logger.Info("Mods updated successfully");
                    MessageBox.Show("Mods updated successfully");
                }
            }
            catch (Exception ex) {
                _logger.Error(ex.ToString());
                MessageBox.Show("Failed to check for updates. Check the logs for details.");
            }
        }

        private void UnfilteredModViewOrModFilters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildDisplay();

        private void RebuildDisplay() {
            IEnumerable<ModVM> query = this.UnfilteredModView;

            // Apply tag-based filters if any exist
            query = query.Where(modView => this.ModFilters.All(modFilter => modFilter.ShouldInclude(modView)));

            // Enabled only toggle
            if (ShowEnabledOnly)
                query = query.Where(m => m.IsEnabled);

            // Text search against name and tags
            var term = (SearchTerm ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(term)) {
                var t = term.ToLowerInvariant();
                query = query.Where(m =>
                    m.Mod.LatestManifest.Name.ToLowerInvariant().Contains(t)
                    || m.TagsString.ToLowerInvariant().Contains(t)
                );
            }

            // Sorting
            query = SelectedSortMode switch {
                ModSortMode.EnabledFirst => query
                    .OrderByDescending(m => m.IsEnabled)
                    .ThenBy(m => m.Mod.LatestManifest.Name, StringComparer.OrdinalIgnoreCase),
                ModSortMode.LatestReleaseDateFirst => 
                    query.OrderByDescending(m => m.Mod.LatestRelease.Map(r => r.ReleaseDate).IfNone(DateTime.MinValue)),
                ModSortMode.NewestModsFirst => 
                    query.OrderByDescending(m => m.Mod.Releases.LastOrDefault()?.ReleaseDate),
                _ => 
                    query.OrderBy(m => m.Mod.LatestManifest.Name, StringComparer.OrdinalIgnoreCase),
            };

            this.DisplayMods.Clear();
            foreach (var mod in query)
                this.DisplayMods.Add(mod);
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
                        var removeElem = this.UnfilteredModView.FirstOrDefault(x => x.Mod.LatestManifest.RepoUrl == mod.LatestManifest.RepoUrl);
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
    }

    public enum ModSortMode {
        EnabledFirst,
        Alphabetical,
        LatestReleaseDateFirst,
        NewestModsFirst
    }

    public record ModFilter(ModTag Tag, FilterType Type) {
        public bool ShouldInclude(ModVM mod) {
            return Type switch {
                FilterType.Include => mod.Mod.LatestManifest.Tags.Contains(Tag),
                FilterType.Exclude => !mod.Mod.LatestManifest.Tags.Contains(Tag),
                _ => throw new Exception("Unhandled FilterType: " + Type)
            };
        }
    }

    public enum FilterType {
        Include,
        Exclude
    };
}