using System.CommandLine;

namespace UnchainedLauncher.Core.Services.Processes;

/// <summary>Process-local command-line permission, never a persisted setting.</summary>
public static class UnsafeDllLoading {
    public const string FlagName = "--unsafe-very-dangerous-load-all-dlls";

    public static Option<bool> CreateOption(bool recursive = false) {
        var option = new Option<bool>(FlagName) { Hidden = true, Arity = ArgumentArity.Zero, Recursive = recursive };
        return option;
    }

    public static bool Read(ParseResult parsed, Option<bool> option, IReadOnlyList<string> commandLine) {
        if (parsed.GetResult(option) is not { } result) return false;
        if (result.IdentifierTokenCount == 0) return false;
        // System.CommandLine tokenizes --flag=true as --flag plus a positional
        // token. Permission requires the original literal flag, not that form.
        if (!commandLine.Contains(FlagName, StringComparer.Ordinal) || result.IdentifierTokenCount != 1 || result.Tokens.Count != 0)
            throw new ArgumentException($"{FlagName} must be supplied once as a value-less command-line flag.");
        var errors = parsed.Errors.Where(error => error.SymbolResult == result).Select(error => error.Message).ToArray();
        if (errors.Length != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
        return parsed.GetValue(option);
    }

    public static (bool Enabled, string[] RemainingArguments) ParseGuiArguments(string[] args) {
        var separator = Array.IndexOf(args, "--");
        var prefix = separator < 0 ? args : args[..separator];
        var root = new RootCommand { TreatUnmatchedTokensAsErrors = false };
        var option = CreateOption();
        root.Options.Add(option);
        var parsed = root.Parse(prefix, new ParserConfiguration { ResponseFileTokenReplacer = null });
        var enabled = Read(parsed, option, prefix);
        // Only a successfully parsed, exact permission token is consumed; game/auth
        // arguments (including those after --) remain byte-for-byte unchanged.
        var remaining = enabled ? prefix.Where(argument => argument != FlagName) : prefix;
        return (enabled, remaining.Concat(separator < 0 ? [] : args[separator..]).ToArray());
    }
}