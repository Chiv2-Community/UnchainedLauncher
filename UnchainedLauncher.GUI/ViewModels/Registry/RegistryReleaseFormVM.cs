using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels.Registry {
    /// <summary>Duplicates Release, but it's modifiable.</summary>
    [AddINotifyPropertyChangedInterface]
    public partial class RegistryReleaseFormVM {
        private readonly ModInfo _originalInfo;
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
                    // The view validates the path before binding.
                    ReleaseHash = FileHelpers.Sha512(value).IfLeft(e => e.Message);
                }
            }
        }
        public DateTime ReleaseDate { get; set; }
        public AssetCollections Manifest { get; }
        public string RepoUrl => $"{Organization}/{Regex.Replace(Name, "\\s", "-")}";
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
            _originalInfo = release.Info;
            PakFilePath = pakFilePath;
            Tag = release.Tag;
            ReleaseHash = release.ReleaseHash;
            ReleaseDate = release.ReleaseDate;
            Manifest = release.Manifest;
            Organization = release.Info.Organization;
            Name = release.Info.Name;
            Description = release.Info.Description;
            HomePage = release.Info.HomePage;
            IconUrl = release.Info.IconUrl;
            ImageUrls = release.Info.ImageUrls;
            Authors = release.Info.Authors;
            Dependencies = release.Info.EffectiveModDependencies;
            ReleaseNotesMarkdown = release.ReleaseNotesMarkdown ?? "";
        }

        public (Release release, string pakPath) ToRelease() {
            var info = _originalInfo with {
                RepoUrl = RepoUrl,
                Name = Name,
                Description = Description,
                HomePage = HomePage,
                IconUrl = IconUrl,
                ImageUrls = ImageUrls,
                Authors = Authors,
                Dependencies = Dependencies,
                ModDependencies = null
            };
            if (PakFilePath == null) throw new InvalidOperationException("Pak file path is not set");
            return (new Release(Tag, ReleaseHash, PakFileName, ReleaseDate, info, Manifest, ReleaseNotesMarkdown), PakFilePath);
        }
    }
}
