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

        if (uninstall) vm.UninstallLauncherCommand.Execute(null);
        else vm.CleanUpInstallationCommand.Execute(null);

        var message = Assert.Single(dialogue.Messages);
        Assert.Contains($"* All files in {new ProfileAssetPaths(ProfileAssetPaths.SharedSuffix).Plugins}", message);
        Assert.Contains($"* All non-vanilla files in {paks.DirPath}", message);
        Assert.Contains("* All files in .mod_cache", message);
        Assert.Contains("Shared caches, other CLI profiles, and legacy installation-wide assets are retained.", message);
        Assert.DoesNotContain("TBL\\Binaries\\Win64\\Plugins", message);
        Assert.DoesNotContain("TBL\\Content\\Paks", message);
    }

    public class ReadOnlyPakDir : DispatchProxy {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == "get_DirPath"
                ? Path.Combine("confirmation-only", "Saved_unchained", "Paks")
                : throw new InvalidOperationException($"Unexpected pak operation: {method?.Name}");
    }

    private sealed class RejectingDialogue : IUserDialogueSpawner {
        public List<string> Messages { get; } = [];
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) {
            Messages.Add(message);
            return UserDialogueChoice.No;
        }
        public void DisplayMessage(string message) => throw new NotSupportedException();
        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) => throw new NotSupportedException();
        public Action DisplayProgress(MemoryProgress progress) => throw new NotSupportedException();
    }
}
