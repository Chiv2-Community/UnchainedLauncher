using FluentAssertions;
using LanguageExt;
using System.IO;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using Xunit;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry
{
    public class LocalModRegistryTests
    {
        private const string DEFAULT_MOD_MANAGER_PATH = "TestData/ModRegistries/DefaultModRegistry";
        private static readonly ModIdentifier TEST_MOD_IDENTIFIER = 
            new ModIdentifier("Chiv2-Community", "Unchained-Mods");
        
        private static LocalModRegistry CreateLocalModRegistry(string path) =>
            new LocalModRegistry(
                path,
                new LocalFilePakDownloader(path)
            );

        [Fact]
        public async Task GetAllMods_ShouldReturnAllValidMods()
        {
            var registry = CreateLocalModRegistry(DEFAULT_MOD_MANAGER_PATH);

            var result = await registry.GetAllMods();

            result.Should().NotBeNull();
            result.Mods.Should().NotBeEmpty();
            result.Errors.Should().BeEmpty();
            
            var firstMod = result.Mods.First();
            firstMod.LatestManifest.Name.Should().Be("Unchained-Mods");
            firstMod.LatestManifest.Authors.Should().Contain("Nihi");
            firstMod.Releases.Should().HaveCount(2); // v0.0.1 and v0.0.2
        }

        [Fact]
        public async Task GetAllMods_WithEmptyDirectory_ShouldReturnEmptyList()
        {
            var emptyDirPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyDirPath);
            var registry = CreateLocalModRegistry(emptyDirPath);

            try
            {
                var result = await registry.GetAllMods();

                result.Should().NotBeNull();
                result.Mods.Should().BeEmpty();
                result.Errors.Should().BeEmpty();
            }
            finally
            {
                Directory.Delete(emptyDirPath, true);
            }
        }

        [Fact]
        public async Task GetModMetadataString_WithValidPath_ShouldReturnContent()
        {
            var registry = CreateLocalModRegistry(DEFAULT_MOD_MANAGER_PATH);

            var result = await registry.GetMod(TEST_MOD_IDENTIFIER).ToEither();

            result.IsRight.Should().BeTrue();
            var content = result.RightToSeq().FirstOrDefault();
            content.Should().NotBeNull();
            content.LatestManifest.Organization.Should().Contain("Chiv2-Community");
            content.LatestManifest.Name.Should().Contain("Unchained-Mods");
            content.LatestManifest.Authors.Should().Contain("Nihi");
            content.Releases.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetModMetadataString_WithInvalidPath_ShouldReturnLeft()
        {
            var registry = CreateLocalModRegistry(DEFAULT_MOD_MANAGER_PATH);
            var nonExistentPath = new ModIdentifier("Bogus", "Mod");

            var result = await registry.GetMod(nonExistentPath).ToEither();

            result.IsLeft.Should().BeTrue();
            result.LeftToSeq().FirstOrDefault().Should().BeOfType<RegistryMetadataException.NotFoundException>();
        }

        [Fact]
        public void Registry_Name_ShouldIncludePath()
        {
            var registry = CreateLocalModRegistry(DEFAULT_MOD_MANAGER_PATH);
            registry.Name.Should().Contain(DEFAULT_MOD_MANAGER_PATH);
        }
    }
}