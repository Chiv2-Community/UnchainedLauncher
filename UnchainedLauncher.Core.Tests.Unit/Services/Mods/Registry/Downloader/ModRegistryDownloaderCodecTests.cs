using FluentAssertions;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry.Downloader {
    public class ModRegistryDownloaderCodecTests : CodecTestBase<IModRegistryDownloader> {
        public ModRegistryDownloaderCodecTests() : base(ModRegistryDownloaderCodec.Instance) { }

        [Fact]
        public void LocalFilePakDownloader_SerializeAndDeserialize_PreservesData() {
            var downloader = new LocalFilePakDownloader(@"C:\TestPath\Mods");
            VerifyCodecRoundtrip(downloader, result => {
                result.PakReleasesDir.Should().Be(downloader.PakReleasesDir);
            });
        }

        [Fact]
        public void HttpPakDownloader_SerializeAndDeserialize_PreservesData() {
            var downloader = new HttpPakDownloader("https://example.com/<Org>/<Repo>/download/<Version>/<PakFileName>");
            VerifyCodecRoundtrip(downloader, result => {
                result.UrlPattern.Should().Be(downloader.UrlPattern);
            });
        }
    }
}