using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.PakDir
{
    public interface IPakDir
    {
        public delegate EitherAsync<Error,FileWriter> MakeFileWriter(string filePath);
        IEnumerable<string> GetPakFiles();
        IEnumerable<string> GetSigFiles();
        Either<Error, string> GetDefaultSigFilePath();
        IEnumerable<string> GetModPakFiles();
        IEnumerable<string> GetModdedSigFiles();
        Either<Error, Unit> Uninstall(ReleaseCoordinates coords);
        Either<Error, Unit> Uninstall(ModIdentifier coords);
        EitherAsync<Error, Unit> Install(ReleaseCoordinates coords, MakeFileWriter mkFileWriter, string suggestedFileName);
        EitherAsync<Error, Unit> InstallOnly(IEnumerable<(ReleaseCoordinates version, MakeFileWriter, string suggestedPakName)> installs);
        Either<Error, Unit> Sign(ReleaseCoordinates coords);
        Either<Error, Unit> Unsign(ReleaseCoordinates coords);
        Either<Error, Unit> Unsign(ModIdentifier coords);
        Either<Error, Unit> SignOnly(IEnumerable<ReleaseCoordinates> coords);
        bool IsSigned(ReleaseCoordinates coords);
        bool IsSigned(ModIdentifier coords);
        Option<string> GetInstalledPakFile(ReleaseCoordinates coords);
        Option<string> GetInstalledPakFile(ModIdentifier coords);
        IEnumerable<ReleaseCoordinates> GetSignedReleases();
        IEnumerable<ReleaseCoordinates> GetInstalledReleases();
        IEnumerable<string> GetUnmanagedPaks();
        IEnumerable<string> GetUnmanagedSigs();
        Either<Error, Unit> SignUnmanaged();
        Either<Error, Unit> UnSignUnmanaged();
        Either<Error, Unit> DeleteUnmanaged();
        Either<Error, Unit> CleanUpDir();
        Either<Error, Unit> DeleteOrphanedSigs();
    }
}