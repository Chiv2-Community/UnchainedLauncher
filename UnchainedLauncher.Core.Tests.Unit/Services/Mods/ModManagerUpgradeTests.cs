using Moq;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using ManagedPakDir = UnchainedLauncher.Core.Services.Mods.PakDir;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods {
    public class ModManagerUpgradeTests : IDisposable {
        private readonly string _pakDir = Path.Combine(Path.GetTempPath(), "upgrade-paks-" + Guid.NewGuid());

        public ModManagerUpgradeTests() {
            Directory.CreateDirectory(_pakDir);
            File.WriteAllBytes(Path.Combine(_pakDir, "Unchained-Mods.pak"), []);
        }

        private JsonNode ReadUpgradeFixture() {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ModManagerV3.json")))!;
            json["PakDir"]!["DirPath"] = _pakDir;
            return json;
        }

        public void Dispose() => Directory.Delete(_pakDir, recursive: true);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task V3CacheUpgradePreservesInstalledStateAndStartsConsumers(bool hasCustomMap) {
            // V3 serializer output from base 8b24e925; enabled/pak state added for upgrade coverage.
            var json = ReadUpgradeFixture();
            if (hasCustomMap)
                json["CachedMods"]![0]!["releases"]![0]!["manifest"]!["maps"]!.AsArray().Add("MyCustomMap");
            var coordinates = new ReleaseCoordinates("Chiv2-Community", "Unchained-Mods", "v0.0.2");
            var info = new ModInfo("https://github.com/Chiv2-Community/Unchained-Mods", "Unchained-Mods", "", null, null, [], [], []);
            var release = new Release("v0.0.2", "hash", "Unchained-Mods.pak", DateTime.UtcNow, info, new AssetCollections([], [], [], [], []), null);
            var mod = new Mod(info, [release]);
            var registry = new Mock<IModRegistry>();
            registry.Setup(r => r.GetAllMods()).ReturnsAsync(new GetAllModsResult([], [mod]));
            var codec = new ModManagerCodec(registry.Object);

            var decoded = codec.Deserialize(json.ToJsonString());
            Assert.True(decoded.Success, decoded.Exception?.ToString());
            var manager = decoded.Result!;
            Assert.Empty(manager.Mods);
            Assert.True(manager.NeedsMetadataRefresh);
            Assert.Equal([coordinates], manager.EnabledModReleaseCoordinates);
            Assert.Equal([new ManagedPak(coordinates, "Unchained-Mods.pak", 42)], ((ManagedPakDir)manager.PakDir).ManagedPaks);

            // Both offline startup and the awaited startup refresh must be safe.
            var modList = new ModListVM(manager, Mock.Of<IUserDialogueSpawner>());
            _ = new AvailableModsAndMapsService(manager, new ModScanTabVM());
            await modList.RefreshModList();
            Assert.False(manager.NeedsMetadataRefresh);
            Assert.Equal([release], manager.GetEnabledAndDependencyReleases());
            _ = new AvailableModsAndMapsService(manager, new ModScanTabVM());
            Assert.Single(modList.DisplayMods);

            var saved = codec.Deserialize(codec.Serialize(manager));
            Assert.True(saved.Success);
            Assert.Single(saved.Result!.Mods);
            Assert.Equal([coordinates], saved.Result.EnabledModReleaseCoordinates);
            Assert.Equal(((ManagedPakDir)manager.PakDir).ManagedPaks, ((ManagedPakDir)saved.Result.PakDir).ManagedPaks);
        }

        [Fact]
        public void MixedCacheRetainsV4MetadataButStillRequestsRefresh() {
            var json = ReadUpgradeFixture();
            var info = new ModInfo("https://github.com/Chiv2-Community/Unchained-Mods", "Unchained-Mods", "", null, null, [], [], []);
            var release = new Release("v0.0.2", "hash", "Unchained-Mods.pak", DateTime.UtcNow, info, new AssetCollections([], [], [], [], []), null);
            var valid = JsonNode.Parse(JsonHelpers.Serialize(new Mod(info, [release])));
            json["CachedMods"]!.AsArray().Add(valid);
            var decoded = new ModManagerCodec(Mock.Of<IModRegistry>()).Deserialize(json.ToJsonString());
            Assert.True(decoded.Success, decoded.Exception?.ToString());
            Assert.Single(decoded.Result!.Mods);
            Assert.True(decoded.Result.NeedsMetadataRefresh);
            Assert.Equal("Unchained-Mods", ModIdentifier.FromMod(decoded.Result.Mods.Single()).ModuleName);
        }

        [Fact]
        public void GithubRegistryUsesStableMain() {
            var registry = new GithubModRegistry("Chiv2-Community", "C2ModRegistry");
            Assert.Equal("https://raw.githubusercontent.com/Chiv2-Community/C2ModRegistry/refs/heads/main/package_db", registry.PackageDBBaseUrl);
        }
    }
}