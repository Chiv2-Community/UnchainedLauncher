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

namespace UnchainedLauncher.GUI.ViewModels
{
    [AddINotifyPropertyChangedInterface]
    public class ModListViewModel {
        private readonly ILog logger = LogManager.GetLogger(nameof(ModListViewModel));
        private readonly ModManager ModManager;
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
                logger.Info("Checking for updates...");
                var pendingUpdates = ModManager.GetUpdateCandidates();
                var updateCount = pendingUpdates.Count();

                if (updateCount == 0) {
                    MessageBox.Show("No updates available");
                    logger.Info("No updates available");
                    return;
                }

                var message = $"Found {updateCount} updates available.\n\n";
                message += string.Join("\n", pendingUpdates.Select(x => $"- {x.Item1.Manifest.Name} {x.Item2.Tag} -> {x.Item1.Tag}"));
                message += "\n\nWould you like to update these mods now?";

                message.Split("\n").ToList().ForEach(x => logger.Info(x));

                MessageBoxResult res = MessageBox.Show(message, "Update Mods?", MessageBoxButton.YesNo);

                logger.Info("User Selects: " + res.ToString());
                if (res == MessageBoxResult.Yes) {
                    logger.Info("Updating mods...");

                    var updatesTask = pendingUpdates.Select(async x => await ModManager.EnableModRelease(x.Item1).DownloadTask.Task);
                    await Task.WhenAll(updatesTask);
                    await RefreshModListAsync();

                    logger.Info("Mods updated successfully");
                    MessageBox.Show("Mods updated successfully");
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
