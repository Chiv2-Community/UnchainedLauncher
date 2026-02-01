using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.PakDir {
    /// <summary>
    /// Represents a request to install a mod, containing the release coordinates and a writer factory.
    /// </summary>
    /// <param name="Coordinates">The coordinates identifying the specific release to install</param>
    /// <param name="Writer">A factory function that creates a FileWriter for the given output path</param>
    public record ModInstallRequest(ReleaseCoordinates Coordinates, IPakDir.MakeFileWriter Writer);

    /// <summary>
    /// Represents a pak file that is managed by the PakDir system.
    /// </summary>
    /// <param name="Coordinates">The release coordinates identifying this pak</param>
    /// <param name="PakFileName">The filename of the pak file within the pak directory</param>
    /// <param name="Priority">The load priority of this pak (lower values load first)</param>
    public record ManagedPak(ReleaseCoordinates Coordinates, string PakFileName, int Priority) {
        public override string ToString() => $"{Coordinates} @ {PakFileName} (priority {Priority})";
    }

    /// <summary>
    /// Manages the pak directory for Chivalry 2 mods, handling installation, signing, and cleanup of pak files.
    /// </summary>
    public interface IPakDir {
        /// <summary>
        /// A delegate that creates a FileWriter for writing pak data to the specified file path.
        /// </summary>
        /// <param name="filePath">The destination file path to write to</param>
        /// <returns>An async either containing the FileWriter on success, or an Error on failure</returns>
        delegate EitherAsync<Error, FileWriter> MakeFileWriter(string filePath);

        /// <summary>
        /// Uninstalls a mod by removing its pak file and associated signature file from the pak directory.
        /// </summary>
        /// <param name="coords">The mod identifier specifying which mod to uninstall</param>
        /// <returns>Right(Unit) on success, Left(Error) on failure</returns>
        Either<Error, Unit> Uninstall(ModIdentifier coords);

        /// <summary>
        /// Installs a set of mods, handling dependency ordering, file naming conflicts, and progress reporting.
        /// Mods are sorted topologically by dependencies and named to ensure correct load order.
        /// </summary>
        /// <param name="installs">The collection of mod install requests to process</param>
        /// <param name="progress">Optional progress tracker for reporting installation progress</param>
        /// <returns>An async enumerable of results, one per install request</returns>
        IAsyncEnumerable<Either<Error, ManagedPak>> InstallModSet(IEnumerable<ModInstallRequest> installs, Option<AccumulatedMemoryProgress> progress);

        /// <summary>
        /// Signs all managed pak files by copying the base signature file to create corresponding .sig files.
        /// This is required for the game to load modded pak files.
        /// </summary>
        /// <returns>Right(Unit) if all files were signed successfully, Left with errors otherwise</returns>
        Either<IEnumerable<Error>, Unit> SignAll();

        /// <summary>
        /// Removes signature files for all managed pak files, effectively preventing them from loading.
        /// </summary>
        /// <returns>Right(Unit) if all signature files were removed successfully, Left with errors otherwise</returns>
        Either<IEnumerable<Error>, Unit> UnSignAll();

        /// <summary>
        /// Deletes any .sig files that don't have a corresponding .pak file.
        /// Useful for cleaning up after manual pak file deletions.
        /// </summary>
        /// <returns>Right(Unit) if cleanup succeeded, Left with errors otherwise</returns>
        Either<IEnumerable<Error>, Unit> DeleteOrphanedSigs();

        /// <summary>
        /// Removes all mod pak and sig files from the directory, restoring it to a clean state.
        /// Preserves the base game pak and sig files (pakchunk0-WindowsNoEditor).
        /// </summary>
        /// <returns>Right(Unit) if reset succeeded, Left with errors otherwise</returns>
        Either<IEnumerable<Error>, Unit> Reset();

        /// <summary>
        /// Gets the full file path for a managed pak file by its release coordinates.
        /// </summary>
        /// <param name="coords">The release coordinates to look up</param>
        /// <returns>Some(path) if the pak is managed, None otherwise</returns>
        Option<string> GetManagedPakFilePath(ReleaseCoordinates coords);
    }
}