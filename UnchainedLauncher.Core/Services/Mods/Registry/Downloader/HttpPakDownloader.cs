using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public class HttpPakDownloader : IModRegistryDownloader {
        public static HttpPakDownloader GithubPakDownloader => new HttpPakDownloader((target, pakFileName) =>
            $"https://github.com/{target.Org}/{target.ModuleName}/releases/download/{target.Version}/{pakFileName}"
        );

        public Func<ReleaseCoordinates, string, string> GetDownloadURL { get; set; }
        public HttpPakDownloader(Func<ReleaseCoordinates, string, string> getDownloadUrl) {
            GetDownloadURL = getDownloadUrl;
        }

        public EitherAsync<ModPakStreamAcquisitionFailure, SizedStream> ModPakStream(ReleaseCoordinates target, string pakFileName) {
            var url = GetDownloadURL(target, pakFileName);
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