using FluentAssertions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods {

    internal class ModManagerFactsFixture {
        public IModManager ModManager;
        public ModEnabledHandler? ModEnabledHandler;
        public ModDisabledHandler? ModDisabledHandler;
        public int ModEnabledCallCount;
        public int ModDisabledCallCount;
        public Release? LastEnabledRelease;
        public string? LastEnabledPreviousVersion;
        public Release? LastDisabledRelease;

        public ModManagerFactsFixture() {
            ModManager = LocalModManagerFactory.ForRegistry(LocalModRegistryFactory.DefaultModRegistry);
            ModEnabledCallCount = 0;
            ModDisabledCallCount = 0;
            LastEnabledRelease = null;
            LastEnabledPreviousVersion = null;
            LastDisabledRelease = null;

            ModEnabledHandler = (release, previousVersion) => {
                ModEnabledCallCount++;
                LastEnabledRelease = release;
                LastEnabledPreviousVersion = previousVersion;
            };

            ModDisabledHandler = (release) => {
                ModDisabledCallCount++;
                LastDisabledRelease = release;
            };

            ModManager.ModEnabled += ModEnabledHandler;
            ModManager.ModDisabled += ModDisabledHandler;
        }
    }

    public class ModManagerTests {
        private ModManagerFactsFixture CreateTestFixture() => new ModManagerFactsFixture();

        [Fact]
        public async Task UpdateModsList_ShouldPopulateModsList() {
            var testFixture = CreateTestFixture();
            var result = await testFixture.ModManager.UpdateModsList();

            // Assert
            result.HasErrors.Should().BeFalse();
            testFixture.ModManager.Mods.Should().NotBeEmpty();
        }

        [Fact]
        public void EnabledModReleases_ShouldBeEmptyInitially() {
            var testFixture = CreateTestFixture();
            testFixture.ModManager.EnabledModReleases.Should().BeEmpty();
        }

        [Fact]
        public async Task EnableMod_WithValidMod_ShouldEnableLatestVersion() {
            var testFixture = CreateTestFixture();
            await testFixture.ModManager.UpdateModsList();
            var mod = testFixture.ModManager.Mods.First();
            var modId = ModIdentifier.FromMod(mod);

            var result = testFixture.ModManager.EnableMod(modId);

            result.Should().BeTrue();
            testFixture.ModEnabledCallCount.Should().Be(1);
            testFixture.LastEnabledRelease.Should().NotBeNull();
            testFixture.ModManager.EnabledModReleases.Should().NotBeEmpty();
        }

        [Fact]
        public void EnableMod_WithInvalidMod_ShouldReturnFalse() {
            var testFixture = CreateTestFixture();
            var invalidModId = new ModIdentifier("InvalidOrg", "InvalidMod");

            var result = testFixture.ModManager.EnableMod(invalidModId);

            result.Should().BeFalse();
            testFixture.ModEnabledCallCount.Should().Be(0);
            testFixture.ModManager.EnabledModReleases.Should().BeEmpty();
        }

        [Fact]
        public async Task DisableMod_WithEnabledMod_ShouldDisableMod() {
            var testFixture = CreateTestFixture();
            await testFixture.ModManager.UpdateModsList();
            var mod = testFixture.ModManager.Mods.First();
            var modId = ModIdentifier.FromMod(mod);
            testFixture.ModManager.EnableMod(modId);
            testFixture.ModEnabledCallCount = 0; // Reset counter after setup

            var result = testFixture.ModManager.DisableMod(modId);

            // Assert
            result.Should().BeTrue();
            testFixture.ModDisabledCallCount.Should().Be(1);
            testFixture.LastDisabledRelease.Should().NotBeNull();
            testFixture.ModManager.EnabledModReleases.Should().BeEmpty();
        }

        [Fact]
        public void DisableMod_WithNotEnabledMod_ShouldReturnFalse() {
            var testFixture = CreateTestFixture();
            var modId = new ModIdentifier("SomeOrg", "SomeMod");

            var result = testFixture.ModManager.DisableMod(modId);

            result.Should().BeFalse();
            testFixture.ModDisabledCallCount.Should().Be(0);
            testFixture.ModManager.EnabledModReleases.Should().BeEmpty();
        }

        [Fact]
        public async Task EnableMod_WhenAlreadyEnabled_ShouldReturnFalse() {
            var testFixture = CreateTestFixture();
            await testFixture.ModManager.UpdateModsList();
            var mod = testFixture.ModManager.Mods.First();
            var modId = ModIdentifier.FromMod(mod);
            testFixture.ModManager.EnableMod(modId);
            testFixture.ModEnabledCallCount = 0;

            var result = testFixture.ModManager.EnableMod(modId);

            result.Should().BeFalse();
            testFixture.ModEnabledCallCount.Should().Be(0);
        }

        [Fact]
        public async Task EnableModRelease_WithSpecificVersion_ShouldEnableCorrectVersion() {
            var testFixture = CreateTestFixture();
            await testFixture.ModManager.UpdateModsList();
            var mod = testFixture.ModManager.Mods.First();
            var release = mod.Releases.First();
            var coordinates = ReleaseCoordinates.FromRelease(release);

            var result = testFixture.ModManager.EnableModRelease(coordinates);

            result.Should().BeTrue();
            testFixture.ModEnabledCallCount.Should().Be(1);
            testFixture.LastEnabledRelease.Should().Be(release);
            testFixture.ModManager.EnabledModReleases.First().Should().Be(coordinates);
        }

        [Fact]
        public async Task EnableModRelease_WithDifferentVersion_ShouldReplaceExistingVersion() {
            var testFixture = CreateTestFixture();
            await testFixture.ModManager.UpdateModsList();
            var mod = testFixture.ModManager.Mods.First();
            var firstRelease = mod.Releases.First();
            var secondRelease = mod.Releases.Skip(1).First();

            testFixture.ModManager.EnableModRelease(ReleaseCoordinates.FromRelease(firstRelease));
            testFixture.ModEnabledCallCount = 0; // Reset counter after setup

            var result = testFixture.ModManager.EnableModRelease(ReleaseCoordinates.FromRelease(secondRelease));

            result.Should().BeTrue();
            testFixture.ModEnabledCallCount.Should().Be(1);
            testFixture.LastEnabledRelease.Should().Be(secondRelease);
            testFixture.LastEnabledPreviousVersion.Should().Be(firstRelease.Tag);
        }
    }
}