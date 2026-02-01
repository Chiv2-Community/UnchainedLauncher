using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.PakDir {
    public record ModInstallRequest(ReleaseCoordinates Version, IPakDir.MakeFileWriter Writer);
    public record ManagedPak(ReleaseCoordinates Coordinates, string PakFileName, int Priority) {
        public override string ToString() => $"{Coordinates} @ {PakFileName} (priority {Priority})";
    }
    
    public interface IPakDir {
        delegate EitherAsync<Error, FileWriter> MakeFileWriter(string filePath);
        Either<Error, Unit> Uninstall(ModIdentifier coords);
        IAsyncEnumerable<Either<Error, ManagedPak>> InstallModSet(IEnumerable<ModInstallRequest> installs, Option<AccumulatedMemoryProgress> progress);
        Option<ManagedPak> GetInstalledPakFile(ModIdentifier coords);
        IEnumerable<ManagedPak> GetManagedPaks();
        Either<IEnumerable<Error>, Unit> SignUnmanaged();
        Either<IEnumerable<Error>, Unit> UnSignUnmanaged();
        Either<IEnumerable<Error>, Unit> Reset();
    }
}