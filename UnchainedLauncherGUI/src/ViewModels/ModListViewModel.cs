using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using CommunityToolkit.Mvvm.Input;
using log4net;
using PropertyChanged;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core.Mods;
using LanguageExt;
using System.Threading;
using UnchainedLauncher.GUI.Views;
using System.Collections;
using System.Collections.Generic;
using LanguageExt.Common;

namespace UnchainedLauncher.GUI.ViewModels
{
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public class ModListViewModel {
        private readonly ILog logger = LogManager.GetLogger(nameof(ModListViewModel));
        private readonly IModManager ModManager;
        private ObservableCollection<ModViewModel> UnfilteredModView { get; }
        private ObservableCollection<ModFilter> ModFilters { get; }

        public ICommand RefreshModListCommand { get; }
        public ICommand UpdateModsCommand { get; }

        public ModViewModel? SelectedMod { get; set; }
        public ObservableCollection<ModViewModel> DisplayMods { get; }

        public ModListViewModel(ModManager modManager) {
            this.ModManager = modManager;
            this.UnfilteredModView = new ObservableCollection<ModViewModel>();
            this.DisplayMods = new ObservableCollection<ModViewModel>();

            this.ModFilters = new ObservableCollection<ModFilter>();

            // Watch the unfiltered mod view and mod filters for changes, and update our view accordingly
            this.UnfilteredModView.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;
            this.ModFilters.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;

            this.RefreshModListCommand = new AsyncRelayCommand(RefreshModListAsync);
            this.UpdateModsCommand = new AsyncRelayCommand(UpdateModsAsync);
        }

        private async Task RefreshModListAsync() {
            try {
                logger.Info("Refreshing mod list...");
                var (errors, updatedModsList) = await ModManager.UpdateModsList();

                if (errors.Any()) {
                    logger.Warn("Errors encountered while refreshing mod list:");
                    errors.ToList().ForEach(error => logger.Warn(error));
                    MessageBox.Show("Errors encountered while refreshing mod list. Check the logs for details.");
                }
                UnfilteredModView.Clear();

                updatedModsList.ToList().ForEach(mod => 
                    UnfilteredModView.Add(
                        new ModViewModel(
                            mod, 
                            ModManager.GetCurrentlyEnabledReleaseForMod(mod), 
                            ModManager
                        )
                    )
                );

                logger.Info("Mod list refreshed.");

            } catch (Exception ex) {
                logger.Error(ex);
                MessageBox.Show(ex.ToString());
            }
        }

        private async Task UpdateModsAsync() {
            try {
                logger.Info("Checking for Mod updates...");

                IEnumerable<(ModViewModel, UpdateCandidate)> pendingUpdates = 
                    DisplayMods
                        .Map(displayMod => displayMod.CheckForUpdate().Map(update => (displayMod, update)))
                        .Collect(x => x.AsEnumerable());

                Option<MessageBoxResult> res = 
                    UpdatesWindow.Show(
                        "Update Mods?", 
                        $"Mod updates available.", 
                        "Yes", "No", None, 
                        pendingUpdates.Select(x => DependencyUpdate.FromUpdateCandidate(x.Item2))
                    );

                if (res.Contains(MessageBoxResult.Yes)) {
                    var updatesTask =
                        pendingUpdates.Select(async x => await x.Item1.UpdateCurrentlyEnabledVersion(x.Item2.AvailableUpdate));

                    var result = await Task.WhenAll(updatesTask);

                    var errors =
                        result
                            .Collect(r => r.LeftAsEnumerable()) // Get only the errors
                            .Map(disableOrEnableFailure => disableOrEnableFailure.Match<Error>(l => l, r => r)); // Both sides are errors, just errors of a different type. 

                    await RefreshModListAsync();

                    if (errors.Any()) {
                        errors.Iter(e => logger.Error(e));

                        var errorMessages = errors.Map(e => "- " + e.Message);
                        var errorMessage = string.Join("\n", errorMessages);

                        MessageBox.Show($"Some errors occurred during update: \n{errorMessage}\n\n Check the logs for more details.");
                    } else {
                        logger.Info("Mods updated successfully");
                        MessageBox.Show("Mods updated successfully");
                    }
                } else if(res.IsNone) {
                    MessageBox.Show("No updates available.");
                } else {
                    MessageBox.Show("Mods not updated");
                }
            } catch (Exception ex) {
                logger.Error(ex.ToString());
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
                        this.UnfilteredModView.Add(new ModViewModel(mod, ModManager.GetCurrentlyEnabledReleaseForMod(mod), ModManager));
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
        public bool ShouldInclude(ModViewModel mod) {
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
