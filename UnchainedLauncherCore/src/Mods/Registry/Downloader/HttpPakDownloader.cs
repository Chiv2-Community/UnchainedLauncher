using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry.Resolver
{
    public class HttpPakDownloader : ModRegistryDownloader
    {
        public static HttpPakDownloader GithubPakDownloader = new HttpPakDownloader(target =>
            $"https://github.com/{target.Org}/{target.RepoName}/releases/download/{target.ReleaseTag}/{target.FileName}"
        );

        public Func<PakTarget, string> GetDownloadURL { get; set; }
        public HttpPakDownloader(Func<PakTarget, string> getDownloadUrl)
        {
            GetDownloadURL = getDownloadUrl;
        }

        public override EitherAsync<Error, Stream> ModPakStream(PakTarget target)
        {
            var url = GetDownloadURL(target);
            var streamDownloadTask = HttpHelpers.GetByteContentsAsync(url).Task;
            return Prelude.TryAsync(streamDownloadTask)
                .ToEither();
        }
    }
}
