using System.Text.Json;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Dlls {
    public sealed class DependencyTypesTests {
        private static Release Pak(string name, params Dependency[] plugins) => new("v1.0.0", "hash", name + ".pak",
            DateTime.UtcNow, new ModInfo("https://github.com/Test/" + name, name, "Description", null, null, [], [],
                PluginDependencies: plugins.ToList()), null!, null);

        [Fact]
        public async Task ModPluginWarningListsOneModAndDeduplicatesItsPluginRequirements() {
            var dependency = new Dependency("https://github.com/Test/Native", ">=1.0.0,<2.0.0");
            var dialogue = new RecordingDialogue();
            var manager = new WarningModManager([Pak("Arena", dependency, dependency), Pak("NoPlugins")]);
            var result = await new Chivalry2ModsInstaller(manager, dialogue).PrepareLaunch(null!);
            Assert.True(result.IsNone);
            Assert.Equal("One mod requires a DLL plugin. Enable them in the Plugin Manager.\n\nArena:\n\t- Native >=1.0.0,<2.0.0",
                Assert.Single(dialogue.Messages));
        }

        [Fact]
        public async Task ModPluginWarningGroupsAllModsIncludingPakDependencies() {
            var alpha = Pak("Alpha", new Dependency("https://github.com/Test/Native", "^1.0.0"));
            var beta = Pak("Beta", new Dependency("https://github.com/Test/ZPlugin", "2.0.0"),
                new Dependency("https://github.com/Test/APlugin", "~1.2.0"));
            alpha = alpha with { Info = alpha.Info with { ModDependencies = [new Dependency(beta.Info.RepoUrl, "1.0.0")] } };
            var dialogue = new RecordingDialogue();
            var manager = new WarningModManager([beta, alpha, Pak("NoPlugins")], [alpha]);
            var result = await new Chivalry2ModsInstaller(manager, dialogue).PrepareLaunch(null!);
            Assert.True(result.IsNone);
            Assert.Equal("Some mods require DLL plugins. Enable them in the Plugin Manager.\n\nAlpha:\n\t- Native ^1.0.0\n\nBeta:\n\t- APlugin ~1.2.0\n\t- ZPlugin 2.0.0",
                Assert.Single(dialogue.Messages));
        }

        [Fact]
        public async Task ModPluginWarningUsesPluralPluginsForOneMod() {
            var dialogue = new RecordingDialogue();
            var manager = new WarningModManager([Pak("Arena", new Dependency("https://github.com/Test/Native", "1.0.0"),
                new Dependency("https://github.com/Test/Other", "2.0.0"))]);
            await new Chivalry2ModsInstaller(manager, dialogue).PrepareLaunch(null!);
            Assert.StartsWith("One mod requires DLL plugins.", Assert.Single(dialogue.Messages));
        }

        [Fact]
        public async Task ModPluginWarningKeepsSharedPluginUnderEachMod() {
            var dependency = new Dependency("https://github.com/Test/Native", "1.0.0");
            var dialogue = new RecordingDialogue();
            var manager = new WarningModManager([Pak("Beta", dependency), Pak("Alpha", dependency)]);
            await new Chivalry2ModsInstaller(manager, dialogue).PrepareLaunch(null!);
            Assert.Equal("Some mods require a DLL plugin. Enable them in the Plugin Manager.\n\nAlpha:\n\t- Native 1.0.0\n\nBeta:\n\t- Native 1.0.0",
                Assert.Single(dialogue.Messages));
        }

        private sealed class WarningModManager(Release[] releases, Release[]? enabled = null) : IModManager {
            public event EventHandler MetadataRefreshed { add { } remove { } }
            public event ModEnabledHandler ModEnabled { add { } remove { } }
            public event ModDisabledHandler ModDisabled { add { } remove { } }
            public IModRegistry ModRegistry => throw new InvalidOperationException("Registry access is not expected.");
            public IPakDir PakDir => throw new InvalidOperationException("Installation must not start before the warning.");
            public IEnumerable<ReleaseCoordinates> EnabledModReleaseCoordinates => (enabled ?? releases).Select(ReleaseCoordinates.FromRelease);
            public IEnumerable<Mod> Mods => releases.Select(release => new Mod(release.Info, [release]));
            public bool DisableModRelease(ReleaseCoordinates release) => throw new NotSupportedException();
            public bool EnableModRelease(ReleaseCoordinates coordinates) => throw new NotSupportedException();
            public bool EnableMod(ModIdentifier identifier) => throw new NotSupportedException();
            public bool DisableMod(ModIdentifier identifier) => throw new NotSupportedException();
            public Task<GetAllModsResult> UpdateModsList() => throw new NotSupportedException();
        }

        private sealed class RecordingDialogue : IUserDialogueSpawner {
            public List<string> Messages { get; } = [];
            public void DisplayMessage(string message) => Messages.Add(message);
            public UserDialogueChoice DisplayYesNoMessage(string message, string caption) => throw new NotSupportedException();
            public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
                string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) => throw new NotSupportedException();
            public Action DisplayProgress(MemoryProgress progress) => throw new NotSupportedException();
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
