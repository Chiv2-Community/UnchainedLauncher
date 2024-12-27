using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using Semver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    public partial class ModViewModel : INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModViewModel));
        private readonly IModManager modManager;

        // Properties
        public Mod Mod { get; }
        public Release? EnabledRelease { get; set; }
        public Either<DisableModFailure, EnableModFailure>? ErrorState { get; set; }
        public ICommand ButtonCommand { get; }

        // Computed Properties
        public bool IsEnabled {
            get => EnabledRelease != null;
            set => EnabledRelease = value ? Mod.LatestRelease.FirstOrDefault() : null;
        }

        public string ImageUrl =>
            Mod?.LatestManifest?.ImageUrl ??
            "https://avatars.githubusercontent.com/u/108312122?s=96&v=4";

        public bool HasError => Optional(ErrorState).IsSome;

        public string TagsString =>
            string.Join(", ", Mod.LatestManifest.Tags);

        public List<string> AvailableVersions =>
            Mod.Releases.Select(x => x.Tag).ToList();

        public VersionNameSort VersionNameSortKey =>
            new(
                Optional(EnabledRelease).Map(x => x.Version).FirstOrDefault(),
                Mod.LatestManifest.Name
            );

        public string Description {
            get {
                var manifest =
                    EnabledRelease == null
                        ? Mod.LatestManifest
                        : EnabledRelease.Manifest;

                var message = manifest.Description;

                if (manifest.Dependencies.Count > 0) {
                    message += BuildDependenciesMessage(manifest);
                }

                ErrorState.IfSome(error => message += BuildErrorMessage(error));

                return message;
            }
        }

        // Constructor
        public ModViewModel(Mod mod, Release? enabledRelease, IModManager modManager) {
            Mod = mod;
            this.modManager = modManager;
            EnabledRelease = enabledRelease;

            ButtonCommand = new RelayCommand(DisableOrEnable);

            PropertyChangedEventManager.AddHandler(
                this,
                async (sender, e) => {
                    if (e.PropertyName == nameof(EnabledRelease)) {
                        await UpdateCurrentlyEnabledVersion(EnabledRelease);
                    }
                },
                nameof(EnabledRelease)
            );


            logger.Debug($"Initialized ModViewModel for {mod.LatestManifest.Name}. Currently enabled release: {EnabledRelease?.Tag}");
        }

        // Public Methods
        public Option<UpdateCandidate> CheckForUpdate() =>
            (Optional(EnabledRelease), Mod.LatestRelease)
                .Sequence()
                .Bind(x => UpdateCandidate.CreateIfNewer(x.Item1, x.Item2));

        public EitherAsync<Either<DisableModFailure, EnableModFailure>, Unit> UpdateCurrentlyEnabledVersion(Release? newVersion) {
            var failureOrSuccess = Optional(newVersion).Match(
                None: () => modManager.DisableMod(Mod)
                    .MapLeft<Either<DisableModFailure, EnableModFailure>>(e => Left(e)),
                Some: x => modManager.EnableModRelease(x, None, CancellationToken.None)
                    .MapLeft<Either<DisableModFailure, EnableModFailure>>(e => Right(e))
            );

            return failureOrSuccess.Match(
                Right: _ => {
                    ErrorState = null;
                    logger.Debug($"Successfully enabled release {EnabledRelease?.Tag} for {Mod.LatestManifest.Name}");
                },
                Left: error => {
                    ErrorState = error;
                    LogError(error);
                }
            );
        }

        // Private Methods
        private void DisableOrEnable() {
            EnabledRelease = EnabledRelease == null ?
                Mod.Releases.First() :
                null;
        }

        private string BuildDependenciesMessage(ModManifest manifest) {
            var depMessage = manifest.Dependencies
                .Select(dep => {
                    var mod = modManager.Mods.FirstOrDefault(x =>
                        x.LatestManifest.RepoUrl == dep.RepoUrl);

                    if (mod == null) {
                        logger.Warn($"Mod {manifest.Name} depends on {dep.Version}, but it doesn't exist.");
                        return null;
                    }

                    return $"- {mod.LatestManifest.Name} {dep.Version}";
                })
                .Where(msg => msg != null)
                .ToList();

            return depMessage.Any()
                ? $"\n\nYou must also enable the dependencies below:\n{string.Join("\n", depMessage)}\n"
                : string.Empty;
        }

        private string BuildErrorMessage(Either<DisableModFailure, EnableModFailure> error) =>
            "\n\nError: " + error.Match(
                Left: BuildDisableErrorMessage,
                Right: BuildEnableErrorMessage
            );

        private void LogError(Either<DisableModFailure, EnableModFailure> error) =>
            error.Match(
                Left: _ => logger.Error($"Failed to disable mod {Mod.LatestManifest.Name}"),
                Right: _ => logger.Error($"Failed to enable release {EnabledRelease?.Tag} for {Mod.LatestManifest.Name}")
            );

        private string BuildDisableErrorMessage(DisableModFailure failure) =>
            failure.Match(
                deleteFailure => $"Failed to disable mod. Cannot delete currently enabled version: {deleteFailure.Failure.Message}",
                notEnabled => "Cannot disable mod. Mod is not enabled."
            );

        private string BuildEnableErrorMessage(EnableModFailure failure) =>
            failure.Match(
                downloadFailure => BuildDownloadErrorMessage(downloadFailure),
                disableModFailure => BuildDisableForEnableErrorMessage(disableModFailure)
            );

        // Helper methods for specific error messages...
        private string BuildDownloadErrorMessage(DownloadModFailure failure) =>
            failure.Match(
                modPakStreamAcquisitionFailure => $"Failed to download mod file '{modPakStreamAcquisitionFailure.Target.FileName}' @{modPakStreamAcquisitionFailure.Target.ReleaseTag} from {modPakStreamAcquisitionFailure.Target.Org}/{modPakStreamAcquisitionFailure.Target.RepoName}. Reason: ${modPakStreamAcquisitionFailure.Error.Message}",
                hashFailure => $"Failed to hash mod file '{hashFailure.FilePath}'. Reason: {hashFailure.Error.Message}",
                hashMismatchFailure => $"Failed to validate mod hash. Expected '{hashMismatchFailure.Release.ReleaseHash}' Got '{hashMismatchFailure.InvalidHash.IfNone(() => "NULL")}'",
                modNotFoundFailure => $"Failed to download mod file. Mod release '{modNotFoundFailure.Release.PakFileName}' @{modNotFoundFailure.Release.Tag} from {modNotFoundFailure.Release.Manifest.RepoUrl} not found.",
                writeFailure => $"Failed to write mod to path '{writeFailure.Path}'. Reason: {writeFailure.Failure.Message}",
                alreadyDownloadedFailure => $"Mod already downloaded. Mod release '{alreadyDownloadedFailure.Release.PakFileName}' @{alreadyDownloadedFailure.Release.Tag} from {alreadyDownloadedFailure.Release.Manifest.RepoUrl}."
            );

        private string BuildDisableForEnableErrorMessage(DisableModFailure failure) =>
            failure.Match(
                deleteFailure => $"Failed to change mod version. Cannot delete currently enabled version: {deleteFailure.Failure.Message}",
                notEnabled => "Cannot disable currently enabled mod. Mod is not enabled. Something funky is going on."
            );
    }

    public record VersionNameSort(SemVersion? Version, string Name) : IComparable<VersionNameSort> {
        public int CompareTo(VersionNameSort? other) {
            if (other == null) return 1;
            if (Version == null) return 1;
            if (other.Version == null) return -1;
            return Name.CompareTo(other.Name);
        }
    }
}