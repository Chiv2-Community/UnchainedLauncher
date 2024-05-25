using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry.Resolver
{
    public class HttpPakDownloader : IModRegistryDownloader {
        public static HttpPakDownloader GithubPakDownloader => new HttpPakDownloader(target =>
            $"https://github.com/{target.Org}/{target.RepoName}/releases/download/{target.ReleaseTag}/{target.FileName}"
        );

        public Func<PakTarget, string> GetDownloadURL { get; set; }
        public HttpPakDownloader(Func<PakTarget, string> getDownloadUrl)
        {
            GetDownloadURL = getDownloadUrl;
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, Stream> ModPakStream(PakTarget target)
        {
            var url = GetDownloadURL(target);
            var streamDownloadTask = HttpHelpers.GetByteContentsAsync(url).Task;
            return Prelude.TryAsync(streamDownloadTask)
                .ToEither()
                .MapLeft(e => new ModPakStreamAcquisitionFailure(target, Error.New($"Failed to fetch pak from {url}.", e)));
        }
    }
}
