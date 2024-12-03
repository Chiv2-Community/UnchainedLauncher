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
using System.Xml.Serialization;
using UnchainedLauncher.Core.Extensions;
using System.CodeDom;
using LanguageExt.Common;
using static UnchainedLauncher.Core.Mods.DownloadModFailure;
using UnchainedLauncher.Core.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.GUI.ViewModels
{
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public partial class ModViewModel : INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModViewModel));
        // A ModViewModel needs access to the mod manager so that it can enable/disable releases as they get set on the view.
        private IModManager ModManager { get; }
        public Mod Mod { get; }

        public VersionNameSort VersionNameSortKey => new VersionNameSort(
            Optional(EnabledRelease).Map(x => x.Version).FirstOrDefault(), 
            Mod.LatestManifest.Name
        );

        public bool HasError {
            get {
                return ErrorState.Match(
                    None: () => false,
                    Some: x => true
                );
            }
        }

        public Either<DisableModFailure, EnableModFailure>? ErrorState { get; set; }


        public Release? EnabledRelease { get; set; }

        public string Description {
            get {
                var manifest = Optional(EnabledRelease).Match(
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

                Optional(ErrorState).Match(
                    None: () => { },
                    Some: x => {

                        message +=
                            "\n\nError: " +
                            x.Match(
                                Left: disableModFailure => disableModFailure.Match(
                                    deleteFailure => $"Failed to disable mod. Cannot delete currently enabled version: {deleteFailure.Failure.Message}",
                                    notEnabled => $"Cannot disable mod. Mod is not enabled."
                                ),
                                Right: y => y.Match(
                                    downloadFailure => downloadFailure.Match(
                                        modPakStreamAcquisitionFailure => $"Failed to download mod file '{modPakStreamAcquisitionFailure.Target.FileName}' @{modPakStreamAcquisitionFailure.Target.ReleaseTag} from {modPakStreamAcquisitionFailure.Target.Org}/{modPakStreamAcquisitionFailure.Target.RepoName}. Reason: ${modPakStreamAcquisitionFailure.Error.Message}",
                                        hashFailure => $"Failed to hash mod file '{hashFailure.FilePath}'. Reason: " + hashFailure.Error.Message,
                                        hashMismatchFailure => $"Failed to validate mod hash. Expected '{hashMismatchFailure.Release.ReleaseHash}' Got '{hashMismatchFailure.InvalidHash.IfNone(() => "NULL")}'",
                                        modNotFoundFailure => $"Failed to download mod file. Mod release '{modNotFoundFailure.Release.PakFileName}' @{modNotFoundFailure.Release.Tag} from {modNotFoundFailure.Release.Manifest.RepoUrl} not found.",
                                        writeFailure => $"Failed to write mod to path '{writeFailure.Path}'. Reason: {writeFailure.Failure.Message}",
                                        alreadyDownloadedFailure => $"Mod already downloaded. Mod release '{alreadyDownloadedFailure.Release.PakFileName}' @{alreadyDownloadedFailure.Release.Tag} from {alreadyDownloadedFailure.Release.Manifest.RepoUrl}."
                                    ),
                                    disableModFailure => disableModFailure.Match(
                                        deleteFailure => $"Failed to change mod version. Cannot delete currently enabled version: {deleteFailure.Failure.Message}",
                                        notEnabled => $"Cannot disable currently enabled mod. Mod is not enabled. Something funky is going on."
                                    )
                            )
                        );
                    }
                );

                return message;
            }
        }

        public string ButtonText {
            get {
                return Optional(EnabledRelease).Match(
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
                return Optional(EnabledRelease).Match(
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
            EnabledRelease = enabledRelease.ValueUnsafe();

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

        public Option<UpdateCandidate> CheckForUpdate() {
            return (Optional(EnabledRelease), Mod.LatestRelease)
                .Sequence()
                .Bind(x => UpdateCandidate.CreateIfNewer(x.Item1, x.Item2));
        }

        private void DisableOrEnable() {
            if (EnabledRelease == null)
                EnabledRelease = Mod.Releases.First();
            else
                EnabledRelease = null;
        }

        public EitherAsync<Either<DisableModFailure, EnableModFailure>, Unit> UpdateCurrentlyEnabledVersion(Option<Release> newVersion) {
            var failureOrSuccess =  
                newVersion.Match(
                    None: () => ModManager.DisableMod(Mod).MapLeft<Either<DisableModFailure, EnableModFailure>>(e => Prelude.Left(e)),
                    Some: x =>
                        ModManager
                            .EnableModRelease(x, Prelude.None, CancellationToken.None)
                            .MapLeft<Either<DisableModFailure, EnableModFailure>>(enableModFailure => Prelude.Right(enableModFailure))
            );

            return failureOrSuccess.Match(
                Right: _ => {
                    ErrorState = null;
                    logger.Debug($"Successfully enabled release {EnabledVersion} for {Mod.LatestManifest.Name}");
                },
                Left: enableOrDisableError => {
                    ErrorState = enableOrDisableError;
                    enableOrDisableError.Match(
                        Left: disableModFailure => logger.Error($"Failed to disable mod {Mod.LatestManifest.Name}"),
                        Right: enableModFailure => logger.Error($"Failed to enable release {EnabledVersion} for {Mod.LatestManifest.Name}")
                    );
                }
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
