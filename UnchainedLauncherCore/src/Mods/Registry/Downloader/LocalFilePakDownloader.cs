using LanguageExt;
using LanguageExt.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry.Resolver
{
    public class LocalFilePakDownloader : IModRegistryDownloader {
        public string PakReleasesDir;

        public LocalFilePakDownloader(string pakReleasesDir)
        {
            PakReleasesDir = pakReleasesDir;
        }

        public override EitherAsync<ModPakStreamAcquisitionFailure, Stream> ModPakStream(PakTarget target)
        {
            // Paks will be found in PakReleasesDir/org/repoName/releaseTag/fileName
            var path = Path.Combine(PakReleasesDir, target.Org, target.RepoName, target.ReleaseTag, target.FileName);

            if (!File.Exists(path))
                return 
                    Prelude
                        .LeftAsync<ModPakStreamAcquisitionFailure, Stream>(
                            new ModPakStreamAcquisitionFailure(
                                target, 
                                Error.New($"Failed to fetch pak. File not found: {path}")
                            )
                        );

            return 
                Prelude
                    .TryAsync(Task.Run(() => (Stream)File.OpenRead(path)))
                    .ToEither()
                    .MapLeft(e => 
                        new ModPakStreamAcquisitionFailure(
                            target, 
                            Error.New($"Failed to fetch pak from {path}.", e)
                        )
                    );
        }
    }
}
