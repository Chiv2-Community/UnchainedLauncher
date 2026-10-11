using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.CLI;

// The CLI is non-interactive during preparation: repair bad hashes, accept the
// explicitly enabled plugin update policy, but never continue a failed install.
public sealed class ConsoleDialogue : IUserDialogueSpawner {
    public void DisplayMessage(string message) => Console.Error.WriteLine(message);

    public UserDialogueChoice DisplayYesNoMessage(string message, string caption) {
        DisplayMessage($"{caption}: {message}");
        return caption == "Refresh pak file?" ? UserDialogueChoice.Yes : UserDialogueChoice.No;
    }

    public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText,
        string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) {
        foreach (var update in updates) Console.Error.WriteLine($"Updating {update.Name} to {update.LatestVersion}.");
        return UserDialogueChoice.Yes;
    }

    public Action DisplayProgress(MemoryProgress progress) {
        Console.Error.WriteLine("Preparing mod files...");
        return () => { };
    }
}