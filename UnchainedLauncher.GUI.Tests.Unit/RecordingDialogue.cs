using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Tests.Unit;

internal sealed class RecordingDialogue(bool? approveUpdates = null) : IUserDialogueSpawner {
    public List<string> Messages { get; } = [];
    public List<DependencyUpdate> Updates { get; } = [];

    public void DisplayMessage(string message) => Messages.Add(message);

    public UserDialogueChoice DisplayYesNoMessage(string message, string caption) =>
        throw new NotSupportedException("Unexpected yes/no prompt.");

    public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
        string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) {
        if (approveUpdates == null) throw new NotSupportedException("Unexpected update prompt.");
        Updates.AddRange(updates);
        return approveUpdates.Value ? UserDialogueChoice.Yes : UserDialogueChoice.No;
    }

    public Action DisplayProgress(MemoryProgress progress) => throw new NotSupportedException("Unexpected progress window.");
}
