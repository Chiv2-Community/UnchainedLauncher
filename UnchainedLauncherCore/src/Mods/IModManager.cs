using LanguageExt;
using LanguageExt.Common;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods.Registry;

namespace UnchainedLauncher.Core.Mods {

    public record UpdateCandidate(Release CurrentlyEnabled, Release AvailableUpdate);

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
        EitherAsync<Error, Unit> DisableModRelease(Release release);

        /// <summary>
        /// Downloads all mod files for all enabled mods as well as the unchained plugin.
        /// TODO: Delete this.
        /// </summary>
        /// <param name="downloadPlugin"></param>
        /// <returns>Either an error containing information about what went wrong, or the nothing if successful</returns>
        EitherAsync<Error, Unit> DownloadModFiles(bool downloadPlugin);

        /// <summary>
        /// Enables the given release for the given mod. This includes downloading any pak files
        /// as well as saving local metadata to indicate that this release is enabled.
        /// </summary>
        /// <param name="release">The release to enable</param>
        /// <param name="progress">An optional progress indicator. Progress in percentage will be reported to the provided IProgress instance.</param>
        /// <param name="cancellationToken">An optional cancellation token to stop any downloads</param>
        /// <returns>Either an Error containing information about what went wrong, or nothing if things were successful.</returns>
        EitherAsync<Error, Unit> EnableModRelease(Release release, Option<IProgress<double>> progress, CancellationToken cancellationToken);

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
        public EitherAsync<Error, Unit> DisableMod(Mod mod) {
            return GetCurrentlyEnabledReleaseForMod(mod).Match(
                Some: release => DisableModRelease(release),
                None: () => EitherAsync<Error, Unit>.Right(Unit.Default)
            );
        }


        /// <summary>
        /// Finds the currently enabled release for the given mod, if any
        /// </summary>
        /// <param name="mod"></param>
        /// <returns></returns>
        public Option<Release> GetCurrentlyEnabledReleaseForMod(Mod mod) {
            return Prelude.Optional(EnabledModReleases.FirstOrDefault(x => x.Manifest.RepoUrl == mod.LatestManifest.RepoUrl));
        }

        /// <summary>
        /// Returns a list of all mods which have updates available
        /// </summary>
        /// <returns>A List of all available updates</returns>
        public IEnumerable<UpdateCandidate> GetUpdateCandidates() {
            return Mods
               .Select(mod =>
                   (GetCurrentlyEnabledReleaseForMod(mod), mod.LatestRelease)
                       .Sequence() // Convert (Option<Release>, Option<Release>) to Option<(Release, Release)>
                       .Map(tuple => new UpdateCandidate(tuple.Item1, tuple.Item2))
               )
               .Collect(result => result.ToImmutableList()) // Filter out mods that aren't enabled
               .Where(tuple => tuple.AvailableUpdate.Version.ComparePrecedenceTo(tuple.CurrentlyEnabled.Version) > 0); // Filter out older releases
        }
    }
}