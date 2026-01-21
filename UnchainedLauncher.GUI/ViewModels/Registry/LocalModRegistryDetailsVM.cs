using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using Semver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels.Registry {
    using static LanguageExt.Prelude;


    [AddINotifyPropertyChangedInterface]
    public partial class LocalModRegistryDetailsVM {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(LocalModRegistryDetailsVM));

        private IRegistryWindowService _windowService;

        public LocalModRegistry Registry { get; }
        public ObservableCollection<RegistryModVM> Mods { get; } = new();
        public LocalModRegistryDetailsVM(LocalModRegistry registry, IRegistryWindowService windowService) {
            _windowService = windowService;
            Registry = registry;
            Registry.OnRegistryChanged += RefreshModsList;
            RefreshModsList(null);
        }

        [RelayCommand]
        public void AddRelease() {
            var dummyRelease = new Release(
                "v0.0.0",
                "",
                "unknown",
                DateTime.Now,
                new ModInfo(
                    "Unchained-Mods/Example-Mod",
                    "MyFancyMod",
                    "Example Description",
                    null,
                    null,
                    null,
                    new List<string> { "Example Author" },
                    new List<Dependency>()
                ),
                new PakManifest(
                    PakName: "Foo.pak",
                    PakPath: "Foo.pak",
                    PakHash: null,
                    Inventory: new AssetCollections(
                        Markers: new List<ModMarkerDto>(),
                        Blueprints: new List<BlueprintDto>(),
                        Maps: new List<MapDto>(),
                        Replacements: new List<ReplacementDto>(),
                        Arbitrary: new List<ArbitraryDto>()
                    )
                ),
                "# Example Release Notes\n\n* Your release notes here"
            );

            _windowService.PromptAddRelease(Registry, new RegistryReleaseFormVM(dummyRelease, null));
        }

        public async void RefreshModsList(ReleaseCoordinates? updatedMod) {
            try {
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

                await Application.Current.Dispatcher.BeginInvoke((Action)delegate () {
                    Mods.Clear();
                    newModVMs.ToList().ForEach(m => Mods.Add(m));
                });
            }
            catch (Exception e) {
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
        public LocalModRegistry SourceRegistry { get; }
        private IRegistryWindowService _windowService;

        public RegistryModVM(LocalModRegistry registry, IRegistryWindowService windowService, Mod mod) {
            _windowService = windowService;
            Mod = mod;
            SourceRegistry = registry;
            PopulateReleases();
        }

        // This must be called from UI thread!
        private void PopulateReleases() {
            Releases.Clear();
            Mod.Releases
                .ForEach(r =>
                    Releases.Add(new RegistryReleaseVM(
                            SourceRegistry,
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
                    latestRelease.Info.Organization,
                    latestRelease.Info.RepoName,
                    latestRelease.Tag,
                    latestRelease.PakFileName
                );
                _windowService.PromptAddRelease(SourceRegistry, new RegistryReleaseFormVM(newRelease, previousReleasePath));
            }
        }
    }

    public partial class RegistryReleaseVM {
        public Release Release { get; set; }
        private IRegistryWindowService _windowService;
        public LocalModRegistry SourceRegistry { get; }

        public RegistryReleaseVM(LocalModRegistry sourceRegistry, Release release, IRegistryWindowService windowService) {
            SourceRegistry = sourceRegistry;
            Release = release;
            _windowService = windowService;
        }

        [RelayCommand]
        public void Delete() {
            var _ = SourceRegistry.DeleteRelease(ReleaseCoordinates.FromRelease(Release));
        }

        [RelayCommand]
        public void Edit() {
            var pakPath = Path.Join(
                SourceRegistry.RegistryPath,
                Release.Info.Organization,
                Release.Info.RepoName,
                Release.Tag,
                Release.PakFileName
            );
            _windowService.PromptAddRelease(SourceRegistry, new RegistryReleaseFormVM(Release, pakPath));
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
        public string? IconUrl { get; set; }
        public List<string> ImageUrls { get; set; }

        public List<string> Authors { get; set; }
        public List<Dependency> Dependencies { get; set; }

        public string ReleaseNotesMarkdown { get; set; }

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
            Organization = release.Info.Organization;
            Name = release.Info.Name;
            Description = release.Info.Description;
            HomePage = release.Info.HomePage;
            IconUrl = release.Info.IconUrl;
            ImageUrls = release.Info.ImageUrls;
            Authors = release.Info.Authors;
            Dependencies = release.Info.Dependencies;
            ReleaseNotesMarkdown = release.ReleaseNotesMarkdown ?? "";
        }

        public (Release release, string pakPath) ToRelease() {
            ModInfo m = new ModInfo(
                RepoUrl,
                Name,
                Description,
                HomePage,
                IconUrl,
                ImageUrls,
                Authors,
                Dependencies
            );

            if (PakFilePath == null) {
                throw new InvalidOperationException("Pak file path is not set");
            }

            Release r = new Release(
                Tag,
                ReleaseHash,
                PakFileName,
                ReleaseDate,
                m,
                null,
                ReleaseNotesMarkdown);

            return (r, PakFilePath);
        }
    }
}