using LanguageExt;
using LanguageExt.Common;

namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    using static LanguageExt.Prelude;
    public interface IModRegistryDownloader {
        public abstract EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(ReleaseCoordinates target, String releasePakName);
    }
    
    public record SizedStream(Stream Stream, long Size);
    public record ModPakStreamAcquisitionFailure(ReleaseCoordinates Target, Error Error) : Expected($"Failed to acquire download stream for mod pak {Target.FileName}", 4000, Some(Error));
}