using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public class HttpPakDownloader : IModRegistryDownloader {
        public static HttpPakDownloader GithubPakDownloader => new HttpPakDownloader(
            $"https://github.com/<Org>/<Repo>/releases/download/<Version>/<PakFileName>"
        );

        public string UrlPattern { get; }

        public HttpPakDownloader(string urlPattern) {
            UrlPattern = urlPattern;
        }

        public string GetDownloadURL(Release r) =>
            UrlPattern
                .Replace("<Org>", r.Manifest.Organization)
                .Replace("<Repo>", r.Manifest.RepoName)
                .Replace("<Version>", r.Tag)
                .Replace("<PakFileName>", r.PakFileName);

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