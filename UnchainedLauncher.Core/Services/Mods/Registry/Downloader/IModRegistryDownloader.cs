using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;


namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public interface IModRegistryDownloader {
        public EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(ReleaseCoordinates target, String releasePakName);
    }
    
    public record SizedStream(Stream Stream, long Size);
    public record ModPakStreamAcquisitionFailure(ReleaseCoordinates Target, Error Error) : Expected($"Failed to acquire download stream for mod pak {Target.FileName}", 4000, Some(Error));
}