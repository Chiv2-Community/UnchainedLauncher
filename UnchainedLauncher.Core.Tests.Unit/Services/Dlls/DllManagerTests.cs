using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnchainedLauncher.Core.Services.Dlls;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Dlls {
    public sealed class DllManagerTests : IDisposable {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "dll-tests-" + Guid.NewGuid().ToString("N"));
        private readonly byte[] _binary = Encoding.UTF8.GetBytes("registered DLL bytes");
        private readonly FixtureHandler _handler = new();
        private readonly HttpClient _http;
        private string StatePath => Path.Combine(_dir, "dll-manager.json");
        private string PluginPath => Path.Combine(_dir, "Plugins");

        public DllManagerTests() { Directory.CreateDirectory(_dir); _http = new HttpClient(_handler); }

        private DllRelease Release(string name = "UnchainedPlugin", string version = "1.0.0") => new(
            "Chiv2-Community", name, version, "https://example.com/" + name + ".dll",
            Convert.ToHexString(SHA256.HashData(_binary)).ToLowerInvariant(), _binary.Length);

        private DllManager Manager() => new(PluginPath, StatePath, _http);

        [Fact]
        public async Task RemovedReleaseCanBeDisabledButNeverReenabled() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            Index();
            await manager.RefreshAsync();
            Assert.Throws<InvalidOperationException>(() => manager.SetEnabled(Release(), true));
            manager.SetEnabled(Release(), false);
            Assert.Empty(manager.EnabledSelections);
            Assert.Empty(Manager().EnabledSelections);
            Assert.Empty(manager.OpenForInjection());
        }

        [Fact]
        public async Task DisableDoesNotRequireCurrentSourcesAndPersistsWithoutReapproving() {
            Index(Release());
            IReadOnlyList<string> sources = [DllManager.DatabaseUrl];
            var manager = new DllManager(PluginPath, StatePath, _http, registryUrls: () => sources);
            await manager.RefreshAsync();
            sources = [];
            Assert.Throws<InvalidOperationException>(() => manager.SetEnabled(Release(), true));
            manager.Disable(DllManager.DefaultPluginId.ToLowerInvariant());
            Assert.Empty(manager.EnabledSelections);
            Assert.Equal("{}", File.ReadAllText(StatePath));
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection());
            var restarted = new DllManager(PluginPath, StatePath, _http, registryUrls: () => sources);
            Assert.Empty(restarted.EnabledSelections);
            await restarted.RefreshAsync();
            Assert.Empty(restarted.OpenForInjection());
        }

        [Fact]
        public async Task AlternateRegistriesMergeAndPersistApprovals() {
            const string alternate = "https://example.com/registry/dll_db";
            Index(Release());
            var other = Release("OtherPlugin");
            var info = new PluginInfo("Other", "Description", ["Author"], []);
            var record = new PluginRelease("v1.0.0", other.Sha256, "OtherPlugin.dll", other.DownloadUrl,
                other.Size, DateTimeOffset.Parse("2023-01-01T00:00:00Z"), info, "# Plugin notes");
            _handler.Responses[alternate + "/index.json"] = null;
            _handler.Responses[alternate + "/plugin_list_index.txt"] = "Chiv2-Community/OtherPlugin\n";
            _handler.Responses[alternate + "/packages/Chiv2-Community/OtherPlugin.json"] =
                JsonSerializer.Serialize(new PluginPackage(info, [record]));
            var manager = new DllManager(PluginPath, StatePath, _http, registryUrls: () => [DllManager.DatabaseUrl, alternate]);
            await manager.RefreshAsync();
            Assert.Equal(2, manager.Releases.Count);
            var registered = manager.Releases.Single(release => release.Name == "OtherPlugin");
            Assert.Equal("# Plugin notes", registered.ReleaseNotesMarkdown);
            manager.SetEnabled(registered, true);
            await manager.PrepareAsync();
            var restarted = new DllManager(PluginPath, StatePath, _http, registryUrls: () => [alternate, DllManager.DatabaseUrl]);
            Assert.Equal(2, restarted.Releases.Count);
            Assert.True(restarted.IsEnabled(registered));
        }

        [Fact]
        public async Task RegistryChangesInvalidateCachedAndLiveApprovals() {
            Index(Release());
            IReadOnlyList<string> sources = [DllManager.DatabaseUrl];
            var manager = new DllManager(PluginPath, StatePath, _http, registryUrls: () => sources);
            await manager.RefreshAsync();
            await manager.PrepareAsync();
            sources = [];
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection());
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
            var restarted = new DllManager(PluginPath, StatePath, _http, registryUrls: () => sources);
            Assert.Empty(restarted.Releases);
            await manager.RefreshAsync();
            Assert.Empty(manager.Releases);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
        }

        [Fact]
        public async Task ConflictingAlternateRegistryPreservesPreviousApprovals() {
            const string alternate = "https://example.com/registry/dll_db";
            Index(Release());
            IReadOnlyList<string> sources = [DllManager.DatabaseUrl];
            var manager = new DllManager(PluginPath, StatePath, _http, registryUrls: () => sources);
            await manager.RefreshAsync();
            var cached = File.ReadAllText(StatePath + ".index.json");
            _handler.Responses[alternate + "/index.json"] = JsonSerializer.Serialize(new[] { Release() with { Sha256 = new string('a', 64) } });
            _handler.Responses[alternate + "/plugin_list_index.txt"] = null;
            sources = [DllManager.DatabaseUrl, alternate];
            await Assert.ThrowsAsync<InvalidDataException>(() => manager.RefreshAsync());
            Assert.Equal(cached, File.ReadAllText(StatePath + ".index.json"));
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection());
        }

        [Fact]
        public async Task IdenticalRecordsAndModOnlyRegistriesAreSupported() {
            const string alternate = "https://example.com/registry/dll_db";
            const string modOnly = "https://example.com/mod-only/dll_db";
            Index(Release());
            _handler.Responses[alternate + "/index.json"] = _handler.Index;
            _handler.Responses[alternate + "/plugin_list_index.txt"] = null;
            _handler.Responses[modOnly + "/index.json"] = null;
            _handler.Responses[modOnly + "/plugin_list_index.txt"] = null;
            var manager = new DllManager(PluginPath, StatePath, _http, registryUrls: () => [DllManager.DatabaseUrl, alternate, modOnly]);
            await manager.RefreshAsync();
            Assert.Single(manager.Releases);
        }

        [Fact]
        public void PluginImagesRoundTripAndAreOptional() {
            var info = new PluginInfo("Plugin", "Description", ["Author"], [],
                "https://example.com/icon.png", ["https://example.com/screenshot.png"]);
            var release = Release() with { Info = info };
            var parsed = Assert.Single(DllManager.ParseIndex(JsonSerializer.Serialize(new[] { release })));
            Assert.Equal(info.IconUrl, parsed.Info!.IconUrl);
            Assert.Equal(info.ImageUrls, parsed.Info.ImageUrls);
            var optional = new PluginInfo("Plugin", "Description", ["Author"], []);
            optional.Validate();
            Assert.Null(optional.IconUrl);
            Assert.Null(optional.ImageUrls);
        }

        [Theory]
        [InlineData("http://example.com/image.png")]
        [InlineData("file:///image.png")]
        [InlineData("https://user:password@example.com/image.png")]
        public void InvalidPluginImagesAreRejected(string url) {
            Assert.Throws<InvalidDataException>(() => new PluginInfo("Plugin", "Description", ["Author"], [], url).Validate());
            Assert.Throws<InvalidDataException>(() => new PluginInfo("Plugin", "Description", ["Author"], [], null, [url]).Validate());
        }

        [Fact]
        public async Task PluginDependenciesMustBeExplicitlyEnabledAndLoadFirst() {
            var info = new PluginInfo("Display Name", "Description", ["Author"],
                [new UnchainedLauncher.Core.JsonModels.ModMetadata.Dependency("https://github.com/Chiv2-Community/OtherPlugin", ">=1.0.0,<2.0.0")]);
            var plugin = Release() with { Info = info };
            var other = Release("OtherPlugin");
            var incompatible = Release("OtherPlugin", "2.0.0");
            Index(plugin, other, incompatible);
            var manager = Manager();
            await manager.RefreshAsync();
            Assert.Equal("Display Name", manager.Releases[0].Info!.Name);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection());
            manager.SetEnabled(incompatible, true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
            manager.SetEnabled(other, true);
            await manager.PrepareAsync();
            var files = manager.OpenForInjection();
            try {
                Assert.Equal(new[] { "OtherPlugin.dll", "UnchainedPlugin.dll" }, files.Select(file => Path.GetFileName(file.Path)));
            }
            finally { foreach (var file in files) file.Dispose(); }
        }

        [Fact]
        public async Task PluginModDependenciesAreCheckedAgainBeforeInjection() {
            var plugin = Release() with {
                Info = new PluginInfo("Plugin", "Description", ["Author"], [],
                ModDependencies: [new UnchainedLauncher.Core.JsonModels.ModMetadata.Dependency("https://github.com/Test/Pak", "1.0.0")])
            };
            Index(plugin);
            var enabledMods = new List<UnchainedLauncher.Core.JsonModels.ModMetadata.Release>();
            var manager = new DllManager(PluginPath, StatePath, _http, () => enabledMods);
            await manager.RefreshAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
            var info = new UnchainedLauncher.Core.JsonModels.ModMetadata.ModInfo("https://github.com/Test/Pak", "Pak", "Description", null, null, [], ["Author"],
                ModDependencies: [], PluginDependencies: []);
            enabledMods.Add(new UnchainedLauncher.Core.JsonModels.ModMetadata.Release("v1.0.0", "hash", "Pak.pak", DateTime.UtcNow, info, null!, null));
            await manager.PrepareAsync();
            var selected = manager.OpenForInjection();
            try { Assert.Single(selected); }
            finally { foreach (var file in selected) file.Dispose(); }
            enabledMods.Clear();
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection());
        }

        [Fact]
        public async Task CircularDependenciesFailBeforeDownloadsOrInjection() {
            var first = Release() with {
                Info = new PluginInfo("First", "Description", ["Author"],
                [new UnchainedLauncher.Core.JsonModels.ModMetadata.Dependency("https://github.com/Chiv2-Community/OtherPlugin", "1.0.0")])
            };
            var second = Release("OtherPlugin") with {
                Info = new PluginInfo("Second", "Description", ["Author"],
                [new UnchainedLauncher.Core.JsonModels.ModMetadata.Dependency("https://github.com/Chiv2-Community/UnchainedPlugin", "1.0.0")])
            };
            Index(first, second);
            var manager = Manager();
            await manager.RefreshAsync();
            manager.SetEnabled(manager.Releases.Single(release => release.Id == second.Id), true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection());
        }

        [Theory]
        [InlineData("1.2.0", "^1.0.0", true)]
        [InlineData("2.0.0", "^1.0.0", false)]
        [InlineData("0.2.5", "^0.2.0", true)]
        [InlineData("0.3.0", "^0.2.0", false)]
        [InlineData("1.0.0-RC2", ">=1.0.0", false)]
        public void PluginDependencyRangesUseSemver(string version, string range, bool expected) {
            Assert.Equal(expected, PluginInfo.MatchesVersion(version, range));
        }

        private string Package(string id, params DllRelease[] releases) => JsonSerializer.Serialize(new PluginPackage(
            releases.OrderByDescending(release => release.ReleaseDate).First(release => release.Info != null).Info!,
            releases.Select(release => new PluginRelease(release.Tag ?? "v" + release.Version, release.Sha256,
                release.DllFileName, release.DownloadUrl, release.Size, release.ReleaseDate, release.Info, release.ReleaseNotesMarkdown)).ToArray()));

        private void Packages(params DllRelease[] releases) {
            _handler.Responses[DllManager.PackageListUrl] = string.Join("\n", releases.Select(release => release.Id).Distinct());
            foreach (var group in releases.GroupBy(release => release.Id))
                _handler.Responses[$"{DllManager.DatabaseUrl}/packages/{group.Key}.json"] = Package(group.Key, group.ToArray());
            _handler.Binary = _binary;
        }

        private DllRelease ModernRelease(string name = "UnchainedPlugin", string target = "shared", string version = "1.0.0") =>
            Release(name, version) with {
                Info = new PluginInfo("Display " + name, "Description", ["Author"], RepoUrl: "https://github.com/Chiv2-Community/" + name, Target: target),
                Tag = "v" + version,
                DllFileName = name + "-native.dll",
                ReleaseDate = DateTimeOffset.Parse("2026-03-23T15:37:46+00:00"),
                ReleaseNotesMarkdown = "Release notes"
            };

        [Fact]
        public async Task ModernPackagesAreAuthoritativeAndPreservePinsAcrossCacheMigration() {
            var legacy = Release();
            Index(legacy);
            var manager = Manager();
            await manager.RefreshAsync();
            var modern = ModernRelease();
            Packages(modern);
            _handler.Responses[DllManager.IndexUrl] = "invalid stale index must not be read";
            await manager.RefreshAsync();
            Assert.True(manager.IsEnabled(modern));
            Assert.Equal(modern.Tag, Assert.Single(manager.Releases).Tag);
            Assert.Equal("shared", Assert.Single(manager.Releases).Info!.Target);
            await manager.PrepareAsync();
            var files = manager.OpenForInjection();
            try { Assert.Equal(modern.DllFileName, Path.GetFileName(Assert.Single(files).Path)); }
            finally { foreach (var file in files) file.Dispose(); }
            _handler.Fail = true;
            var cached = Manager();
            Assert.True(cached.IsEnabled(Assert.Single(cached.Releases)));
            await cached.PrepareAsync();
        }

        [Fact]
        public async Task RemovedPackageCannotBeReapprovedByStaleLegacyIndex() {
            var release = ModernRelease();
            Packages(release);
            var manager = Manager();
            await manager.RefreshAsync();
            _handler.Index = JsonSerializer.Serialize(new[] { release });
            _handler.Responses[DllManager.PackageListUrl] = "";
            await manager.RefreshAsync();
            Assert.Empty(manager.Releases);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
        }

        [Fact]
        public async Task LaunchTargetsFilterDownloadsAndInjectionWithoutChangingSelections() {
            var shared = ModernRelease();
            var client = ModernRelease("ClientPlugin", "client");
            var server = ModernRelease("ServerPlugin", "server");
            Packages(shared, client, server);
            var manager = Manager();
            await manager.RefreshAsync();
            manager.SetEnabled(manager.Releases.Single(release => release.Id == client.Id), true);
            manager.SetEnabled(manager.Releases.Single(release => release.Id == server.Id), true);
            await manager.PrepareAsync();
            var files = manager.OpenForInjection();
            try { Assert.Equal(new[] { client.DllFileName, shared.DllFileName }.Order(), files.Select(file => Path.GetFileName(file.Path)).Order()); }
            finally { foreach (var file in files) file.Dispose(); }
            Assert.DoesNotContain(Directory.GetFiles(PluginPath, "*.dll", SearchOption.AllDirectories), file => Path.GetFileName(file) == server.DllFileName);
            await manager.PrepareAsync(server: true);
            files = manager.OpenForInjection(server: true);
            try { Assert.Equal(new[] { server.DllFileName, shared.DllFileName }.Order(), files.Select(file => Path.GetFileName(file.Path)).Order()); }
            finally { foreach (var file in files) file.Dispose(); }
            // Selection is per call, not the side of the most recent preparation.
            files = manager.OpenForInjection(server: false);
            try { Assert.Equal(new[] { client.DllFileName, shared.DllFileName }.Order(), files.Select(file => Path.GetFileName(file.Path)).Order()); }
            finally { foreach (var file in files) file.Dispose(); }
            Assert.True(manager.IsEnabled(manager.Releases.Single(release => release.Id == client.Id)));
            Assert.True(manager.IsEnabled(manager.Releases.Single(release => release.Id == server.Id)));
        }

        [Fact]
        public async Task IncompatibleDependencyTargetFailsBeforeDownload() {
            var client = ModernRelease("OtherPlugin", "client");
            var shared = ModernRelease() with {
                Info = ModernRelease().Info! with {
                    PluginDependencies = [new UnchainedLauncher.Core.JsonModels.ModMetadata.Dependency("https://github.com/Chiv2-Community/OtherPlugin", "1.0.0")]
                }
            };
            Packages(shared, client);
            var manager = Manager();
            await manager.RefreshAsync();
            manager.SetEnabled(manager.Releases.Single(release => release.Id == client.Id), true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync(server: true));
            Assert.False(Directory.Exists(PluginPath));
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection(server: true));
            await manager.PrepareAsync(server: false);
        }

        [Fact]
        public async Task OppositeSidePakRequirementsDoNotBlockClientLaunch() {
            var shared = ModernRelease();
            var server = ModernRelease("ServerPlugin", "server");
            server = server with {
                Info = server.Info! with {
                    ModDependencies = [new UnchainedLauncher.Core.JsonModels.ModMetadata.Dependency("https://github.com/Org/Pak.Mod", "1.0.0")]
                }
            };
            Packages(shared, server);
            var manager = Manager();
            await manager.RefreshAsync();
            manager.SetEnabled(manager.Releases.Single(release => release.Id == server.Id), true);
            await manager.PrepareAsync(server: false);
            var files = manager.OpenForInjection(server: false);
            try { Assert.Single(files); }
            finally { foreach (var file in files) file.Dispose(); }
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync(server: true));
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection(server: true));
        }

        [Fact]
        public void PackagesPermitApprovedHistoricalNullMetadataAndDefaultSharedTarget() {
            var latest = ModernRelease();
            var historical = Release(version: "0.5.0");
            var json = Package(latest.Id, latest, historical);
            var releases = DllManager.ParsePackage(json, latest.Id);
            Assert.Equal(2, releases.Count);
            Assert.Null(releases.Single(release => release.Version == "0.5.0").Info);
            Assert.Null(releases.Single(release => release.Version == "0.5.0").DllFileName);
            var minimalInfo = JsonSerializer.Deserialize<PluginInfo>("""{"name":"Plugin","description":"Test","authors":["Author"]}""")!;
            minimalInfo.Validate();
            Assert.Equal("shared", minimalInfo.Target);
        }

        [Fact]
        public void PackageInfoComparisonUsesSchemaDefaultsAndIgnoresSchemaAnnotation() {
            var release = ModernRelease() with {
                Info = ModernRelease().Info! with {
                    Schema = "https://example.test/plugin.schema.json",
                    PluginDependencies = [],
                    ModDependencies = [],
                    ImageUrls = []
                }
            };
            var json = JsonSerializer.Serialize(new PluginPackage(ModernRelease().Info!, [new PluginRelease(release.Tag!, release.Sha256,
                release.DllFileName, release.DownloadUrl, release.Size, release.ReleaseDate, release.Info, null)]));
            Assert.Single(DllManager.ParsePackage(json, release.Id));
        }

        [Theory]
        [InlineData("both")]
        [InlineData("Client")]
        [InlineData("")]
        [InlineData(null)]
        public void InvalidPluginTargetsAreRejected(string? target) {
            var release = ModernRelease() with { Info = ModernRelease().Info! with { Target = target! } };
            Assert.Throws<InvalidDataException>(() => DllManager.ParsePackage(Package(release.Id, release), release.Id));
        }

        [Fact]
        public void PackageValidationRejectsIdentityMismatchMissingFieldsAndDuplicates() {
            var release = ModernRelease();
            var json = Package(release.Id, release);
            Assert.Throws<InvalidDataException>(() => DllManager.ParsePackage(json, "Another/Plugin"));
            Assert.Throws<InvalidDataException>(() => DllManager.ParsePackage(Package(release.Id, release, release), release.Id));
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            node["releases"]![0]!.AsObject().Remove("info");
            Assert.Throws<JsonException>(() => DllManager.ParsePackage(node.ToJsonString(), release.Id));
            Assert.Throws<InvalidDataException>(() => DllManager.ParsePackage("""{"latest_release_info":null,"releases":[]}""", release.Id));
        }

        [Fact]
        public void PakDependenciesAllowDottedRepositoriesButPluginsDoNot() {
            var dependency = new UnchainedLauncher.Core.JsonModels.ModMetadata.Dependency("https://github.com/Org/Pak.Mod", "^1.0.0");
            new PluginInfo("Plugin", "Description", ["Author"], ModDependencies: [dependency]).Validate();
            Assert.Throws<InvalidDataException>(() => new PluginInfo("Plugin", "Description", ["Author"], PluginDependencies: [dependency]).Validate());
            Assert.Throws<InvalidDataException>(() => new PluginInfo("Plugin", "Description", ["Author"],
                ModDependencies: [dependency with { RepoUrl = "https://github.com/Org/.." }]).Validate());
        }

        [Fact]
        public async Task DisabledDownloadsRequireExistingValidatedFilesAndWorkOffline() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync(allowDownloads: false));
            Assert.False(Directory.Exists(PluginPath));
            await manager.PrepareAsync();
            _handler.Fail = true;
            await manager.PrepareAsync(allowDownloads: false);
        }

        [Fact]
        public async Task UpdateCandidatesRespectPrereleasePermissionLaunchTargetAndEnabledSelection() {
            var current = ModernRelease();
            Packages(current);
            var manager = Manager();
            await manager.RefreshAsync();
            Packages(current, ModernRelease(version: "2.0.0"), ModernRelease(target: "server", version: "3.0.0"),
                ModernRelease(target: "client", version: "4.0.0-RC1"), ModernRelease("DisabledPlugin", version: "10.0.0"));
            await manager.RefreshAsync();
            Assert.Equal("2.0.0", Assert.Single(manager.GetUpdateCandidates(server: false, allowUnstable: false)).Latest.Version);
            Assert.Equal("4.0.0-RC1", Assert.Single(manager.GetUpdateCandidates(server: false, allowUnstable: true)).Latest.Version);
            Assert.Equal("3.0.0", Assert.Single(manager.GetUpdateCandidates(server: true, allowUnstable: true)).Latest.Version);
            Assert.True(manager.IsEnabled(current)); // Merely checking does not change the pin.
        }

        private sealed class UpdateDialogue(bool approve) : UnchainedLauncher.Core.Services.IUserDialogueSpawner {
            public List<UnchainedLauncher.Core.Services.DependencyUpdate> Updates { get; } = [];
            public List<string> Messages { get; } = [];
            public void DisplayMessage(string message) => Messages.Add(message);
            public UnchainedLauncher.Core.Services.UserDialogueChoice DisplayYesNoMessage(string message, string caption) =>
                approve ? UnchainedLauncher.Core.Services.UserDialogueChoice.Yes : UnchainedLauncher.Core.Services.UserDialogueChoice.No;
            public UnchainedLauncher.Core.Services.UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText,
                string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<UnchainedLauncher.Core.Services.DependencyUpdate> updates) {
                Updates.AddRange(updates);
                return DisplayYesNoMessage(messageText, titleText);
            }
            public Action DisplayProgress(UnchainedLauncher.Core.Utilities.MemoryProgress progress) => () => { };
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task LaunchSettingsControlConfirmedAutomaticUpdates(bool allowUnstable, bool approve) {
            var current = Release();
            Index(current);
            var manager = Manager();
            await manager.RefreshAsync();
            await manager.PrepareAsync();
            Index(current, Release(version: "2.0.0"), Release(version: "3.0.0-RC1"));
            var dialogue = new UpdateDialogue(approve);
            var preparer = new UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers.DllLaunchPreparer(manager, dialogue);
            var options = new UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchOptions([], LanguageExt.Option<string>.None, "",
                true, allowUnstable, LanguageExt.Option<string>.None,
                LanguageExt.Option<UnchainedLauncher.Core.Services.Processes.Chivalry.ServerLaunchOptions>.None);
            Assert.True((await preparer.PrepareLaunch(options)).IsSome);
            var expectedVersion = allowUnstable ? "3.0.0-RC1" : "2.0.0";
            Assert.Equal(expectedVersion, Assert.Single(dialogue.Updates).LatestVersion);
            Assert.Equal(approve ? expectedVersion : current.Version, Assert.Single(manager.Releases.Where(manager.IsEnabled)).Version);
            Assert.Empty(dialogue.Messages);
        }

        [Fact]
        public async Task DisabledAutomaticUpdatesSkipRefreshOffersAndDownloads() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            _handler.Fail = true;
            var dialogue = new UpdateDialogue(true);
            var preparer = new UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers.DllLaunchPreparer(manager, dialogue);
            var options = new UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchOptions([], LanguageExt.Option<string>.None, "",
                false, false, LanguageExt.Option<string>.None,
                LanguageExt.Option<UnchainedLauncher.Core.Services.Processes.Chivalry.ServerLaunchOptions>.None);
            Assert.True((await preparer.PrepareLaunch(options)).IsNone);
            Assert.Empty(dialogue.Updates);
            Assert.Contains("downloads are disabled", Assert.Single(dialogue.Messages));
            _handler.Fail = false;
            await manager.PrepareAsync();
            _handler.Fail = true;
            Assert.True((await preparer.PrepareLaunch(options)).IsSome);
            Assert.Empty(dialogue.Updates);
        }

        private void Index(params DllRelease[] releases) {
            _handler.Index = JsonSerializer.Serialize(releases);
            _handler.Binary = _binary;
        }

        [Fact]
        public async Task DefaultPluginOnlyAndLooseFilesIgnored() {
            Index(Release(), Release("OtherPlugin"));
            Directory.CreateDirectory(PluginPath);
            File.WriteAllText(Path.Combine(PluginPath, "arbitrary.dll"), "unregistered");
            var manager = Manager();
            await manager.RefreshAsync();
            Assert.True(manager.IsEnabled(Release()));
            Assert.False(manager.IsEnabled(Release("OtherPlugin")));
            await manager.PrepareAsync();
            var files = manager.OpenForInjection();
            try {
                Assert.Single(files);
                Assert.Equal("UnchainedPlugin.dll", Path.GetFileName(files[0].Path));
                Assert.DoesNotContain("arbitrary", files[0].Path);
            }
            finally { foreach (var file in files) file.Dispose(); }
        }

        [Fact]
        public async Task AdditionalPluginCanBeEnabledAndDefaultDisabledAcrossRestart() {
            var plugin = Release();
            var other = Release("OtherPlugin");
            Index(plugin, other);
            var manager = Manager();
            await manager.RefreshAsync();
            manager.SetEnabled(other, true);
            manager.SetEnabled(plugin, false);
            manager = Manager();
            Assert.False(manager.IsEnabled(plugin));
            Assert.True(manager.IsEnabled(other));
            await manager.PrepareAsync();
            var files = manager.OpenForInjection();
            try { Assert.Equal("OtherPlugin.dll", Path.GetFileName(Assert.Single(files).Path)); }
            finally { foreach (var file in files) file.Dispose(); }
        }

        [Fact]
        public async Task InjectionIgnoresDisabledInstalledDllsAndStraysInManagedDirectories() {
            var plugin = Release();
            var other = Release("OtherPlugin");
            Index(plugin, other);
            var manager = Manager();
            await manager.RefreshAsync();
            manager.SetEnabled(other, true);
            await manager.PrepareAsync();
            var installed = manager.OpenForInjection();
            string pluginPath;
            string otherPath;
            try {
                pluginPath = installed.Single(file => Path.GetFileName(file.Path) == "UnchainedPlugin.dll").Path;
                otherPath = installed.Single(file => Path.GetFileName(file.Path) == "OtherPlugin.dll").Path;
            }
            finally { foreach (var file in installed) file.Dispose(); }
            // Even byte-identical loose DLLs beside an approved DLL are not selected.
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(pluginPath)!, "stray.dll"), _binary);
            File.WriteAllBytes(Path.Combine(PluginPath, "UnchainedPlugin.dll"), _binary);
            manager.SetEnabled(other, false);
            Assert.True(File.Exists(otherPath));
            var selected = manager.OpenForInjection();
            try { Assert.Equal(pluginPath, Assert.Single(selected).Path); }
            finally { foreach (var file in selected) file.Dispose(); }

            manager.SetEnabled(plugin, false);
            Assert.Empty(manager.OpenForInjection());
            // An empty managed selection never invokes native injection, even
            // when DLLs remain on disk. This also runs on non-Windows hosts.
            Assert.True(new UnchainedLauncher.Core.Services.Processes.DllInjector(manager)
                .Inject(System.Diagnostics.Process.GetCurrentProcess()));
        }

        [Fact]
        public async Task InjectorRejectsTamperingWithoutFallingBackToLooseDlls() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            await manager.PrepareAsync();
            var selected = manager.OpenForInjection();
            string path;
            try { path = Assert.Single(selected).Path; }
            finally { foreach (var file in selected) file.Dispose(); }
            File.WriteAllBytes(Path.Combine(PluginPath, "UnchainedPlugin.dll"), _binary);
            File.WriteAllText(path, "tampered");
            Assert.False(new UnchainedLauncher.Core.Services.Processes.DllInjector(manager)
                .Inject(System.Diagnostics.Process.GetCurrentProcess()));
            File.Delete(path);
            Assert.False(new UnchainedLauncher.Core.Services.Processes.DllInjector(manager)
                .Inject(System.Diagnostics.Process.GetCurrentProcess()));
        }

        [Fact]
        public async Task VersionsStayPinnedUntilExplicitlyChanged() {
            var first = Release();
            Index(first);
            var manager = Manager();
            await manager.RefreshAsync();
            var second = Release(version: "2.0.0");
            Index(first, second);
            await manager.RefreshAsync();
            Assert.True(manager.IsEnabled(first));
            Assert.False(manager.IsEnabled(second));
            manager.SetEnabled(second, true);
            Assert.False(manager.IsEnabled(first));
            Assert.True(manager.IsEnabled(second));
        }

        [Fact]
        public async Task DefaultSelectsLatestStableSemanticVersion() {
            Index(Release(version: "2.0.0"), Release(version: "10.0.0"), Release(version: "11.0.0-rc.1"));
            var manager = Manager();
            await manager.RefreshAsync();
            Assert.True(manager.IsEnabled(Release(version: "10.0.0")));
        }

        [Fact]
        public async Task OfflineCachedMetadataAndValidatedBinaryWork() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            await manager.PrepareAsync();
            _handler.Fail = true;
            manager = Manager();
            await Assert.ThrowsAsync<HttpRequestException>(() => manager.RefreshAsync());
            await manager.PrepareAsync();
            var files = manager.OpenForInjection();
            try { Assert.Single(files); }
            finally { foreach (var file in files) file.Dispose(); }
        }

        [Fact]
        public async Task TamperedInstalledBinaryFailsBeforeLaunchAndAtInjection() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            await manager.PrepareAsync();
            var path = Directory.GetFiles(PluginPath, "*.dll", SearchOption.AllDirectories).Single();
            File.WriteAllBytes(path, Enumerable.Repeat((byte)0, _binary.Length).ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(() => manager.PrepareAsync());
            Assert.Throws<InvalidDataException>(() => manager.OpenForInjection());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task CorruptTruncatedAndOversizedDownloadsAreNeverInstalled(int mode) {
            Index(Release());
            _handler.Binary = mode switch {
                0 => Enumerable.Repeat((byte)0, _binary.Length).ToArray(),
                1 => _binary[..^1],
                _ => _binary.Concat(new byte[] { 0 }).ToArray()
            };
            var manager = Manager();
            await manager.RefreshAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => manager.PrepareAsync());
            Assert.Empty(Directory.GetFiles(PluginPath, "*.dll", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(PluginPath, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task RemovedEnabledReleaseFailsClosed() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            Index();
            await manager.RefreshAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
        }

        [Fact]
        public async Task MissingRegistryDoesNotFallBackToLooseDlls() {
            var manager = Manager();
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareAsync());
            Assert.Throws<InvalidOperationException>(() => manager.OpenForInjection());
        }

        [Fact]
        public async Task InvalidRefreshPreservesLastValidIndex() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            _handler.Index = "[null]";
            await Assert.ThrowsAsync<InvalidDataException>(() => manager.RefreshAsync());
            Assert.Single(manager.Releases);
            Assert.Single(Manager().Releases);
        }

        [Fact]
        public void InvalidMetadataAndDuplicatesRejected() {
            foreach (var invalid in new[] {
                Release() with { Organization = "../escape" }, Release() with { Name = "foo.dll" },
                Release() with { Version = "latest" }, Release() with { DownloadUrl = "http://example.com/a.dll" },
                Release() with { DownloadUrl = "https://example.com/a.pak" },
                Release() with { Sha256 = "bad" }, Release() with { Size = 0 }
            }) {
                Assert.Throws<InvalidDataException>(() => DllManager.ParseIndex(JsonSerializer.Serialize(new[] { invalid })));
            }
            Assert.Throws<InvalidDataException>(() => DllManager.ParseIndex(JsonSerializer.Serialize(new[] { Release(), Release() })));
            Assert.Throws<InvalidDataException>(() => DllManager.ParseIndex(JsonSerializer.Serialize(new[] {
                Release(), Release() with { Name = "UNCHAINEDPLUGIN" }
            })));
        }

        [Fact]
        public async Task UnregisteredSelectionRejected() {
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            Assert.Throws<InvalidOperationException>(() => manager.SetEnabled(Release("Unregistered"), true));
        }

        [Fact]
        public async Task InjectionHandlesPreventModificationOnWindows() {
            if (!OperatingSystem.IsWindows()) return;
            Index(Release());
            var manager = Manager();
            await manager.RefreshAsync();
            await manager.PrepareAsync();
            var files = manager.OpenForInjection();
            try { Assert.Throws<IOException>(() => File.WriteAllText(files[0].Path, "tampered")); }
            finally { foreach (var file in files) file.Dispose(); }
        }

        public void Dispose() { _http.Dispose(); Directory.Delete(_dir, true); }

        private sealed class FixtureHandler : HttpMessageHandler {
            public Dictionary<string, string?> Responses { get; } = new();
            public string Index { get; set; } = "[]";
            public byte[] Binary { get; set; } = [];
            public bool Fail { get; set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                if (Fail) throw new HttpRequestException("Offline fixture");
                if (Responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var content))
                    return Task.FromResult(new HttpResponseMessage(content == null ? HttpStatusCode.NotFound : HttpStatusCode.OK) {
                        RequestMessage = request,
                        Content = new StringContent(content ?? "")
                    });
                if (request.RequestUri!.AbsoluteUri == DllManager.PackageListUrl)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    RequestMessage = request,
                    Content = request.RequestUri!.AbsoluteUri == DllManager.IndexUrl
                        ? new StringContent(Index) : new ByteArrayContent(Binary)
                });
            }
        }
    }
}
