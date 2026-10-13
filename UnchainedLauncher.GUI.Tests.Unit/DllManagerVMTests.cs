using System.Text.Json;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.GUI.ViewModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit {
    public sealed class DllManagerVMTests {
        [Fact]
        public async Task PrereleasesAreOptInAndVersionsUseSemverPrecedence() {
            var directory = Path.Combine(Path.GetTempPath(), "dll-view-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var versions = new[] { "1.9.0", "1.0.0-RC1", "1.10.0", "1.0.0-rc0", "1.0.0-RC2", "1.0.0", "1.0.0-rc.2", "1.0.0-rc.10" };
                var releases = versions.Select(version => new DllRelease("Chiv2-Community", "UnchainedPlugin", version,
                    "https://example.com/UnchainedPlugin.dll", new string('a', 64), 1)).ToArray();
                using var http = new HttpClient(new IndexHandler(JsonSerializer.Serialize(releases)));
                var manager = new DllManager(Path.Combine(directory, "Plugins"), Path.Combine(directory, "state.json"), http);
                var vm = new DllManagerVM(manager);
                await vm.Refresh();
                Assert.False(vm.AllowUnstablePluginReleases);
                var plugin = Assert.Single(vm.Plugins);
                Assert.Equal(new[] { "1.10.0", "1.9.0", "1.0.0" }, plugin.AvailableVersions.Select(row => row.Version));
                Assert.All(plugin.AvailableVersions, row => Assert.False(row.IsPrerelease));
                Assert.True(plugin.IsEnabled);
                Assert.Equal("1.10.0", plugin.SelectedVersion!.Version);

                vm.AllowUnstablePluginReleases = true;
                plugin = Assert.Single(vm.Plugins);
                Assert.Equal(new[] { "1.10.0", "1.9.0", "1.0.0", "1.0.0-rc0", "1.0.0-rc.10", "1.0.0-rc.2", "1.0.0-RC2", "1.0.0-RC1" }, plugin.AvailableVersions.Select(row => row.Version));
                var rc = plugin.AvailableVersions.Single(row => row.Version == "1.0.0-RC2");
                Assert.True(rc.IsPrerelease);
                plugin.SelectedVersion = rc;
                Assert.False(manager.IsEnabled(releases.Single(release => release.Version == "1.10.0")));
                vm.AllowUnstablePluginReleases = false;
                plugin = Assert.Single(vm.Plugins);
                Assert.Null(plugin.SelectedVersion);
                Assert.True(plugin.HasHiddenEnabledVersion);
                Assert.DoesNotContain(plugin.AvailableVersions, row => row.IsPrerelease);
                Assert.True(manager.IsEnabled(rc.Release));
                vm.AllowUnstablePluginReleases = true;
                plugin = Assert.Single(vm.Plugins);
                Assert.Equal(rc.Release, plugin.SelectedVersion!.Release);
                await vm.Refresh();
                Assert.Contains(Assert.Single(vm.Plugins).AvailableVersions, row => row.IsPrerelease);
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public async Task OneCardPerPluginAndEnableDisableChangesOnlyThatPlugin() {
            var directory = Path.Combine(Path.GetTempPath(), "dll-group-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                DllRelease Release(string org, string name, string version) => new(org, name, version,
                    "https://example.com/" + name + ".dll", new string('a', 64), 1);
                var releases = new[] { Release("Org", "Plugin", "1.0.0"), Release("Org", "Plugin", "2.0.0"),
                    Release("OtherOrg", "Plugin", "1.0.0"), Release("Org", "OnlyRC", "1.0.0-RC1") };
                releases[1] = releases[1] with { Info = new PluginInfo("Display name", "Description", ["Author"], [],
                    "https://example.com/icon.png", ["https://example.com/screenshot.png"]) };
                using var http = new HttpClient(new IndexHandler(JsonSerializer.Serialize(releases)));
                var state = Path.Combine(directory, "state.json");
                File.WriteAllText(state, "{}"); // Do not request the default plugin.
                var manager = new DllManager(Path.Combine(directory, "Plugins"), state, http);
                var vm = new DllManagerVM(manager);
                await vm.Refresh();
                Assert.Equal(2, vm.Plugins.Count);
                var plugin = vm.Plugins.Single(row => row.Organization == "Org");
                Assert.Equal(2, plugin.AvailableVersions.Count);
                Assert.Equal("Display name", plugin.Name);
                Assert.Equal("Description", plugin.Description);
                Assert.Equal("Author", plugin.Authors);
                Assert.Equal("None", plugin.Dependencies);
                Assert.Equal("https://example.com/icon.png", plugin.IconUrl);
                Assert.Equal(new[] { "https://example.com/screenshot.png" }, plugin.ImageUrls);
                Assert.False(plugin.IsEnabled);
                var notifications = new List<string?>();
                plugin.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
                plugin.EnableOrDisable();
                Assert.Equal("2.0.0", plugin.SelectedVersion!.Version);
                Assert.Equal("Disable", plugin.ButtonText);
                Assert.Contains(nameof(plugin.SelectedVersion), notifications);
                Assert.False(vm.Plugins.Single(row => row.Organization == "OtherOrg").IsEnabled);
                plugin.SelectedVersion = plugin.AvailableVersions.Single(row => row.Version == "1.0.0");
                Assert.Equal("Plugin", plugin.Name); // Legacy metadata remains readable.
                Assert.True(manager.IsEnabled(releases[0]));
                Assert.False(manager.IsEnabled(releases[1]));
                await vm.Refresh();
                plugin = vm.Plugins.Single(row => row.Organization == "Org");
                Assert.Equal("1.0.0", plugin.SelectedVersion!.Version); // Refresh does not update the selection.
                plugin.EnableOrDisable();
                Assert.False(plugin.IsEnabled);
                Assert.Null(plugin.SelectedVersion);
                Assert.Equal("Enable", plugin.ButtonText);
                Assert.False(manager.IsEnabled(releases[0]));
                vm.AllowUnstablePluginReleases = true;
                Assert.Equal(3, vm.Plugins.Count);
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public async Task ModernPackageCardsShowReleaseSpecificTargetsAssetsAndNotes() {
            var directory = Path.Combine(OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "dll-package-view-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var shared = new PluginInfo("Core", "Description", ["Author"], Target: "shared");
                var server = shared with { Name = "Server core", Target = "server" };
                var date = DateTimeOffset.Parse("2026-03-23T15:37:46+00:00");
                PluginRelease Release(string tag, PluginInfo info, string file, string notes, DateTimeOffset published) => new(tag,
                    new string('a', 64), file, "https://example.com/" + file, 123, published, info, notes);
                var package = new PluginPackage(server, [Release("v1.0.0", shared, "Core.dll", "Stable notes", date),
                    Release("v2.0.0-RC1", server, "ServerCore.dll", "RC notes", date.AddDays(1))]);
                using var http = new HttpClient(new PackageHandler(JsonSerializer.Serialize(package)));
                var manager = new DllManager(Path.Combine(directory, "Plugins"), Path.Combine(directory, "state.json"), http);
                var vm = new DllManagerVM(manager);
                await vm.Refresh();
                var plugin = Assert.Single(vm.Plugins);
                Assert.Equal("shared", plugin.Target);
                Assert.Equal("Core.dll", plugin.FileName);
                Assert.Equal("v1.0.0", plugin.ReleaseTag);
                Assert.Equal("Stable notes", plugin.ReleaseNotes);
                vm.AllowUnstablePluginReleases = true;
                plugin = Assert.Single(vm.Plugins);
                var notifications = new List<string?>();
                plugin.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
                plugin.SelectedVersion = plugin.AvailableVersions.Single(row => row.IsPrerelease);
                Assert.Equal("server", plugin.Target);
                Assert.Equal("ServerCore.dll", plugin.FileName);
                Assert.Equal("RC notes", plugin.ReleaseNotes);
                Assert.Equal(date.AddDays(1).ToString("u"), plugin.ReleaseDate);
                Assert.Contains(nameof(plugin.Target), notifications);
                Assert.Contains(nameof(plugin.ReleaseNotes), notifications);
                vm.AllowUnstablePluginReleases = false;
                plugin = Assert.Single(vm.Plugins);
                Assert.True(plugin.HasHiddenEnabledVersion);
                Assert.Equal("Server core", plugin.Name);
                Assert.Equal("server", plugin.Target);
                Assert.Equal("RC notes", plugin.ReleaseNotes);
            }
            finally { Directory.Delete(directory, true); }
        }

        private sealed class PackageHandler(string json) : HttpMessageHandler {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                var body = request.RequestUri!.AbsoluteUri == DllManager.PackageListUrl ? "Chiv2-Community/UnchainedPlugin\n" : json;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body) });
            }
        }

        private sealed class PluginSettings : IPluginSettings, System.ComponentModel.INotifyPropertyChanged {
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            public bool EnablePluginAutomaticUpdates { get; set; } = true;
            private bool _allowUnstable;
            public bool AllowUnstablePluginReleases {
                get => _allowUnstable;
                set {
                    _allowUnstable = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AllowUnstablePluginReleases)));
                }
            }
        }

        [Fact]
        public async Task OriginalSettingsDriveDllControlsAndPrereleaseVisibility() {
            var directory = Path.Combine(OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "dll-settings-view-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var releases = new[] { "1.0.0", "2.0.0-RC1" }.Select(version => new DllRelease("Chiv2-Community", "UnchainedPlugin", version,
                    "https://example.com/UnchainedPlugin.dll", new string('a', 64), 1)).ToArray();
                using var http = new HttpClient(new IndexHandler(JsonSerializer.Serialize(releases)));
                var manager = new DllManager(Path.Combine(directory, "Plugins"), Path.Combine(directory, "state.json"), http);
                var settings = new PluginSettings { EnablePluginAutomaticUpdates = false, AllowUnstablePluginReleases = true };
                var vm = new DllManagerVM(manager, settings);
                await vm.Refresh();
                Assert.False(vm.EnablePluginAutomaticUpdates);
                Assert.True(vm.AllowUnstablePluginReleases);
                Assert.Equal(2, Assert.Single(vm.Plugins).AvailableVersions.Count);
                vm.AllowUnstablePluginReleases = false;
                Assert.False(settings.AllowUnstablePluginReleases);
                Assert.Single(Assert.Single(vm.Plugins).AvailableVersions);
                vm.EnablePluginAutomaticUpdates = true;
                Assert.True(settings.EnablePluginAutomaticUpdates);
                settings.AllowUnstablePluginReleases = true;
                Assert.Equal(2, Assert.Single(vm.Plugins).AvailableVersions.Count);
                manager.SetEnabled(manager.Releases.Single(release => release.Version == "2.0.0-RC1"), true);
                Assert.Equal("2.0.0-RC1", Assert.Single(vm.Plugins).SelectedVersion!.Version);
                var recreated = new DllManagerVM(manager, settings);
                await recreated.Refresh();
                Assert.True(recreated.EnablePluginAutomaticUpdates);
                Assert.True(recreated.AllowUnstablePluginReleases);
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public void DllManagerHasOriginalSettingsControlsWithoutPreambleOrSeparatePrereleaseToggle() {
            var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.xaml"));
            System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            System.Xml.Linq.XNamespace language = "http://schemas.microsoft.com/winfx/2006/xaml";
            var tab = document.Descendants(xaml + "TabItem").Single(element => (string?)element.Attribute(language + "Name") == "PluginManagerTab");
            var header = Assert.Single(tab.Elements(xaml + "TabItem.Header"));
            Assert.Contains(header.Descendants(xaml + "TextBlock"), element => (string?)element.Attribute("Text") == "🔌");
            Assert.Contains(header.Descendants(xaml + "TextBlock"), element => (string?)element.Attribute("Text") == "Plugin Manager");
            var controls = tab.Descendants(xaml + "CheckBox").ToArray();
            Assert.Equal(2, controls.Length);
            Assert.Equal("Enable Automatic Plugin Updates and Downloads", (string?)controls[0].Attribute("Content"));
            Assert.Equal("{Binding EnablePluginAutomaticUpdates}", (string?)controls[0].Attribute("IsChecked"));
            Assert.Equal("Enable Unstable Plugin Releases", (string?)controls[1].Attribute("Content"));
            Assert.Equal("{Binding AllowUnstablePluginReleases}", (string?)controls[1].Attribute("IsChecked"));
            Assert.Equal("{Binding EnablePluginAutomaticUpdates}", (string?)controls[1].Attribute("IsEnabled"));
            Assert.DoesNotContain("UnchainedPlugin is enabled by default", tab.ToString());
            Assert.DoesNotContain("Show prereleases", tab.ToString());
            var footer = controls[0].Parent!;
            Assert.Same(footer, controls[1].Parent);
            Assert.Equal("2", (string?)footer.Attribute("Grid.Row"));
            Assert.Equal("1", (string?)controls[0].Attribute("Grid.Row"));
            Assert.Equal("1", (string?)controls[1].Attribute("Grid.Row"));
            Assert.Equal("0", (string?)controls[0].Attribute("Grid.Column"));
            Assert.Equal("1", (string?)controls[1].Attribute("Grid.Column"));
            var divider = Assert.Single(footer.Elements(xaml + "Border"));
            Assert.Equal("0", (string?)divider.Attribute("Grid.Row"));
            Assert.Equal("2", (string?)divider.Attribute("Grid.ColumnSpan"));
            Assert.Equal("{DynamicResource Brush.Border}", (string?)divider.Attribute("BorderBrush"));
            Assert.Equal("0,1,0,0", (string?)divider.Attribute("BorderThickness"));
            Assert.Equal("True", (string?)divider.Attribute("SnapsToDevicePixels"));
            Assert.DoesNotContain(tab.Descendants(xaml + "TextBlock"), element => (string?)element.Attribute("Text") == "{Binding Status}");
            var toolbar = tab.Descendants(xaml + "Grid").Single(element => (string?)element.Attribute("Grid.Row") == "0"
                && element.Elements(xaml + "TextBox").Any());
            Assert.Equal("{Binding SearchTerm, UpdateSourceTrigger=PropertyChanged}", (string?)Assert.Single(toolbar.Elements(xaml + "TextBox")).Attribute("Text"));
            Assert.Equal("{Binding SelectedSortMode}", (string?)Assert.Single(toolbar.Elements(xaml + "ComboBox")).Attribute("SelectedItem"));
            var update = toolbar.Elements(xaml + "Button").Single(element => (string?)element.Attribute("Content") == "Update Plugins");
            Assert.Equal("{Binding UpdatePluginsCommand}", (string?)update.Attribute("Command"));
            var refresh = toolbar.Elements(xaml + "Button").Single(element => (string?)element.Attribute("Content") == "Refresh Plugin List");
            Assert.Equal("{StaticResource Button.Primary}", (string?)refresh.Attribute("Style"));
            Assert.Equal("{Binding RefreshCommand}", (string?)refresh.Attribute("Command"));
            Assert.Contains(tab.Descendants(xaml + "Button"), element => (string?)element.Attribute("Command") == "{Binding ViewOnGithubCommand}");
            Assert.Contains(tab.Descendants(xaml + "Button"), element => (string?)element.Attribute("Command") == "{Binding OpenGithubReleaseCommand}");
        }

        private sealed class MutableIndexHandler : HttpMessageHandler {
            public string Json { get; set; } = "[]";
            public bool Fail { get; set; }
            public List<string> Requests { get; } = [];
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                Requests.Add(request.RequestUri!.AbsoluteUri);
                if (Fail) throw new HttpRequestException("Offline fixture");
                return Task.FromResult(new HttpResponseMessage(request.RequestUri.AbsoluteUri == DllManager.PackageListUrl
                    ? System.Net.HttpStatusCode.NotFound : System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(Json) });
            }
        }

        [Fact]
        public async Task PluginLinksOpenRepositoryAndOriginalDisplayedTagWithoutChangingSelection() {
            var directory = Path.Combine(OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "plugin-links-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var handler = new MutableIndexHandler { Json = JsonSerializer.Serialize(new[] {
                    Release("Plugin"), Release("Plugin", "2.0.0-RC1+build.abc")
                }) };
                using var http = new HttpClient(handler);
                var state = Path.Combine(directory, "state.json");
                File.WriteAllText(state, "{}");
                var manager = new DllManager(Path.Combine(directory, "Plugins"), state, http);
                await manager.RefreshAsync();
                var opened = new List<string>();
                var errors = new List<string>();
                var plugin = new DllPluginVM(manager, manager.Releases, true, _ => { }, errors.Add, opened.Add);
                plugin.ViewOnGithubCommand.Execute(null);
                plugin.OpenGithubReleaseCommand.Execute(null);
                Assert.Equal(new[] { "https://github.com/Chiv2-Community/Plugin",
                    "https://github.com/Chiv2-Community/Plugin/releases/tag/v2.0.0-RC1%2Bbuild.abc" }, opened);
                Assert.Empty(manager.Releases.Where(manager.IsEnabled));
                var changed = new List<string?>();
                plugin.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
                plugin.SelectedVersion = plugin.AvailableVersions.Single(row => !row.IsPrerelease);
                plugin.OpenGithubReleaseCommand.Execute(null);
                Assert.Equal("https://github.com/Chiv2-Community/Plugin/releases/tag/v1.0.0", opened.Last());
                Assert.Contains(nameof(plugin.GithubReleaseUrl), changed);
                plugin.SelectedVersion = plugin.AvailableVersions.Single(row => row.IsPrerelease);
                var hidden = new DllPluginVM(manager, manager.Releases, false, _ => { }, errors.Add, opened.Add);
                Assert.True(hidden.HasHiddenEnabledVersion);
                hidden.OpenGithubReleaseCommand.Execute(null);
                Assert.Equal("https://github.com/Chiv2-Community/Plugin/releases/tag/v2.0.0-RC1%2Bbuild.abc", opened.Last());
                Assert.Empty(errors);
                var failing = new DllPluginVM(manager, manager.Releases, true, _ => { }, errors.Add,
                    _ => throw new InvalidOperationException("Browser unavailable"));
                failing.ViewOnGithubCommand.Execute(null);
                Assert.Contains("Could not open GitHub link", Assert.Single(errors));
            }
            finally { Directory.Delete(directory, true); }
        }

        private static DllRelease Release(string name, string version = "1.0.0", string date = "2026-01-01T00:00:00Z", string target = "shared") =>
            new("Chiv2-Community", name, version, "https://example.com/" + name + ".dll", new string('a', 64), 1,
                new PluginInfo(name + " tools", "Useful widgets", ["Jane"], Target: target), "v" + version, DateTimeOffset.Parse(date));

        [Fact]
        public async Task PluginSearchAndSharedSortModesRebuildWithoutChangingSelections() {
            var directory = Path.Combine(OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "plugin-sort-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var handler = new MutableIndexHandler { Json = JsonSerializer.Serialize(new[] {
                    Release("Alpha", date: "2020-01-01T00:00:00Z"), Release("Alpha", "2.0.0", "2026-04-01T00:00:00Z"),
                    Release("Beta", date: "2025-01-01T00:00:00Z"), Release("Beta", "2.0.0", "2026-02-01T00:00:00Z"),
                    Release("Gamma", date: "2024-01-01T00:00:00Z"), Release("OnlyRC", "1.0.0-RC1")
                }) };
                using var http = new HttpClient(handler);
                var state = Path.Combine(directory, "state.json");
                File.WriteAllText(state, "{}");
                var manager = new DllManager(Path.Combine(directory, "Plugins"), state, http);
                var vm = new DllManagerVM(manager);
                await vm.Refresh();
                manager.SetEnabled(manager.Releases.Single(release => release.Name == "Gamma"), true);
                Assert.Equal(new[] { "Gamma", "Alpha", "Beta" }, vm.Plugins.Select(plugin => plugin.RepositoryName));
                vm.SelectedSortMode = ModSortMode.Alphabetical;
                Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, vm.Plugins.Select(plugin => plugin.RepositoryName));
                vm.SelectedSortMode = ModSortMode.LatestReleaseDateFirst;
                Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, vm.Plugins.Select(plugin => plugin.RepositoryName));
                vm.SelectedSortMode = ModSortMode.NewestModsFirst;
                Assert.Equal(new[] { "Beta", "Gamma", "Alpha" }, vm.Plugins.Select(plugin => plugin.RepositoryName));
                vm.SearchTerm = "  ALPHA tools  ";
                Assert.Equal("Alpha", Assert.Single(vm.Plugins).RepositoryName);
                vm.SearchTerm = "community/beta";
                Assert.Equal("Beta", Assert.Single(vm.Plugins).RepositoryName);
                vm.SearchTerm = "jane";
                Assert.Equal(3, vm.Plugins.Count);
                vm.SearchTerm = "widgets";
                Assert.Equal(3, vm.Plugins.Count);
                vm.SearchTerm = "missing";
                Assert.Empty(vm.Plugins);
                vm.SearchTerm = "";
                Assert.Equal(new[] { "Beta", "Gamma", "Alpha" }, vm.Plugins.Select(plugin => plugin.RepositoryName));
                vm.AllowUnstablePluginReleases = true;
                Assert.Equal(4, vm.Plugins.Count);
                Assert.Equal("Gamma", Assert.Single(manager.Releases.Where(manager.IsEnabled)).Name);
                await vm.Refresh();
                Assert.Equal(ModSortMode.NewestModsFirst, vm.SelectedSortMode);
                manager.SetEnabled(manager.Releases.Single(release => release.Name == "OnlyRC"), true);
                vm.AllowUnstablePluginReleases = false;
                var hidden = vm.Plugins.Single(plugin => plugin.RepositoryName == "OnlyRC");
                Assert.True(hidden.HasHiddenEnabledVersion);
                Assert.Empty(hidden.AvailableVersions);
                hidden.EnableOrDisable();
                Assert.DoesNotContain(vm.Plugins, plugin => plugin.RepositoryName == "OnlyRC");
            }
            finally { Directory.Delete(directory, true); }
        }

        private sealed class UpdateDialogue(bool approve) : UnchainedLauncher.Core.Services.IUserDialogueSpawner {
            public List<UnchainedLauncher.Core.Services.DependencyUpdate> Updates { get; } = [];
            public List<string> Messages { get; } = [];
            public void DisplayMessage(string message) => Messages.Add(message);
            public UnchainedLauncher.Core.Services.UserDialogueChoice DisplayYesNoMessage(string message, string caption) => approve
                ? UnchainedLauncher.Core.Services.UserDialogueChoice.Yes : UnchainedLauncher.Core.Services.UserDialogueChoice.No;
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
        public async Task UpdatePluginsConfirmsEligibleVisibleEnabledVersionsWithoutDownloading(bool unstable, bool approve) {
            var directory = Path.Combine(OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath(), "plugin-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var handler = new MutableIndexHandler { Json = JsonSerializer.Serialize(new[] {
                    Release("UnchainedPlugin"), Release("ServerPlugin", target: "server"), Release("DisabledPlugin")
                }) };
                using var http = new HttpClient(handler);
                var manager = new DllManager(Path.Combine(directory, "Plugins"), Path.Combine(directory, "state.json"), http);
                var dialogue = new UpdateDialogue(approve);
                var settings = new PluginSettings { EnablePluginAutomaticUpdates = false, AllowUnstablePluginReleases = unstable };
                var vm = new DllManagerVM(manager, settings, dialogue);
                await vm.Refresh();
                manager.SetEnabled(manager.Releases.Single(release => release.Name == "ServerPlugin"), true);
                handler.Json = JsonSerializer.Serialize(new[] {
                    Release("UnchainedPlugin"), Release("UnchainedPlugin", "2.0.0"), Release("UnchainedPlugin", "3.0.0-RC1"),
                    Release("ServerPlugin", target: "server"), Release("ServerPlugin", "2.0.0", target: "server"),
                    Release("DisabledPlugin"), Release("DisabledPlugin", "2.0.0")
                });
                await vm.UpdatePlugins();
                Assert.Equal(2, dialogue.Updates.Count);
                var newest = unstable ? "3.0.0-RC1" : "2.0.0";
                Assert.Equal(newest, dialogue.Updates.Single(update => update.Name == "UnchainedPlugin tools").LatestVersion);
                Assert.All(dialogue.Updates, update => Assert.Contains("/releases/tag/v", update.ReleaseUrl));
                Assert.Equal(approve ? newest : "1.0.0", manager.Releases.Single(release => release.Name == "UnchainedPlugin" && manager.IsEnabled(release)).Version);
                Assert.Equal(approve ? "2.0.0" : "1.0.0", manager.Releases.Single(release => release.Name == "ServerPlugin" && manager.IsEnabled(release)).Version);
                Assert.DoesNotContain(manager.Releases.Where(manager.IsEnabled), release => release.Name == "DisabledPlugin");
                Assert.False(settings.EnablePluginAutomaticUpdates);
                Assert.DoesNotContain(handler.Requests, url => url.EndsWith(".dll"));
                dialogue.Updates.Clear();
                vm.SearchTerm = "not found";
                await vm.UpdatePlugins();
                Assert.Empty(dialogue.Updates);
                Assert.Equal("No plugin updates available.", vm.Status);
                handler.Fail = true;
                await vm.UpdatePlugins();
                Assert.StartsWith("Could not update plugins:", vm.Status);
                Assert.Equal(vm.Status, Assert.Single(dialogue.Messages));
            }
            finally { Directory.Delete(directory, true); }
        }

        private sealed class IndexHandler(string json) : HttpMessageHandler {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsoluteUri == DllManager.PackageListUrl
                    ? System.Net.HttpStatusCode.NotFound : System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(json) });
        }
    }
}
