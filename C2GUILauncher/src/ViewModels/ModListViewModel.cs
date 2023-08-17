using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace C2GUILauncher.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public class ModListViewModel {
        /// <summary>
        /// The mod manager is used to maintain the list of mods and their releases.
        /// </summary>
        private readonly ModManager ModManager;

        /// <summary>
        /// The unfiltered mod view is the list of mods that we get from the mod manager, before any filters are applied.
        /// </summary>
        private ObservableCollection<ModViewModel> UnfilteredModView { get; }

        /// <summary>
        /// The mod filters are the list of filters that we apply to the unfiltered mod view.
        /// </summary>
        private ObservableCollection<ModFilter> ModFilters { get; }

        /// <summary>
        /// The refresh mod list command is used to invoke the mod manager to refresh the list of mods.
        /// </summary>
        public ICommand RefreshModListCommand { get; }

        /// <summary>
        /// The selected mod is the mod that is currently selected in the mod list.
        /// </summary>
        public ModViewModel? SelectedMod { get; set; }

        /// <summary>
        /// Display mods is the list of mods that we display in the mod list, after applying the filters.
        /// </summary>
        public ObservableCollection<ModViewModel> DisplayMods { get; }


        public ModListViewModel(ModManager modManager) {
            this.ModManager = modManager;
            this.UnfilteredModView = new ObservableCollection<ModViewModel>();
            this.DisplayMods = new ObservableCollection<ModViewModel>();

            this.ModFilters = new ObservableCollection<ModFilter>();

            // Add the default mod filters
            ModFilters.Add(new ModFilter("Explicit", FilterType.Exclude));

            // Watch the mod manager for changes, and update our view accordingly
            this.ModManager.Mods.CollectionChanged += ModManager_ModList_CollectionChanged;

            // Watch the unfiltered mod view and mod filters for changes, and update our view accordingly
            this.UnfilteredModView.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;
            this.ModFilters.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;

            this.RefreshModListCommand = new AsyncRelayCommand(RefreshModListAsync);
        }

        /// <summary>
        /// The refresh mod list command is used to invoke the mod manager to refresh the list of mods.
        /// </summary>
        /// <returns>A Task which completes when the update is complete</returns>
        private async Task RefreshModListAsync() {
            try {
                await ModManager.UpdateModsList();
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        /// <summary>
        /// Triggered when the unfiltered mod view or mod filters change. 
        /// This will keep the display mods list up to date.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UnfilteredModViewOrModFilters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            this.DisplayMods.Clear();
            this.UnfilteredModView
                .Where(modView => this.ModFilters.All(modFilter => modFilter.ShouldInclude(modView)))
                .ToList()
                .ForEach(modView => this.DisplayMods.Add(modView));

        }

        /// <summary>
        /// Triggered when the mod manager's mod list changes.
        /// This will keep the unfiltered mod view up to date by adding or removing mods as needed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="Exception"></exception>
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

    public record ModFilter(string Tag, FilterType Type) {
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
