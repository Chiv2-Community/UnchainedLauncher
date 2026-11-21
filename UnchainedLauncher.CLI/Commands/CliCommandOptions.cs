namespace UnchainedLauncher.CLI.Commands;

public abstract class CliCommandOptions {
    public bool Help { get; protected init; }
    public string HelpText { get; protected init; } = "";
}