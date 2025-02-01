using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    public partial class ModListVM : INotifyPropertyChanged {
        private readonly ILog _logger = LogManager.GetLogger(nameof(ModListVM));
        public readonly IModManager _modManager;
        private ObservableCollection<ModVM> UnfilteredModView { get; }
        private ObservableCollection<ModFilter> ModFilters { get; }

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

        [RelayCommand]
        private async Task RefreshModList() {
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

        private void UnfilteredModViewOrModFilters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            this.DisplayMods.Clear();
            this.UnfilteredModView
                .OrderBy(modView => modView.VersionNameSortKey)
                .Where(modView => this.ModFilters.All(modFilter => modFilter.ShouldInclude(modView)))
                .ToList()
                .ForEach(modView => this.DisplayMods.Add(modView));

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