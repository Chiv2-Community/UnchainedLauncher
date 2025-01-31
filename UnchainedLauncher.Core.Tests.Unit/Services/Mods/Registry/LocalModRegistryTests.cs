using FluentAssertions;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry {
    public class LocalModRegistryTests {
        private static readonly ModIdentifier TestModIdentifier =
            new ModIdentifier("Chiv2-Community", "Unchained-Mods");

        [Fact]
        public async Task GetAllMods_ShouldReturnAllValidMods() {
            var registry = LocalModRegistryFactory.DefaultModRegistry;

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
        public async Task GetAllMods_WithEmptyDirectory_ShouldReturnEmptyList() {
            var emptyDirPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyDirPath);
            var registry = LocalModRegistryFactory.Create(emptyDirPath);

            try {
                var result = await registry.GetAllMods();

                result.Should().NotBeNull();
                result.Mods.Should().BeEmpty();
                result.Errors.Should().BeEmpty();
            }
            finally {
                Directory.Delete(emptyDirPath, true);
            }
        }

        [Fact]
        public async Task GetModMetadataString_WithValidPath_ShouldReturnContent() {
            var registry = LocalModRegistryFactory.DefaultModRegistry;

            var result = await registry.GetMod(TestModIdentifier).ToEither();

            result.IsRight.Should().BeTrue();
            var content = result.RightToSeq().FirstOrDefault();
            content.Should().NotBeNull();
            content.LatestManifest.Organization.Should().Contain("Chiv2-Community");
            content.LatestManifest.Name.Should().Contain("Unchained-Mods");
            content.LatestManifest.Authors.Should().Contain("Nihi");
            content.Releases.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetModMetadataString_WithInvalidPath_ShouldReturnLeft() {
            var registry = LocalModRegistryFactory.DefaultModRegistry;
            var nonExistentPath = new ModIdentifier("Bogus", "Mod");

            var result = await registry.GetMod(nonExistentPath).ToEither();

            result.IsLeft.Should().BeTrue();
            result.LeftToSeq().FirstOrDefault().Should().BeOfType<RegistryMetadataException.NotFoundException>();
        }

        [Fact]
        public void Registry_Name_ShouldIncludePath() {
            var registry = LocalModRegistryFactory.DefaultModRegistry;
            registry.Name.Should().Contain(LocalModRegistryFactory.DefaultModManagerPath);
        }

        [Fact]
        public async Task DownloadPak_ForExistingMod_ShouldDownloadSuccessfully() {
            var aggregateRegistry = LocalModRegistryFactory.DefaultModRegistry;
            var tempOutputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempOutputDir);

            try {
                var coordinates = new ReleaseCoordinates(
                    TestModIdentifier.Org,
                    TestModIdentifier.ModuleName,
                    "v0.0.2"
                );
                var outputPath = Path.Combine(tempOutputDir, $"{coordinates.Org}-{coordinates.ModuleName}-{coordinates.Version}.pak");

                var result = await aggregateRegistry.DownloadPak(coordinates, outputPath).ToEither();

                result.IsRight.Should().BeTrue($"Download failed for Unchained-Mods v0.0.2");
                var fileWriter = result.RightToSeq().FirstOrDefault();
                fileWriter.Should().NotBeNull();
            }
            finally {
                // Cleanup
                if (Directory.Exists(tempOutputDir)) {
                    Directory.Delete(tempOutputDir, true);
                }
            }
        }

        [Fact]
        public async Task DownloadPak_WithNonexistentVersion_ShouldReturnFailure() {
            var registry = LocalModRegistryFactory.DefaultModRegistry;
            var tempOutputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempOutputDir);

            try {
                var coordinates = new ReleaseCoordinates(
                    TestModIdentifier.Org,
                    TestModIdentifier.ModuleName,
                    "v9.9.9"  // A version that doesn't exist
                );
                var outputPath = Path.Combine(tempOutputDir, $"{coordinates.Org}-{coordinates.ModuleName}-{coordinates.Version}.pak");

                var result = await registry.DownloadPak(coordinates, outputPath).ToEither();

                result.IsLeft.Should().BeTrue();
                result.LeftToSeq().FirstOrDefault().Should().BeOfType<ModPakStreamAcquisitionFailure>();
                var failure = result.LeftToSeq().FirstOrDefault() as ModPakStreamAcquisitionFailure;
                failure.Should().NotBeNull();
                failure!.Target.Should().Be(coordinates);
            }
            finally {
                // Cleanup
                if (Directory.Exists(tempOutputDir)) {
                    Directory.Delete(tempOutputDir, true);
                }
            }
        }
    }
}