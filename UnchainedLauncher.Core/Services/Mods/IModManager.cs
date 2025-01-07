using LanguageExt;
using LanguageExt.Common;
using System.Collections.ObjectModel;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods {
    using static LanguageExt.Prelude;

    public record UpdateCandidate(Release CurrentlyEnabled, Release AvailableUpdate) {
        public static Option<UpdateCandidate> CreateIfNewer(Release CurrentlyEnabled, Release AvailableUpdate) {
            return AvailableUpdate.Version.ComparePrecedenceTo(CurrentlyEnabled.Version) > 0
                ? Some(new UpdateCandidate(CurrentlyEnabled, AvailableUpdate))
                : None;
        }
    }

    public interface IModManager {

        /// <summary>
        /// A List of all currently enabled mods
        /// </summary>
        ObservableCollection<Release> EnabledModReleases { get; }

        /// <summary>
        /// A List of all mods which are available to be enabled.
        /// This list may be empty if UpdateModsList has not been called yet.
        /// </summary>
        IEnumerable<Mod> Mods { get; }

        /// <summary>
        /// Disables the given release. This includes deleting any pak files as well as removing
        /// any local metadata indicating that this release is enabled.
        /// </summary>
        /// <param name="release">The release to disable</param>
        /// <returns>Either an error containing information about why this failed, or nothing if it was successful.</returns>
        EitherAsync<DisableModFailure, Unit> DisableModRelease(Release release);

        /// <summary>
        /// Enables the given release for the given mod. This includes downloading any pak files
        /// as well as saving local metadata to indicate that this release is enabled.
        /// 
        /// Enabling an already enabled mod results in a noop
        /// Enabling a different version for an already enabled mod will disable the currently enabled version
        /// </summary>
        /// <param name="release">The release to enable</param>
        /// <param name="progress">An optional progress indicator. Progress in percentage will be reported to the provided IProgress instance.</param>
        /// <param name="cancellationToken">An optional cancellation token to stop any downloads</param>
        /// <returns>Either an Error containing information about what went wrong, or nothing if things were successful.</returns>
        EitherAsync<EnableModFailure, Unit> EnableModRelease(Release release, Option<IProgress<double>> progress, CancellationToken cancellationToken);

        /// <summary>
        /// Fetches all mod metadata from all registries, returning a list of successfully fetched
        /// mod metadata, as well as a list of failures containing any potential errors.  If this 
        /// IModManager implementation has a cached internal state, this method may update that 
        /// internal state.
        /// </summary>
        /// <returns>A Task containing a GetAllModsResult which has the aggregate list of errors and mods from all registries this ModManager instance has</returns>
        public Task<GetAllModsResult> UpdateModsList();

        /// <summary>
        /// Disables the given mod if it is enabled. This includes deleting any pak files as well as removing
        /// any local metadata indicating that this release is enabled.
        /// </summary>
        /// <param name="release">The release to disable</param>
        /// <returns>Either an error containing information about why this failed, or nothing if it was successful.</returns>
        public EitherAsync<DisableModFailure, Unit> DisableMod(Mod mod) {
            return GetCurrentlyEnabledReleaseForMod(mod).Match(
                Some: release => DisableModRelease(release),
                None: () => EitherAsync<DisableModFailure, Unit>.Right(default)
            );
        }


        /// <summary>
        /// Finds the currently enabled release for the given mod, if any
        /// </summary>
        /// <param name="mod"></param>
        /// <returns></returns>
        public Option<Release> GetCurrentlyEnabledReleaseForMod(Mod mod) {
            return Optional(EnabledModReleases.FirstOrDefault(x => x.Manifest.RepoUrl == mod.LatestManifest.RepoUrl));
        }

        /// <summary>
        /// Returns a list of all mods which have updates available
        /// </summary>
        /// <returns>A List of all available updates</returns>
        public IEnumerable<UpdateCandidate> GetUpdateCandidates() {
            return Mods
               .SelectMany(mod =>
                   (GetCurrentlyEnabledReleaseForMod(mod), mod.LatestRelease)
                       .Sequence() // Convert (Option<Release>, Option<Release>) to Option<(Release, Release)>
                       .Bind(tuple => UpdateCandidate.CreateIfNewer(tuple.Item1, tuple.Item2))
               );
        }
    }

    /// <summary>
    /// The failure domain of downloading a mod.
    /// 
    /// Do not invoke the constructors directly, use the static methods instead.
    /// </summary>
    public abstract record DownloadModFailure(string Message, int Code, Option<Error> Inner = default) : Expected(Message, Code, Inner) {

        public record ModPakStreamAcquisitionFailureWrapper(ModPakStreamAcquisitionFailure Failure) : DownloadModFailure(Failure.Message, Failure.Code, Failure.Inner);
        public record HashFailureWrapper(HashFailure Failure) : DownloadModFailure(Failure.Message, Failure.Code, Failure.Inner);
        public record HashMismatchFailure(Release Release, Option<string> InvalidHash) : DownloadModFailure($"Mismatched hashes. Got '{InvalidHash.IfNone("None")}', Expected '{Release.ReleaseHash}'", 4001);
        public record ModNotFoundFailure(Release Release) : DownloadModFailure($"Mod release verion '{Release.Tag}' for '{Release.Manifest.Name}' not found. Please contact the author.", 4002);
        public record WriteFailure(string Path, Error Failure) : DownloadModFailure($"Failed to write mod to path '{Path}'. Reason: {Failure.Message}", Failure.Code, Some(Failure));
        public record AlreadyDownloadedFailure(Release Release) : DownloadModFailure($"Mod '{Release.Manifest.Name} {Release.Tag}' already downloaded. Cache may be corrupt.", 4003);

        public DownloadModFailure Widen => this;
        public static DownloadModFailure Wrap(ModPakStreamAcquisitionFailure failure) => new ModPakStreamAcquisitionFailureWrapper(failure).Widen;
        public static DownloadModFailure Wrap(HashFailure failure) => new HashFailureWrapper(failure).Widen;

        public static DownloadModFailure HashMismatch(Release release, Option<string> invalidHash) => new HashMismatchFailure(release, invalidHash).Widen;
        public static DownloadModFailure ModNotFound(Release release) => new ModNotFoundFailure(release).Widen;
        public static DownloadModFailure WriteFailed(string path, Error failure) => new WriteFailure(path, failure);
        public static DownloadModFailure AlreadyDownloaded(Release release) => new AlreadyDownloadedFailure(release);

        public T Match<T>(
            Func<ModPakStreamAcquisitionFailure, T> ModPakStreamAcquisitionFailure,
            Func<HashFailure, T> HashFailure,
            Func<HashMismatchFailure, T> HashMismatchFailure,
            Func<ModNotFoundFailure, T> ModNotFoundFailure,
            Func<WriteFailure, T> WriteFailure,
            Func<AlreadyDownloadedFailure, T> AlreadyDownloadedFailure
        ) => this switch {
            ModPakStreamAcquisitionFailureWrapper wrapper => ModPakStreamAcquisitionFailure(wrapper.Failure),
            HashFailureWrapper wrapper => HashFailure(wrapper.Failure),
            HashMismatchFailure mismatch => HashMismatchFailure(mismatch),
            ModNotFoundFailure notFound => ModNotFoundFailure(notFound),
            WriteFailure write => WriteFailure(write),
            AlreadyDownloadedFailure alreadyDownloaded => AlreadyDownloadedFailure(alreadyDownloaded),
            _ => throw new Exception("Unreachable")
        };
    }

    /// <summary>
    /// The failure domain of disabling a mod.
    /// 
    /// Do not invoke the constructors directly, use the static methods instead.
    /// </summary>
    public abstract record DisableModFailure(string Message, int Code, Option<Error> Inner = default) : Expected(Message, Code, Inner) {
        public record DeleteFailure(string Path, Error Failure) : DisableModFailure($"Failed to delete file at '{Path}'.  Reason: {Failure.Message}", Failure.Code, Some(Failure));
        public record ModNotEnabledFailure(Release Release) : DisableModFailure($"Attempted to disable a mod ('{Release.Manifest.Name}') that is not enabled. Cache may be corrupt.", 4004);

        public static DisableModFailure DeleteFailed(string path, Error failure) => new DeleteFailure(path, failure);
        public static DisableModFailure ModNotEnabled(Release release) => new ModNotEnabledFailure(release);

        public DisableModFailure Widen => this;

        public T Match<T>(
            Func<DeleteFailure, T> DeleteFailure,
            Func<ModNotEnabledFailure, T> ModNotEnabledFailure
        ) => this switch {
            DeleteFailure delete => DeleteFailure(delete),
            ModNotEnabledFailure notEnabled => ModNotEnabledFailure(notEnabled),
            _ => throw new Exception("Unreachable")
        };
    }

    /// <summary>
    /// The failure domain of enabling a mod.
    /// 
    /// Do not invoke the constructors directly, use the static methods instead.
    /// </summary>
    public abstract record EnableModFailure(string Message, int Code, Option<Error> Inner = default) : Expected(Message, Code, Inner) {
        public record DownloadModFailureWrapper(DownloadModFailure Failure) : EnableModFailure(Failure.Message, Failure.Code, Failure.Inner);
        public record DisableModFailureWrapper(DisableModFailure Failure) : EnableModFailure(Failure.Message, Failure.Code, Failure.Inner);


        public static EnableModFailure Wrap(DownloadModFailure failure) => new DownloadModFailureWrapper(failure);
        public static EnableModFailure Wrap(DisableModFailure failure) => new DisableModFailureWrapper(failure);

        public EnableModFailure Widen => this;

        public T Match<T>(
            Func<DownloadModFailure, T> DownloadModFailure,
            Func<DisableModFailure, T> DisableModFailure
        ) {
            return this switch {
                DownloadModFailureWrapper wrapper => DownloadModFailure(wrapper.Failure),
                DisableModFailureWrapper wrapper => DisableModFailure(wrapper.Failure),
                _ => throw new Exception("Unreachable")
            };
        }
    }
}