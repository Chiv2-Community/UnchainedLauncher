using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using CommunityToolkit.Mvvm.Input;
using log4net;
using PropertyChanged;
using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using UnchainedLauncher.Core.Mods;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.Threading;

namespace UnchainedLauncher.GUI.ViewModels
{
    [AddINotifyPropertyChangedInterface]
    public partial class ModViewModel : INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModViewModel));
        // A ModViewModel needs access to the mod manager so that it can enable/disable releases as they get set on the view.
        private IModManager ModManager { get; }
        public Mod Mod { get; }

        public VersionNameSort VersionNameSortKey => new VersionNameSort(
            EnabledRelease.Map(x => x.Version).FirstOrDefault(), 
            Mod.LatestManifest.Name
        );

        public Option<Release> EnabledRelease { get; set; }

        public string Description {
            get {
                var manifest = EnabledRelease.Match(
                    None: Mod.LatestManifest,
                    Some: x => x.Manifest
                );

                var message = manifest.Description;

                if (manifest.Dependencies.Count > 0) {
                    message += "\n\nYou must also enable the dependencies below:\n";
                    foreach (var dep in manifest.Dependencies) {
                        var mod = ModManager.Mods.FirstOrDefault(x => x.LatestManifest.RepoUrl == dep.RepoUrl);
                        if (mod != null)
                            message += $"- {mod.LatestManifest.Name} {dep.Version}\n";
                    }
                }

                return message;
            }
        }

        public string ButtonText {
            get {
                return EnabledRelease.Match(
                    None: () => "Enable",
                    Some: _ => "Disable"
                );
            }
        }


        public string TagsString {
            get { return string.Join(", ", Mod.LatestManifest.Tags); }
        }

        public bool IsEnabled {
            get { return EnabledRelease != null; }
        }

        public string EnabledVersion {
            get {
                return EnabledRelease.Match(
                    None: () => "none",
                    Some: x => x.Tag
                );
            }
        }

        public List<string> AvailableVersions {
            get { return Mod.Releases.Select(x => x.Tag).ToList(); }
        }

        public ICommand ButtonCommand { get; }

        public ModViewModel(Mod mod, Option<Release> enabledRelease, IModManager modManager) {
            EnabledRelease = enabledRelease;

            Mod = mod;
            ModManager = modManager;

            ButtonCommand = new RelayCommand(DisableOrEnable);

            PropertyChangedEventManager.AddHandler(this, async (sender, e) => {
                if (e.PropertyName == nameof(EnabledRelease)) {
                    await UpdateCurrentlyEnabledVersion(EnabledRelease);
                }
            }, nameof(EnabledRelease));
            
            logger.Debug($"Initialized ModViewModel for {mod.LatestManifest.Name}. Currently enabled release: {EnabledVersion}");
        }

        private void DisableOrEnable() {
            if (EnabledRelease == null)
                EnabledRelease = Mod.Releases.First();
            else
                EnabledRelease = null;
        }

        private EitherAsync<Either<DisableModFailure, EnableModFailure>, Unit> UpdateCurrentlyEnabledVersion(Option<Release> newVersion) {
            return newVersion.Match(
                None: () => ModManager.DisableMod(Mod).MapLeft<Either<DisableModFailure, EnableModFailure>>(e => Prelude.Left(e)),
                Some: x => ModManager.EnableModRelease(x, Prelude.None, CancellationToken.None).MapLeft<Either<DisableModFailure, EnableModFailure>>(e => Prelude.Right(e))
            );
        }
    }

    public record VersionNameSort(SemVersion? Version, string Name) : IComparable<VersionNameSort> {
        public int CompareTo(VersionNameSort? other) {
            if(other == null)
                return 1;

            if (Version == null)
                return 1;

            if (other.Version == null)
                return -1;

            return Name.CompareTo(other.Name);
        }
    }
}
