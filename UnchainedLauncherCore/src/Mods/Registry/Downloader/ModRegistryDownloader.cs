using LanguageExt;
using LanguageExt.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnchainedLauncher.Core.Mods.Registry.Resolver
{
    public abstract class ModRegistryDownloader
    {
        public abstract EitherAsync<Error, Stream> ModPakStream(PakTarget target);
    }

    public record PakTarget(string Org, string RepoName, string FileName, string ReleaseTag);
}
