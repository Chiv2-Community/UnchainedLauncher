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
        private readonly ModManager ModManager;
        private ObservableCollection<ModViewModel> UnfilteredModView { get; }
        private ObservableCollection<ModFilter> ModFilters { get; }

        public ICommand RefreshModListCommand { get; }

        public ModViewModel? SelectedMod { get; set; }
        public ObservableCollection<ModViewModel> DisplayMods { get; }


        public ModListViewModel(ModManager modManager) {
            this.ModManager = modManager;
            this.UnfilteredModView = new ObservableCollection<ModViewModel>();
            this.DisplayMods = new ObservableCollection<ModViewModel>();

            this.ModFilters = new ObservableCollection<ModFilter>();
            ModFilters.Add(new ModFilter("Explicit", FilterType.Exclude));

            // Watch the mod manager for changes, and update our view accordingly
            this.ModManager.Mods.CollectionChanged += ModManager_ModList_CollectionChanged;

            // Watch the unfiltered mod view and mod filters for changes, and update our view accordingly
            this.UnfilteredModView.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;
            this.ModFilters.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;

            this.RefreshModListCommand = new AsyncRelayCommand(RefreshModListAsync);
        }

        private async Task RefreshModListAsync() {
            try {
                await ModManager.UpdateModsList();
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        private void UnfilteredModViewOrModFilters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            this.DisplayMods.Clear();
            this.UnfilteredModView
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
