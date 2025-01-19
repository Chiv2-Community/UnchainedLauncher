using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public class HttpPakDownloader : IModRegistryDownloader {
        public static HttpPakDownloader GithubPakDownloader => new HttpPakDownloader(target =>
            $"https://github.com/{target.Manifest.Organization}/{target.Manifest.RepoName}/releases/download/{target.Version}/{target.PakFileName}"
        );

        public Func<Release, string> GetDownloadURL { get; set; }
        public HttpPakDownloader(Func<Release, string> getDownloadUrl) {
            GetDownloadURL = getDownloadUrl;
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(Release target) {
            var url = GetDownloadURL(target);
            var length = HttpHelpers.GetContentLengthAsync(url);
            var streamDownloadTask = HttpHelpers.GetByteContentsAsync(url).Task;
            return
                Prelude.TryAsync(length)
                    .Bind(length => Prelude.TryAsync(streamDownloadTask)
                        .Map(stream => new SizedStream(stream, length))
                    )
                    .ToEither()
                    .MapLeft(e => new ModPakStreamAcquisitionFailure(ReleaseCoordinates.FromRelease(target), Error.New($"Failed to fetch pak from {url}.", e)));
        }
    }
}