using LanguageExt;
using LanguageExt.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnchainedLauncher.Core.Mods.Registry.Resolver
{
    public interface IModRegistryDownloader
    {
        public abstract EitherAsync<ModPakStreamAcquisitionFailure, Stream> ModPakStream(PakTarget target);
    }

    public record PakTarget(string Org, string RepoName, string FileName, string ReleaseTag);

    public record ModPakStreamAcquisitionFailure(PakTarget Target, Error Error);
}
