using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Mods.Registry.Downloader {
    public class HttpPakDownloader : IModRegistryDownloader {
        public static HttpPakDownloader GithubPakDownloader => new HttpPakDownloader(target =>
            $"https://github.com/{target.Org}/{target.RepoName}/releases/download/{target.ReleaseTag}/{target.FileName}"
        );

        public Func<PakTarget, string> GetDownloadURL { get; set; }
        public HttpPakDownloader(Func<PakTarget, string> getDownloadUrl) {
            GetDownloadURL = getDownloadUrl;
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(PakTarget target) {
            var url = GetDownloadURL(target);
            var length = HttpHelpers.GetContentLengthAsync(url);
            var streamDownloadTask = HttpHelpers.GetByteContentsAsync(url).Task;
            return
                Prelude.TryAsync(length)
                    .Bind(length => Prelude.TryAsync(streamDownloadTask)
                        .Map(stream => new SizedStream(stream, length))
                    )
                    .ToEither()
                    .MapLeft(e => new ModPakStreamAcquisitionFailure(target, Error.New($"Failed to fetch pak from {url}.", e)));
        }
    }
}