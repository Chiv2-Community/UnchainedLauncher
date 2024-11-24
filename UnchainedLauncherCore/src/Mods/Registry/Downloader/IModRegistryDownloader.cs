using LanguageExt;
using LanguageExt.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnchainedLauncher.Core.Mods.Registry.Resolver
{
    using static LanguageExt.Prelude;
    public interface IModRegistryDownloader
    {
        public abstract EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(PakTarget target);
    }

    public record PakTarget(string Org, string RepoName, string FileName, string ReleaseTag);
    public record SizedStream(Stream Stream, long Size);
    public record ModPakStreamAcquisitionFailure(PakTarget Target, Error Error): Expected($"Failed to acquire download stream for mod pak {Target.FileName}", 4000, Some(Error));
}
