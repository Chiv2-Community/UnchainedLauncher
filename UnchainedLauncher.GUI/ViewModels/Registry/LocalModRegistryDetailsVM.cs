using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using Semver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;
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
                    new List<string>(),
                    new List<string> { "Example Author" },
                    new List<Dependency>()
                ),
                Manifest: new AssetCollections(
                    Markers: new List<ModMarkerDto>(),
                    Blueprints: new List<BlueprintDto>(),
                    Maps: new List<MapDto>(),
                    Replacements: new List<ReplacementDto>(),
                    Arbitrary: new List<ArbitraryDto>()
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

}