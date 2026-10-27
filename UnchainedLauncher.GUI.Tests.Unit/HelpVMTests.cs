using System.Reflection;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class HelpVMTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CleanupConfirmationsDescribeActualPathsAndRetainedAssets(bool uninstall) {
        var paks = DispatchProxy.Create<IPakDir, ReadOnlyPakDir>();
        var dialogue = new RejectingDialogue();
        var vm = new HelpVM(null!, null!, null!, paks, dialogue,
            _ => throw new InvalidOperationException("Must not exit after rejecting confirmation."));

        foreach (var path in new[] { Path.Combine("confirmation-only", "Saved_unchained", "Paks"), "changed\nprofile\\[unknown]*\rPaks" }) {
            ((ReadOnlyPakDir)paks).Path = path;
            if (uninstall) vm.UninstallLauncherCommand.Execute(null);
            else vm.CleanUpInstallationCommand.Execute(null);

            var intro = uninstall
                ? "Are you sure? This will disable all mods, reset all settings to their defaults, and uninstall the launcher. This will delete the following:"
                : "Are you sure? This will disable all mods and reset all settings to their defaults. This will delete the following:";
            var instruction = !uninstall ? "After deleting, the launcher will restart itself."
                : File.Exists(FilePaths.OriginalLauncherPath) ? "IMPORTANT: Wait at least 1 second and then launch normally."
                : "IMPORTANT: The original launcher is not present. You will need to verify game files after this so it can be re-downloaded.";
            Assert.Equal(string.Join("\n", intro, "* All files in .mod_cache",
                $"* All files in {new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix).Plugins}",
                $"* All non-vanilla files in {path}",
                "Shared caches, other CLI profiles, and legacy installation-wide assets are retained.", "", instruction), dialogue.Messages[^1]);
            Assert.Equal(uninstall ? "Really uninstall?" : "Really clean up installation?", dialogue.Captions[^1]);
        }
        Assert.Equal(2, dialogue.Messages.Count);
    }

    public class ReadOnlyPakDir : DispatchProxy {
        public string Path { get; set; } = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == "get_DirPath"
                ? Path
                : throw new InvalidOperationException($"Unexpected pak operation: {method?.Name}");
    }

    private sealed class RejectingDialogue : IUserDialogueSpawner {
        public List<string> Messages { get; } = [];
        public List<string> Captions { get; } = [];
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) {
            Messages.Add(message);
            Captions.Add(caption);
            return UserDialogueChoice.No;
        }
        public void DisplayMessage(string message) => throw new NotSupportedException();
        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) => throw new NotSupportedException();
        public Action DisplayProgress(MemoryProgress progress) => throw new NotSupportedException();
    }
}
