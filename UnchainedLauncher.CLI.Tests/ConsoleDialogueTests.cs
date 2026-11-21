using UnchainedLauncher.CLI.Terminal;
using UnchainedLauncher.Core.Services;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class ConsoleDialogueTests {
    private static readonly DependencyUpdate[] Files = [
        new("001-known-full-name.pak", null, "v1.2.3", "https://example.test/release", "Known release"),
        new("unknown with spaces.pak", null, "Unknown", "", "Unrecognized pak")
    ];

    private static UserDialogueChoice? Recover(ConsoleDialogue dialogue) =>
        dialogue.DisplayUpdateMessage("Unmanaged Mods detected", "Keep them or remove them?",
            "Keep", "Remove", "Cancel", Files);

    [Theory]
    [InlineData("Keep\n", UserDialogueChoice.Yes)]
    [InlineData("Remove\n", UserDialogueChoice.No)]
    [InlineData("Cancel\n", UserDialogueChoice.Cancel)]
    [InlineData("  kEeP  \n", UserDialogueChoice.Yes)]
    [InlineData("", UserDialogueChoice.Cancel)]
    [InlineData("\nYes\nNo\n1\nRemove\n", UserDialogueChoice.No)]
    [InlineData("invalid\n", UserDialogueChoice.Cancel)]
    public void RecoveryRequiresAnExplicitChoice(string input, UserDialogueChoice expected) {
        var output = new StringWriter();
        var dialogue = new ConsoleDialogue(new StringReader(input), output, () => true);
        Assert.Equal(expected, Recover(dialogue));
        Assert.Contains("Choose Keep / Remove / Cancel:", output.ToString());
        if (input.Contains("invalid") || input.StartsWith('\n'))
            Assert.Contains("No default choice.", output.ToString());
        if (input == "" || input == "invalid\n")
            Assert.Contains("End of input; recovery cancelled.", output.ToString());
    }

    [Fact]
    public void RecoveryShowsEveryFilenameAndExplainsConsequences() {
        var output = new StringWriter();
        var dialogue = new ConsoleDialogue(new StringReader("Cancel\n"), output, () => true);
        Assert.Equal(UserDialogueChoice.Cancel, Recover(dialogue));
        var text = output.ToString();
        foreach (var file in Files) {
            Assert.Contains(file.Name, text);
            Assert.Contains(file.LatestVersion, text);
            Assert.Contains(file.Reason, text);
        }
        Assert.Contains(Files[0].ReleaseUrl, text);
        Assert.Contains("command-selected profile's Paks directory", text);
        Assert.Contains("adopting recognized releases as managed files", text);
        Assert.Contains("replaced or removed", text);
        Assert.Contains("does not add CLI --mod selections", text);
        Assert.Contains("deletion of the listed pak files and their matching .sig files", text);
    }

    [Fact]
    public void HeadlessRecoveryDoesNotReadEvenPipedConsent() {
        var output = new StringWriter();
        var dialogue = new ConsoleDialogue(new UnreadableInput(), output, () => false);
        Assert.Equal(UserDialogueChoice.Cancel, Recover(dialogue));
        var text = output.ToString();
        Assert.Contains(Files[0].Name, text);
        Assert.Contains(Files[1].Name, text);
        Assert.Contains("Rerun the same launch command", text);
        Assert.Contains("--saved-dir-suffix", text);
        Assert.Contains("without redirecting stdin, stdout or stderr", text);
        Assert.DoesNotContain("Choose Keep / Remove / Cancel:", text);
    }

    [Fact]
    public void NormalUpdateAndRefreshPoliciesRemainUnchanged() {
        var output = new StringWriter();
        var dialogue = new ConsoleDialogue(new UnreadableInput(), output, () => false);
        Assert.Equal(UserDialogueChoice.Yes, dialogue.DisplayUpdateMessage("Update plugin", "Update?",
            "Yes", "No", "Cancel", Files));
        Assert.Contains($"Updating {Files[0].Name} to v1.2.3.", output.ToString());
        Assert.Equal(UserDialogueChoice.Yes, dialogue.DisplayYesNoMessage("Bad hash", "Refresh pak file?"));
        Assert.Equal(UserDialogueChoice.No, dialogue.DisplayYesNoMessage("Failed install", "Continue?"));
    }

    private sealed class UnreadableInput : TextReader {
        public override string? ReadLine() => throw new InvalidOperationException("Must not read redirected input.");
    }
}