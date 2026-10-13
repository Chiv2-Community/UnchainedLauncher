using System.Text.Json;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Dlls;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Dlls {
    public sealed class DependencyTypesTests {
        private static Release Pak(string name, params Dependency[] plugins) => new("v1.0.0", "hash", name + ".pak",
            DateTime.UtcNow, new ModInfo("https://github.com/Test/" + name, name, "Description", null, null, [], [],
                PluginDependencies: plugins.ToList()), null!, null);

        [Fact]
        public void ModPluginWarningListsOneModAndDeduplicatesItsPluginRequirements() {
            var dependency = new Dependency("https://github.com/Test/Native", ">=1.0.0,<2.0.0");
            var error = Assert.Throws<InvalidOperationException>(() =>
                DependencyValidator.Validate([Pak("Arena", dependency, dependency), Pak("NoPlugins")], []));
            Assert.Equal("One mod requires a DLL plugin. Enable them in the Plugin Manager.\n\nArena:\n\t- Native >=1.0.0,<2.0.0",
                error.Message);
        }

        [Fact]
        public void ModPluginWarningGroupsAllModsIncludingPakDependencies() {
            var alpha = Pak("Alpha", new Dependency("https://github.com/Test/Native", "^1.0.0"));
            var beta = Pak("Beta", new Dependency("https://github.com/Test/ZPlugin", "2.0.0"),
                new Dependency("https://github.com/Test/APlugin", "~1.2.0"));
            alpha = alpha with { Info = alpha.Info with { ModDependencies = [new Dependency(beta.Info.RepoUrl, "1.0.0")] } };
            var error = Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([beta, alpha, Pak("NoPlugins")], []));
            Assert.Equal("Some mods require DLL plugins. Enable them in the Plugin Manager.\n\nAlpha:\n\t- Native ^1.0.0\n\nBeta:\n\t- APlugin ~1.2.0\n\t- ZPlugin 2.0.0",
                error.Message);
        }

        [Fact]
        public void ModPluginWarningUsesPluralPluginsForOneMod() {
            var error = Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([
                Pak("Arena", new Dependency("https://github.com/Test/Native", "1.0.0"),
                    new Dependency("https://github.com/Test/Other", "2.0.0"))], []));
            Assert.StartsWith("One mod requires DLL plugins.", error.Message);
        }

        [Fact]
        public void ModPluginWarningKeepsSharedPluginUnderEachMod() {
            var dependency = new Dependency("https://github.com/Test/Native", "1.0.0");
            var error = Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([Pak("Beta", dependency), Pak("Alpha", dependency)], []));
            Assert.Equal("Some mods require a DLL plugin. Enable them in the Plugin Manager.\n\nAlpha:\n\t- Native 1.0.0\n\nBeta:\n\t- Native 1.0.0",
                error.Message);
        }

        [Fact]
        public void ManuallyEnabledCompatiblePluginsPassAndDoNotAppearInWarning() {
            var native = new DllRelease("Test", "Native", "1.5.0", "https://example.com/Native.dll", new string('a', 64), 1);
            var satisfied = Pak("Satisfied", new Dependency("https://github.com/Test/Native", "^1.0.0"));
            DependencyValidator.Validate([satisfied], [native]);
            var missing = new Dependency("https://github.com/Test/Other", "2.0.0");
            var partial = Pak("Partial", satisfied.Info.EffectivePluginDependencies[0], missing);
            var error = Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([satisfied, partial], [native]));
            Assert.Equal("One mod requires a DLL plugin. Enable them in the Plugin Manager.\n\nPartial:\n\t- Other 2.0.0", error.Message);
            Assert.Throws<InvalidOperationException>(() => DependencyValidator.Validate([satisfied], [native with { Version = "2.0.0" }]));
            DependencyValidator.Validate([Pak("NoPlugins")], []);
        }

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