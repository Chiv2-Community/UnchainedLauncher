using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using PropertyChanged;
using Semver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Views.Registry;

namespace UnchainedLauncher.GUI.ViewModels.Registry {
    using static LanguageExt.Prelude;

    public class ReleaseCreationHelper {
        public LocalModRegistry Registry { get; set; }
        
        public ReleaseCreationHelper(LocalModRegistry registry) {
            Registry = registry;
        }
        
        public RegistryReleaseFormVM PromptAddRelease(Release newRelease, string? previousPakPath = null) {
            var editableForm = new RegistryReleaseFormVM(newRelease, previousPakPath);
            var editWindow = new RegistryReleaseFormWindow();
            editWindow.DataContext = editableForm;
            editableForm.OnSubmit += editWindow.Close;
            editableForm.OnSubmit += async () => {
                var (res, p) = editableForm.ToRelease();
                await Registry.AddRelease(
                    res, 
                    p
                );
            };
            editWindow.Show();
            return editableForm;
        }

        public RegistryReleaseFormVM PromptAddRelease() {
            var dummyRelease = new Release(
                "v0.0.0",
                "",
                "unknown",
                DateTime.Now,
                new ModManifest(
                    "Unchained-Mods/Example-Mod",
                    "MyFancyMod",
                    "Example Description",
                    null,
                    null,
                    ModType.Shared,
                    new List<string> { "Example Author" },
                    new List<Dependency>(),
                    new List<ModTag> { ModTag.Doodad },
                    new List<string> { "ffa_exampleMap", "tdm_exampleMap" },
                    new OptionFlags(true)
                )
            );
            
            return PromptAddRelease(dummyRelease);
        }
    }
    
    [AddINotifyPropertyChangedInterface]
    public partial class LocalModRegistryWindowVM {
        public LocalModRegistry Registry { get; set; }
        public ObservableCollection<RegistryModVM> Mods { get; set; } = new();
        public ReleaseCreationHelper ReleaseCreationHelper { get; set; }
        public LocalModRegistryWindowVM(LocalModRegistry registry) {
            Registry = registry;
            ReleaseCreationHelper = new ReleaseCreationHelper(registry);
            registry.OnRegistryChanged += RefreshModsList;
            RefreshModsList();
        }

        [RelayCommand]
        public void AddRelease() {
            ReleaseCreationHelper.PromptAddRelease();
        }

        public void RefreshModsList() {
            var newMods = Registry.GetAllMods().Result;
                
            var newModVMs = newMods.Mods
                .ToList()
                .Map(m => {
                    var vm = new RegistryModVM(
                        Registry,
                        ReleaseCreationHelper,
                        m
                        );
                    
                    return vm;
                });
            // make sure this happens on the UI thread
            Application.Current.Dispatcher.BeginInvoke((Action)delegate()
            {
                Mods.Clear();
                newModVMs.ToList().ForEach(m => Mods.Add(m));
            });
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class RegistryModVM {
        public Mod Mod {
            get;
        }

        public ObservableCollection<RegistryReleaseVM> Releases { get; } = new();
        public LocalModRegistry SourceRegistry { get; set; }
        public ReleaseCreationHelper CreationHelper { get; set; }

        public RegistryModVM(LocalModRegistry registry, ReleaseCreationHelper creationHelper, Mod mod) {
            CreationHelper = creationHelper;
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
                            CreationHelper
                        )
                    )
                );
        }
        
        [RelayCommand]
        public void AddRelease() {
            var latestRelease = Mod.LatestRelease.FirstOrDefault();
            if (latestRelease != null) {
                SemVersion.TryParse(latestRelease.Tag, SemVersionStyles.AllowV, out var newTag);
                newTag = newTag ?? new SemVersion(0,0,0);
            
                var newRelease = latestRelease with {
                    ReleaseDate = DateTime.Now,
                    Tag = $"v{newTag.WithPatch(newTag.Patch+1).ToString()}"
                };
                var previousReleasePath = Path.Join(
                    SourceRegistry.RegistryPath,
                    latestRelease.Manifest.Organization,
                    latestRelease.Manifest.RepoName,
                    latestRelease.Tag,
                    latestRelease.PakFileName
                );
                CreationHelper.PromptAddRelease(newRelease, previousReleasePath);
            }
        }
    }

    public partial class RegistryReleaseVM {
        public Release Release { get; set; }
        private ReleaseCreationHelper _creationHelper;
        [RelayCommand]
        public void Delete() {
            var _ = _creationHelper.Registry.DeleteRelease(ReleaseCoordinates.FromRelease(Release));
        }

        [RelayCommand]
        public void Edit() {
            var pakPath = Path.Join(
                _creationHelper.Registry.RegistryPath,
                Release.Manifest.Organization,
                Release.Manifest.RepoName,
                Release.Tag,
                Release.PakFileName
            );
            _creationHelper.PromptAddRelease(Release, pakPath);
        }
        
        public RegistryReleaseVM(Release release, ReleaseCreationHelper creationHelper) {
            Release = release;
            _creationHelper = creationHelper;            
        }
    }
    
    /// <summary>
    /// Duplicates Release, but it's modifiable
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public partial class RegistryReleaseFormVM {
        // Release stuff
        public string Tag {get; set;}
        public string ReleaseHash {get; set;}
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
        public DateTime ReleaseDate {get; set;}
        
        // Manifest stuff
        public string RepoUrl {
            get => $"{Organization}/{Regex.Replace(Name, "\\s","-")}";
        }
        public string Organization {get; set;}
        public string Name {get; set;}
        public string Description {get; set;}
        public string? HomePage {get; set;}
        public string? ImageUrl {get; set;}
        
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
        public ModType ModType {get; set;}
        public List<string> Authors {get; set;}
        public List<Dependency> Dependencies {get; set;}
        public List<ModTag> Tags {get; set;}
        public List<string> Maps {get; set;}
        public OptionFlags OptionFlags {get; set;}

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