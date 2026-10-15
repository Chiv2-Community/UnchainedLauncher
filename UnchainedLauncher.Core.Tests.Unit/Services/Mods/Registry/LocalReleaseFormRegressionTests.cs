using Moq;
using System.Text.Json;
using System.Xml.Linq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.ViewModels.Registry;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Mods.Registry {
    public class LocalReleaseFormRegressionTests {
        [Fact]
        public void SettingsExposesRegistryManagementCommand() {
            var settings = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "Settings.xaml"));
            var button = Assert.Single(settings.Descendants().Where(element => element.Name.LocalName == "Button"
                && (string?)element.Attribute("Command") == "{Binding OpenRegistryWindowCommand}"));
            Assert.Null(button.Attribute("IsEnabled"));
            Assert.Null(button.Attribute("Visibility"));
            Assert.NotNull(typeof(SettingsVM).GetProperty("OpenRegistryWindowCommand"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FormSubmissionWritesAndReadsRequiredManifest(bool editing) {
            var root = Path.Combine(Path.GetTempPath(), "LocalReleaseForm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                // The form test does not exercise the view's async-void list refresh.
                // Keep it from reading manifests after the fixture starts cleanup.
                var registryMock = new Mock<LocalModRegistry>(Path.Combine(root, "registry")) { CallBase = true };
                registryMock.Setup(source => source.GetAllMods()).ReturnsAsync(new GetAllModsResult([], []));
                var registry = registryMock.Object;
                var pak = Path.Combine(root, "source.pak");
                await File.WriteAllTextAsync(pak, "test pak");
                RegistryReleaseFormVM form;
                if (editing) {
                    var assets = new AssetCollections(
                        [new("marker.uasset", "hash", null, null, ["blueprint.uasset"])],
                        [new("blueprint.uasset", "hash", null, null, "ServerMod", "1", "", "", "", true, false, true, false, false, true, false, false)],
                        [new("TBL/Content/Maps/Local.umap", "hash", null, null, null, "Local", null, null, null, "FFA", null)],
                        [new("replacement.uasset", "hash", null, null)],
                        [new("arbitrary.uasset", "hash", null, null, "Other")]);
                    var original = new Release("v1.0.0", "hash", "source.pak", DateTime.UtcNow,
                        new("Org/Local", "Local", "original", null, null, [], [], []), assets, "notes");
                    await registry.AddRelease(original, pak);
                    form = new RegistryReleaseFormVM(original, pak) { Description = "edited" };
                    Assert.Same(assets, form.Manifest);
                }
                else {
                    // Exercise the production create template, not a test substitute.
                    var windows = new Mock<IRegistryWindowService>();
                    RegistryReleaseFormVM? captured = null;
                    windows.Setup(service => service.PromptAddRelease(registry, It.IsAny<RegistryReleaseFormVM>()))
                        .Returns((LocalModRegistry _, RegistryReleaseFormVM vm) => { captured = vm; return vm; });
                    new LocalModRegistryDetailsVM(registry, windows.Object).AddRelease();
                    form = Assert.IsType<RegistryReleaseFormVM>(captured);
                    form.PakFilePath = pak;
                }

                var (release, path) = form.ToRelease();
                Assert.Same(form.Manifest, release.Manifest);
                // Match the GUI runtime's strict nullable serialization, independently of test-host defaults.
                JsonSerializer.Serialize(release, new JsonSerializerOptions { RespectNullableAnnotations = true });
                await registry.AddRelease(release, path);
                var result = await registry.GetModRelease(ReleaseCoordinates.FromRelease(release)).ToEither();
                Assert.True(result.IsRight);
                var saved = Assert.Single(result.RightToSeq());
                Assert.Equal(JsonSerializer.Serialize(release.Manifest), JsonSerializer.Serialize(saved.Manifest));
                if (editing) Assert.Equal("edited", saved.Info.Description);
                Assert.True(File.Exists(Path.Combine(registry.RegistryPath, release.Info.Organization,
                    release.Info.RepoName, release.Tag, release.PakFileName)));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
