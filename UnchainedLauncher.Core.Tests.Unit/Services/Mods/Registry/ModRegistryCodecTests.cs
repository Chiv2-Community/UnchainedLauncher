using FluentAssertions;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry {
    public class ModRegistryCodecTests : CodecTestBase<IModRegistry> {
        public ModRegistryCodecTests() : base(ModRegistryCodec.Instance) {
        }

        [Fact]
        public void LocalModRegistry_SerializeAndDeserialize_PreservesData() {
            var downloader = new LocalFilePakDownloader(@"C:\TestPath\Mods");
            var originalRegistry = new LocalModRegistry(@"C:\TestPath\Mods", downloader);

            VerifyCodecRoundtrip(originalRegistry, registry => {
                registry.RegistryPath.Should().Be(originalRegistry.RegistryPath);
                registry.ModRegistryDownloader.Should().BeOfType<LocalFilePakDownloader>();
            });
        }

        [Fact]
        public void GithubModRegistry_SerializeAndDeserialize_PreservesData() {
            var downloader = new HttpPakDownloader("https://example.com/<Org>/<Repo>/download/<Version>/<PakFileName>");
            var originalRegistry = new GithubModRegistry("TestOrg", "TestRepo", downloader);

            VerifyCodecRoundtrip(originalRegistry, registry => {
                registry.Organization.Should().Be(originalRegistry.Organization);
                registry.RepoName.Should().Be(originalRegistry.RepoName);
                registry.ModRegistryDownloader.Should().BeOfType<HttpPakDownloader>();
            });

        }

        [Fact]
        public void AggregateModRegistry_SerializeAndDeserialize_PreservesData() {
            var localRegistry = new LocalModRegistry(@"C:\TestPath\Mods",
                new LocalFilePakDownloader(@"C:\TestPath\Mods"));
            var githubRegistry = new GithubModRegistry("TestOrg", "TestRepo",
                new HttpPakDownloader("https://example.com/<Org>/<Repo>/download/<Version>/<PakFileName>"));
            var originalRegistry = new AggregateModRegistry(new IModRegistry[] { localRegistry, githubRegistry });

            VerifyCodecRoundtrip(originalRegistry, registry => {
                registry.ModRegistries.Should().HaveCount(2);
                registry.ModRegistries.ElementAt(0).Should().BeOfType<LocalModRegistry>();
                registry.ModRegistries.ElementAt(1).Should().BeOfType<GithubModRegistry>();
            });
        }
    }
}