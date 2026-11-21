using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.CLI;

// Normal updates retain their existing policy; file recovery requires consent.
public sealed class ConsoleDialogue : IUserDialogueSpawner {
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly Func<bool> _isInteractive;

    public ConsoleDialogue() : this(Console.In, Console.Error,
        () => !Console.IsInputRedirected && !Console.IsOutputRedirected && !Console.IsErrorRedirected) { }

    internal ConsoleDialogue(TextReader input, TextWriter output, Func<bool> isInteractive) {
        _input = input;
        _output = output;
        _isInteractive = isInteractive;
    }

    public void DisplayMessage(string message) => _output.WriteLine(message);

    public UserDialogueChoice DisplayYesNoMessage(string message, string caption) {
        DisplayMessage($"{caption}: {message}");
        return caption == "Refresh pak file?" ? UserDialogueChoice.Yes : UserDialogueChoice.No;
    }

    public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText,
        string yesButtonText, string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates) {
        if (yesButtonText == "Keep" && noButtonText == "Remove") {
            return RecoverFiles(titleText, messageText, updates);
        }
        foreach (var update in updates) DisplayMessage($"Updating {update.Name} to {update.LatestVersion}.");
        return UserDialogueChoice.Yes;
    }

    private UserDialogueChoice RecoverFiles(string title, string message, IEnumerable<DependencyUpdate> updates) {
        DisplayMessage($"{title}: {message}");
        DisplayMessage("Affected files in the command-selected profile's Paks directory (filenames relative to that directory):");
        foreach (var update in updates)
            DisplayMessage($"  {update.Name} | version: {update.LatestVersion} | {update.Reason}"
                + (string.IsNullOrEmpty(update.ReleaseUrl) ? "" : $" | {update.ReleaseUrl}"));
        DisplayMessage("Keep: authorize recovery, retaining unrecognized files and adopting recognized releases as managed files.");
        DisplayMessage("Adopted files may later be replaced or removed to match this command's requested mods; Keep does not add CLI --mod selections.");
        DisplayMessage("Remove: authorize deletion of the listed pak files and their matching .sig files.");
        DisplayMessage("Cancel: stop preparation without authorizing recovery.");
        if (!_isInteractive()) {
            DisplayMessage("Recovery cancelled; no recovery was authorized. Rerun the same launch command with the same profile"
                + " (--saved-dir-suffix, if supplied) in an interactive terminal, without redirecting stdin, stdout or stderr,"
                + " then choose Keep, Remove or Cancel for the files above.");
            return UserDialogueChoice.Cancel;
        }
        while (true) {
            _output.Write("Choose Keep / Remove / Cancel: ");
            _output.Flush();
            var answer = _input.ReadLine();
            if (answer == null) {
                DisplayMessage("End of input; recovery cancelled.");
                return UserDialogueChoice.Cancel;
            }
            switch (answer.Trim().ToLowerInvariant()) {
                case "keep": return UserDialogueChoice.Yes;
                case "remove": return UserDialogueChoice.No;
                case "cancel": return UserDialogueChoice.Cancel;
                default: DisplayMessage("Enter Keep, Remove or Cancel. No default choice."); break;
            }
        }
    }

    public Action DisplayProgress(MemoryProgress progress) {
        DisplayMessage("Preparing mod files...");
        return () => { };
    }
}
