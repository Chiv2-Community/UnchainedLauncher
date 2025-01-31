using FluentAssertions;
using LanguageExt;
using System.Collections.Immutable;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry {
    public class AggregateModRegistryTests {
        private static readonly ModIdentifier TEST_MOD_IDENTIFIER =
            new ModIdentifier("Chiv2-Community", "Unchained-Mods");

        // These test registries have a couple of different mods, and then also some overlapping mods with both
        // differing and duplicate releases. This lets us test deduplication and merging
        private static AggregateModRegistry AggregateTestRegistry => new AggregateModRegistry(
            LocalModRegistryFactory.AlternateModRegistry,
            LocalModRegistryFactory.DefaultModRegistry
        );

        private static SortedSet<ModIdentifier> ExpectedModIdentifiers = new SortedSet<ModIdentifier>() {
            new ModIdentifier("Chiv2-Community", "Unchained-Mods"),
            new ModIdentifier("Chiv2-Community", "Test-Mod"),
        };

        [Fact]
        public async Task GetAllMods_WithDuplicateMods_ShouldDeduplicateMods() {
            var aggregateRegistry = AggregateTestRegistry;

            var result = await aggregateRegistry.GetAllMods();

            result.Should().NotBeNull();
            result.Mods.Should().NotBeEmpty();
            result.Errors.Should().BeEmpty();

            var modCount = result.Mods.Count();
            var modIdentifiers = result.Mods.Select(ModIdentifier.FromMod).ToImmutableSortedSet();

            var distinctModCount = modIdentifiers.Distinct().Count();

            modCount.Should().Be(distinctModCount);

            modIdentifiers.Should().ContainInOrder(ExpectedModIdentifiers);
            var unchainedMods = result.Mods
                .Find(TEST_MOD_IDENTIFIER.Matches).FirstOrDefault();

            Assert.NotNull(unchainedMods);
            unchainedMods!.Releases.Select(x => x.Tag)
                .Should()
                .ContainInOrder(new[] { "v0.0.3", "v0.0.2", "v0.0.1" });
        }

        [Fact]
        public async Task GetMod_WithDuplicateMods_ShouldReturnFirstMatchWithReleasesDeduplicatedAndMerged() {
            var aggregateRegistry = AggregateTestRegistry;

            var result = await aggregateRegistry.GetMod(TEST_MOD_IDENTIFIER).ToEither();

            result.IsRight.Should().BeTrue();
            var mod = result.RightToSeq().FirstOrDefault();
            mod.Should().NotBeNull();
            mod.LatestManifest.Organization.Should().Be("Chiv2-Community");
            mod.LatestManifest.Name.Should().Be("Unchained-Mods");
            mod.LatestManifest.Authors.Should().Contain("Nihi");
            mod.Releases.Should().HaveCount(3);
        }

        [Fact]
        public async Task GetMod_WithAllRegistriesFailing_ShouldReturnNotFound() {
            // Arrange
            var emptyDirPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyDirPath);

            var registry1 = LocalModRegistryFactory.Create(emptyDirPath);
            var registry2 = LocalModRegistryFactory.Create(emptyDirPath);

            var aggregateRegistry = new AggregateModRegistry(registry1, registry2);

            try {
                var result = await aggregateRegistry.GetMod(TEST_MOD_IDENTIFIER).ToEither();

                result.IsLeft.Should().BeTrue();
                result.LeftToSeq().FirstOrDefault().Should().BeOfType<RegistryMetadataException.NotFoundException>();
            }
            finally {
                Directory.Delete(emptyDirPath, true);
            }
        }

        [Fact]
        public void Registry_Name_ShouldIncludeAllRegistryNames() {
            var aggregateRegistry = AggregateTestRegistry;

            var name = aggregateRegistry.Name;

            name.Should().Contain(LocalModRegistryFactory.DEFAULT_MOD_MANAGER_PATH);
            name.Should().Contain(LocalModRegistryFactory.ALTERNATE_MOD_MANAGER_PATH);
        }

        [Fact]
        public async Task DownloadPak_ForLatestVersionOfEachMod_ShouldDownloadSuccessfully() {
            var aggregateRegistry = AggregateTestRegistry;
            var tempOutputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempOutputDir);

            try {
                // Get all mods and their latest releases
                var getAllModsResult = await aggregateRegistry.GetAllMods();
                getAllModsResult.Errors.Should().BeEmpty();
                getAllModsResult.Mods.Should().NotBeEmpty();

                // Test downloading each mod's latest release
                foreach (var mod in getAllModsResult.Mods) {
                    var latestRelease = mod.Releases.MaxBy(r => r.ReleaseDate);
                    latestRelease.Should().NotBeNull();

                    var coordinates = ReleaseCoordinates.FromRelease(latestRelease);
                    var outputPath = Path.Combine(tempOutputDir, $"{coordinates.Org}-{coordinates.ModuleName}-{coordinates.Version}.pak");

                    var result = await aggregateRegistry.DownloadPak(coordinates, outputPath).ToEither();

                    result.IsRight.Should().BeTrue($"Download failed for {coordinates.Org}/{coordinates.ModuleName} v{coordinates.Version}");
                    var fileWriter = result.RightToSeq().FirstOrDefault();
                    fileWriter.Should().NotBeNull();
                }
            }
            finally {
                // Cleanup
                if (Directory.Exists(tempOutputDir)) {
                    Directory.Delete(tempOutputDir, true);
                }
            }
        }

        [Fact]
        public async Task DownloadPak_WhenAllRegistriesFail_ShouldReturnFailure() {
            var emptyDirPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyDirPath);

            var registry1 = LocalModRegistryFactory.Create(emptyDirPath);
            var registry2 = LocalModRegistryFactory.Create(emptyDirPath);

            var aggregateRegistry = new AggregateModRegistry(registry1, registry2);
            var outputPath = Path.Combine(emptyDirPath, "test.pak");

            try {
                var coordinates = new ReleaseCoordinates(
                    TEST_MOD_IDENTIFIER.Org,
                    TEST_MOD_IDENTIFIER.ModuleName,
                    "v0.0.4"  // Use a version that does not exist
                );

                var result = await aggregateRegistry.DownloadPak(coordinates, outputPath).ToEither();

                result.IsLeft.Should().BeTrue();
                result.LeftToSeq().FirstOrDefault().Should().BeOfType<ModPakStreamAcquisitionFailure>();
                var failure = result.LeftToSeq().FirstOrDefault() as ModPakStreamAcquisitionFailure;
                failure.Should().NotBeNull();
                failure!.Target.Should().Be(coordinates);
            }
            finally {
                // Cleanup
                if (Directory.Exists(emptyDirPath)) {
                    Directory.Delete(emptyDirPath, true);
                }
            }
        }
    }
}