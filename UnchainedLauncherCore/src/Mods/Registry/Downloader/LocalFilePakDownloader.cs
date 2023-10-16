using LanguageExt;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry.Resolver
{
    public class LocalFilePakDownloader : ModRegistryDownloader
    {
        public string PakReleasesDir;

        public LocalFilePakDownloader(string pakReleasesDir)
        {
            PakReleasesDir = pakReleasesDir;
        }

        public override EitherAsync<string, Stream> DownloadModPak(PakTarget target)
        {
            // Paks will be found in PakReleasesDir/org/repoName/releaseTag/fileName
            var path = Path.Combine(PakReleasesDir, target.Org, target.RepoName, target.ReleaseTag, target.FileName);

            if (!File.Exists(path))
                return Prelude.LeftAsync<string, Stream>($"Failed to fetch pak. File not found: {path}");

            return Prelude.Try(() => (Stream)File.OpenRead(path))
                .ToEither()
                .MapLeft(e => e.Message)
                .ToAsync();
        }
    }
}
