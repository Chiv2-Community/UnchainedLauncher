using System.Text.Json;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Dlls;
using Xunit;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Dlls {
    public sealed class DependencyTypesTests {
        [Fact]
        public void LegacyModDependenciesAreReadAsModDependencies() {
            var json = """
                {"repo_url":"https://github.com/Test/Pak","name":"Pak","description":"Description",
                 "authors":["Author"],"image_urls":[],
                 "dependencies":[{"repo_url":"https://github.com/Test/OldPak","version":"1.0.0"}],
                 "mod_dependencies":[{"repo_url":"https://github.com/Test/NewPak","version":"2.0.0"}],
                 "plugin_dependencies":[{"repo_url":"https://github.com/Test/Native","version":"^1.0.0"}]}
                """;
            var info = JsonSerializer.Deserialize<ModInfo>(json)!;
            Assert.Equal(2, info.EffectiveModDependencies.Count);
            Assert.Equal("https://github.com/Test/Native", Assert.Single(info.EffectivePluginDependencies).RepoUrl);
            var restored = JsonSerializer.Deserialize<ModInfo>(JsonSerializer.Serialize(info))!;
            Assert.Equal(info.EffectiveModDependencies, restored.EffectiveModDependencies);
        }

        [Fact]
        public void ModAndPluginCrossDependenciesRequireCorrectEnabledTypesAndVersions() {
            var nativeDependency = new Dependency("https://github.com/Test/Native", ">=1.0.0,<2.0.0");
            var pakInfo = new ModInfo("https://github.com/Test/Pak", "Pak", "Description", null, null, [], ["Author"],
                ModDependencies: [], PluginDependencies: [nativeDependency]);
            var pak = new Release("v1.0.0", "hash", "Pak.pak", DateTime.UtcNow, pakInfo, null!, null);
            var nativeInfo = new PluginInfo("Native", "Description", ["Author"], [],
                ModDependencies: [new Dependency("https://github.com/Test/Pak", "1.0.0")]);
            var native = new DllRelease("Test", "Native", "1.0.0", "https://example.com/Native.dll", new string('a', 64), 1, nativeInfo);
            DependencyValidator.Validate([pak], [native]);
            Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([pak], []));
            Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([], [native]));
            Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([pak], [native with { Version = "2.0.0" }]));
            Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([pak with { Tag = "v2.0.0" }], [native]));

            var wrongType = native with { Info = nativeInfo with { ModDependencies = [nativeDependency] } };
            Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([pak], [wrongType]));
        }
    }
}
