using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using static LanguageExt.Prelude;


namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public interface IModRegistryDownloader {
        public EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(Release release);
    }

    public record SizedStream(Stream Stream, long Size);
    public record ModPakStreamAcquisitionFailure(ReleaseCoordinates Target, Error Error) : Expected($"Failed to acquire download stream for mod pak '{Target.Org} / {Target.ModuleName} / {Target.Version}", 4000, Some(Error));
}