using System.Text.Json;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.GUI.ViewModels.Registry;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit {
    public sealed class RegistryReleaseFormVMTests {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void EditAndNewReleasePreservePluginAndPakRequirements(bool newRelease, bool modernPakDependencies) {
            var path = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? (OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath()), "form-test-" + Guid.NewGuid().ToString("N") + ".pak");
            File.WriteAllText(path, "pak fixture");
            try {
                var pak = new Dependency("https://github.com/Org/Pak", ">=1.0.0");
                var plugin = new Dependency("https://github.com/Org/Plugin", ">=1.0.0");
                var info = new ModInfo("Org/Mod", "Mod", "Original", null, null, [], [],
                    Dependencies: modernPakDependencies ? null : [pak],
                    ModDependencies: modernPakDependencies ? [pak] : null, PluginDependencies: [plugin]);
                var original = new Release("v1.0.0", "hash", "test.pak", new DateTime(2026, 1, 1), info,
                    new AssetCollections([], [], [], [], []), "notes");
                var input = newRelease ? original with { Tag = "v1.0.1", ReleaseDate = original.ReleaseDate.AddDays(1) } : original;
                var form = new RegistryReleaseFormVM(input, path) { Description = "Edited" };
                var (edited, pakPath) = form.ToRelease();
                Assert.Equal(path, pakPath);
                Assert.Equal(input.Tag, edited.Tag);
                Assert.Equal(input.ReleaseDate, edited.ReleaseDate);
                Assert.Equal("Edited", edited.Info.Description);
                Assert.Equal(new[] { pak }, edited.Info.EffectiveModDependencies);
                Assert.Equal(new[] { plugin }, edited.Info.EffectivePluginDependencies);
                Assert.Same(info.PluginDependencies, edited.Info.PluginDependencies);
                var persisted = JsonSerializer.Deserialize<Release>(JsonSerializer.Serialize(edited))!;
                Assert.Equal(new[] { pak }, persisted.Info.EffectiveModDependencies);
                Assert.Equal(new[] { plugin }, persisted.Info.EffectivePluginDependencies);
                Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([persisted], []));
                Assert.Equal("Original", original.Info.Description);
            }
            finally { File.Delete(path); }
        }
    }
}
