using FluentAssertions;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Tests.Unit.Utilities;
using Xunit.Abstractions;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry {
    public class ModRegistryCodecTests : CodecTestBase<IModRegistry> {
        public ModRegistryCodecTests(ITestOutputHelper testOutputHelper) : base(ModRegistryCodec.Instance, testOutputHelper) {
        }

        [Fact]
        public void LocalModRegistry_SerializeAndDeserialize_PreservesData() {
            var originalRegistry = new LocalModRegistry(@"C:\TestPath\Mods");

            VerifyCodecRoundtrip(originalRegistry, registry => {
                registry.RegistryPath.Should().Be(originalRegistry.RegistryPath);
            });
        }

        [Fact]
        public void GithubModRegistry_SerializeAndDeserialize_PreservesData() {
            var originalRegistry = new GithubModRegistry("TestOrg", "TestRepo");

            VerifyCodecRoundtrip(originalRegistry, registry => {
                registry.Organization.Should().Be(originalRegistry.Organization);
                registry.RepoName.Should().Be(originalRegistry.RepoName);
            });

        }

        [Fact]
        public void AggregateModRegistry_SerializeAndDeserialize_PreservesData() {
            var localRegistry = new LocalModRegistry(@"C:\TestPath\Mods");
            var githubRegistry = new GithubModRegistry("TestOrg", "TestRepo");
            var originalRegistry = new AggregateModRegistry(new IModRegistry[] { localRegistry, githubRegistry });

            VerifyCodecRoundtrip(originalRegistry, registry => {
                registry.ModRegistries.Should().HaveCount(2);
                registry.ModRegistries.ElementAt(0).Should().BeOfType<LocalModRegistry>();
                registry.ModRegistries.ElementAt(1).Should().BeOfType<GithubModRegistry>();
            });
        }
    }
}