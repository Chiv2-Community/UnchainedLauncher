using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using Semver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.Views.Registry;

namespace UnchainedLauncher.GUI.ViewModels.Registry {
    using static LanguageExt.Prelude;
    

    [AddINotifyPropertyChangedInterface]
    public partial class LocalModRegistryWindowVM {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(PersistentServerRegistration));

        private ILocalModRegistryWindowService _windowService;

        public LocalModRegistry Registry { get; }
        public ObservableCollection<RegistryModVM> Mods { get; } = new();
        public LocalModRegistryWindowVM(ILocalModRegistryWindowService windowService) {
            _windowService = windowService;
            Registry = windowService.Registry;
            Registry.OnRegistryChanged += RefreshModsList;
            RefreshModsList(null);
        }

        [RelayCommand]
        public void AddRelease() {
            _windowService.PromptAddRelease();
        }

        public async void RefreshModsList(ReleaseCoordinates? updatedMod) {
            try
            {
                if (updatedMod == null) {
                    var newMods = await Registry.GetAllMods();

                    var newModVMs = newMods.Mods
                        .ToList()
                        .Map(m => {
                            var vm = new RegistryModVM(
                                Registry,
                                _windowService,
                                m
                            );

                            return vm;
                        });
                    
                    Application.Current.Dispatcher.BeginInvoke((Action)delegate () {
                        Mods.Clear();
                        newModVMs.ToList().ForEach(m => Mods.Add(m));
                    });
                }
                else {
                    
                    var modVM = Mods.First(mod => ModIdentifier.FromMod(mod.Mod) == updatedMod);
                    var existing = modVM.Releases.FirstOrDefault(r => r.Release.Tag == updatedMod.Version);
                    if(existing != null)
                        modVM.Releases.Remove(existing);
                    
                    var newRelease = await Registry.GetModRelease(updatedMod);
                    
                    newRelease.ToOption().IfSome(mod => {
                        modVM.Releases.Add(new RegistryReleaseVM(mod, _windowService));
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Error("Failed to update mod list", e);
            }
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class RegistryModVM {
        public Mod Mod {
            get;
        }

        public ObservableCollection<RegistryReleaseVM> Releases { get; } = new();
        public LocalModRegistry SourceRegistry { get; set; }
        private ILocalModRegistryWindowService _windowService;

        public RegistryModVM(LocalModRegistry registry, ILocalModRegistryWindowService windowService, Mod mod) {
            _windowService = windowService;
            Mod = mod;
            SourceRegistry = registry;
            PopulateReleases();
        }

        // This must be called from UI thread!
        private void PopulateReleases() {
            Releases.Clear();
            Mod.Releases.ToList()
                .ForEach(r =>
                    Releases.Add(new RegistryReleaseVM(
                            r,
                            _windowService
                        )
                    )
                );
        }

        [RelayCommand]
        public void AddRelease() {
            var latestRelease = Mod.LatestRelease.FirstOrDefault();
            if (latestRelease != null) {
                SemVersion.TryParse(latestRelease.Tag, SemVersionStyles.AllowV, out var newTag);
                newTag ??= new SemVersion(0, 0, 0);

                var newRelease = latestRelease with {
                    ReleaseDate = DateTime.Now,
                    Tag = $"v{newTag.WithPatch(newTag.Patch + 1)}"
                };
                var previousReleasePath = Path.Join(
                    SourceRegistry.RegistryPath,
                    latestRelease.Manifest.Organization,
                    latestRelease.Manifest.RepoName,
                    latestRelease.Tag,
                    latestRelease.PakFileName
                );
                _windowService.PromptAddRelease(newRelease, previousReleasePath);
            }
        }
    }

    public partial class RegistryReleaseVM {
        public Release Release { get; set; }
        private ILocalModRegistryWindowService _windowService;
        [RelayCommand]
        public void Delete() {
            var _ = _windowService.Registry.DeleteRelease(ReleaseCoordinates.FromRelease(Release));
        }

        [RelayCommand]
        public void Edit() {
            var pakPath = Path.Join(
                _windowService.Registry.RegistryPath,
                Release.Manifest.Organization,
                Release.Manifest.RepoName,
                Release.Tag,
                Release.PakFileName
            );
            _windowService.PromptAddRelease(Release, pakPath);
        }

        public RegistryReleaseVM(Release release, ILocalModRegistryWindowService windowService) {
            Release = release;
            _windowService = windowService;
        }
    }

    /// <summary>
    /// Duplicates Release, but it's modifiable
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class RegistryReleaseFormVM {
        // Release stuff
        public string Tag { get; set; }
        public string ReleaseHash { get; set; }
        private string PakFileName => Path.GetFileName(PakFilePath) ?? "Unknown; invalid path";
        private string? _pakFilePath;
        [DoNotCheckEquality]
        public string? PakFilePath {
            get => _pakFilePath;
            set {
                _pakFilePath = value;
                if (value != null) {
                    // this will only error if the path is invalid, which it won't be because the
                    // view will validate it before binding
                    ReleaseHash = FileHelpers.Sha512(value).IfLeft(e => e.Message);
                }
            }
        }
        public DateTime ReleaseDate { get; set; }

        // Manifest stuff
        public string RepoUrl {
            get => $"{Organization}/{Regex.Replace(Name, "\\s", "-")}";
        }
        public string Organization { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string? HomePage { get; set; }
        public string? ImageUrl { get; set; }

        public static readonly Hashtable ModTypeMap = new Hashtable {
            {"Client", ModType.Client},
            {"Server", ModType.Server},
            {"Shared", ModType.Shared}
        };

        public string SelectedModType {
            get =>
                ModTypeMap.Keys.Cast<string>().ToList()
                    .Filter((k) => (ModType)ModTypeMap[k]! == ModType)
                    .First();
            set => ModType = (ModType)ModTypeMap[value]!;
        }

        public static List<string> ModTypeChoices { get; } = ModTypeMap.Keys.Cast<string>().ToList();
        public ModType ModType { get; set; }
        public List<string> Authors { get; set; }
        public List<Dependency> Dependencies { get; set; }
        public List<ModTag> Tags { get; set; }
        public List<string> Maps { get; set; }
        public OptionFlags OptionFlags { get; set; }

        public string LastSubmitComplaint { get; set; } = "";

        [RelayCommand]
        public void Submit() {
            if (PakFilePath == null) {
                LastSubmitComplaint = "Please add a pak file";
                return;
            }
            OnSubmit?.Invoke();
        }
        public event Action? OnSubmit;
        public RegistryReleaseFormVM(Release release, string? pakFilePath) {
            PakFilePath = pakFilePath;
            // Release stuff
            Tag = release.Tag;
            ReleaseHash = release.ReleaseHash;
            ReleaseDate = release.ReleaseDate;

            // Manifest stuff
            Organization = release.Manifest.Organization;
            Name = release.Manifest.Name;
            Description = release.Manifest.Description;
            HomePage = release.Manifest.HomePage;
            ImageUrl = release.Manifest.ImageUrl;
            ModType = release.Manifest.ModType;
            Authors = release.Manifest.Authors;
            Dependencies = release.Manifest.Dependencies;
            Tags = release.Manifest.Tags;
            Maps = release.Manifest.Maps;
            OptionFlags = release.Manifest.OptionFlags;
        }

        public (Release release, string pakPath) ToRelease() {
            ModManifest m = new ModManifest(
                RepoUrl,
                Name,
                Description,
                HomePage,
                ImageUrl,
                ModType,
                Authors,
                Dependencies,
                Tags,
                Maps,
                OptionFlags);

            if (PakFilePath == null) {
                throw new InvalidOperationException("Pak file path is not set");
            }

            Release r = new Release(
                Tag,
                ReleaseHash,
                PakFileName,
                ReleaseDate,
                m);

            return (r, PakFilePath);
        }
    }
}