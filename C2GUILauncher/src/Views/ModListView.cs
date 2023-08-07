using C2GUILauncher.Mods;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace C2GUILauncher.Views
{
    class ModListView : ObservableCollection<ModView>
    {

        private ModManager ModManager;
        public ObservableCollection<ModView> UnfilteredModView { get; }
        public ObservableCollection<ModFilter> ModFilters { get; }

        public ModListView(ModManager modManager) : base(new List<ModView>()) {
            this.ModManager = modManager;
            this.UnfilteredModView = new ObservableCollection<ModView>();

            this.ModFilters = new ObservableCollection<ModFilter>();

            this.ModManager.Mods.CollectionChanged += ModManager_ModList_CollectionChanged;
            this.ModManager.EnabledModReleases.CollectionChanged += ModManager_EnabledMods_CollectionChanged;

            this.UnfilteredModView.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;
            this.ModFilters.CollectionChanged += UnfilteredModViewOrModFilters_CollectionChanged;
        }

        private void UnfilteredModViewOrModFilters_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            this.Clear();
            this.UnfilteredModView
                .Where(modView => this.ModFilters.All(modFilter => modFilter.ShouldInclude(modView)))
                .ToList()
                .ForEach(modView => this.Add(modView));

        }

        private void ModManager_ModList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (Mod mod in e.NewItems!)
                    {
                        this.UnfilteredModView.Add(new ModView(mod, ModManager.GetCurrentlyEnabledReleaseForMod(mod)));
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (Mod mod in e.OldItems!)
                    {
                        this.UnfilteredModView.Remove(this.First(x => x.Mod.LatestManifest == mod.LatestManifest));
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    this.UnfilteredModView.Clear();
                    break;
                default: 
                    throw new Exception("Unhandled NotifyCollectionChangedAction: " + e.Action);
            }
        }

        private void ModManager_EnabledMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (Release release in e.NewItems!)
                    {
                        var enabledMod = this.UnfilteredModView.First(modView => modView.Mod.Releases.Contains(release));
                        enabledMod.EnabledRelease = release;
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (Release release in e.OldItems!)
                    {
                        var removedMod = this.UnfilteredModView.First(modView => modView.Mod.Releases.Contains(release));
                        removedMod.EnabledRelease = null;
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (ModView modView in this.UnfilteredModView)
                    {
                        modView.EnabledRelease = null;
                    }
                    break;
                default:
                    throw new Exception("Unhandled NotifyCollectionChangedAction: " + e.Action);
            }
        }
    }

    record ModFilter(string Tag, string filterType)
    {
          public bool ShouldInclude(ModView mod)
        {
            return filterType switch
            {
                FilterType.Include => mod.Mod.LatestManifest.Tags.Contains(Tag),
                FilterType.Exclude => !mod.Mod.LatestManifest.Tags.Contains(Tag),
                _ => throw new Exception("Unhandled FilterType: " + filterType)
            };
        }
    }

    public static class FilterType
    {
        public const string Include = "Include";
        public const string Exclude = "Exclude";
    };

    class ModView : NotifyBoilerplate
    {
        public Mod Mod { get; }
        private Release? _EnabledRelease { get; set; }

        public Release? EnabledRelease
        {
            get { return _EnabledRelease; }
            set
            {
                bool shouldSet = MaybeNotify(_EnabledRelease, value, "EnabledRelease");
                if (shouldSet)
                    _EnabledRelease = value;
            }
        }

        public string TagsString
        {
            get { return string.Join(", ", Mod.LatestManifest.Tags); }
        }

        public bool IsEnabled
        {
            get { return EnabledRelease != null; }
        }

        public string? EnabledVersion { 
            get { return IsEnabled ? EnabledRelease!.Tag : ""; } 
        }

        public ModView(Mod mod, Release? enabledRelease)
        {
            Mod = mod;
            EnabledRelease = enabledRelease;
        }

    }
}
