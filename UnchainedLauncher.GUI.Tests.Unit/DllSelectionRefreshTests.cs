using System.Net;
using System.Text.Json;
using LanguageExt;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.GUI.ViewModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class DllSelectionRefreshTests {
    private sealed class Handler : HttpMessageHandler {
        public string Json { get; set; } = "[]";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsoluteUri.EndsWith("plugin_list_index.txt")
                ? HttpStatusCode.NotFound : HttpStatusCode.OK) {
                RequestMessage = request, Content = new StringContent(Json)
            });
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("modified")]
    [InlineData("revoked")]
    public async Task ExistingCardAfterLaunchRefreshRequiresCurrentCompleteApproval(string change) {
        var directory = Path.Combine(Path.GetTempPath(), "dll-refresh-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var release = new DllRelease("Chiv2-Community", "UnchainedPlugin", "1.0.0", "https://example.test/Core.dll",
                new string('a', 64), 123, new PluginInfo("Unchained Core Plugin", "test metadata", ["Author"], ImageUrls: ["https://example.test/image.png"]));
            var handler = new Handler { Json = JsonSerializer.Serialize(new[] { release }) };
            using var http = new HttpClient(handler);
            var manager = new DllManager(Path.Combine(directory, "Plugins"), Path.Combine(directory, "state.json"), http);
            var dialogue = new RecordingDialogue();
            var vm = new DllManagerVM(manager, dialogue: dialogue);
            await vm.Refresh();
            Assert.Single(vm.Plugins).EnableOrDisable(); // Disable the initially enabled core plugin.
            var oldCard = Assert.Single(vm.Plugins);
            Assert.False(oldCard.IsEnabled);
            handler.Json = change switch {
                "modified" => JsonSerializer.Serialize(new[] { release with { Info = release.Info! with { Description = "changed approval" } } }),
                "revoked" => "[]",
                _ => handler.Json
            };
            var preparer = new DllLaunchPreparer(manager, null!); // No enabled plugins: no prompts or downloads.
            var options = new LaunchOptions([], Option<string>.None, "", true, false, Option<string>.None, Option<ServerLaunchOptions>.None);
            Assert.True((await preparer.PrepareLaunch(options)).IsSome);
            if (change != "revoked") Assert.NotSame(oldCard.AvailableVersions[0].Release, Assert.Single(manager.Releases));
            oldCard.EnableOrDisable();
            if (change == "unchanged") {
                Assert.True(oldCard.IsEnabled);
                Assert.Equal("1.0.0", manager.EnabledSelections[DllManager.DefaultPluginId]);
                Assert.Empty(dialogue.Messages);
            }
            else {
                Assert.False(oldCard.IsEnabled);
                Assert.Empty(manager.EnabledSelections);
                Assert.Contains("Unregistered", Assert.Single(dialogue.Messages));
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
