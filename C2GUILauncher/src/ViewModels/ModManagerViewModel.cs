using C2GUILauncher.Mods;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using C2GUILauncher.JsonModels;

namespace C2GUILauncher.ViewModels
{
    public class ModManagerViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly ModManager ModManager;
        private ObservableCollection<ModViewModel> UnfilteredModView { get; }
        private ObservableCollection<ModFilter> ModFilters { get; }
        public ObservableCollection<ModViewModel> DisplayMods { get; }
        public ICommand RefreshModListCommand { get; }
        public ICommand DisableModCommand { get; }

        public ModViewModel? SelectedMod { get; set; }
        public Release? SelectedModRelease { get; set; }


        public ModManagerViewModel(ModManager modManager) {
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

            this.RefreshModListCommand = new AsyncRelayCommand(() => this.RefreshModListAsync());
            this.DisableModCommand = new RelayCommand(() => this.DisableMod());
        }

        private void DisableMod()
        {
            if (this.SelectedMod == null) return;
            this.SelectedMod.EnabledRelease = null;
        }

        private async Task RefreshModListAsync()
        {
            try
            {
                await ModManager.UpdateModsList();
                var result = ModManager.EnableModRelease(ModManager.Mods.First().Releases.First());

                if (result.Warnings.Count > 0) MessageBox.Show(result.Warnings.Aggregate((x, y) => x + ", " + y));
                if (result.Failures.Count > 0) MessageBox.Show(result.Failures.Aggregate((x, y) => x + ", " + y));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void UnfilteredModViewOrModFilters_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            this.DisplayMods.Clear();
            this.UnfilteredModView
                .Where(modView => this.ModFilters.All(modFilter => modFilter.ShouldInclude(modView)))
                .ToList()
                .ForEach(modView => this.DisplayMods.Add(modView));

        }

        private void ModManager_ModList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (Mod mod in e.NewItems!)
                    {
                        this.UnfilteredModView.Add(new ModViewModel(mod, ModManager.GetCurrentlyEnabledReleaseForMod(mod), ModManager));
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (Mod mod in e.OldItems!)
                    {
                        this.UnfilteredModView.Remove(this.UnfilteredModView.First(x => x.Mod.LatestManifest.RepoUrl == mod.LatestManifest.RepoUrl));
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

    public record ModFilter(string Tag, FilterType Type)
    {
          public bool ShouldInclude(ModViewModel mod)
        {
            return Type switch
            {
                FilterType.Include => mod.Mod.LatestManifest.Tags.Contains(Tag),
                FilterType.Exclude => !mod.Mod.LatestManifest.Tags.Contains(Tag),
                _ => throw new Exception("Unhandled FilterType: " + Type)
            };
        }
    }

    public enum FilterType
    {
        Include,
        Exclude
    };
}
