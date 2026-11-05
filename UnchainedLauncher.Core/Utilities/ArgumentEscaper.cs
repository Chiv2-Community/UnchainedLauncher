namespace UnchainedLauncher.Core.Utilities {
    public static class ArgumentEscaper {
        // Keep the raw command-line contracts used by Core and GUI, but delegate argv quoting
        // to CliWrap's .NET-derived implementation (including backslashes, quotes and whitespace).
        public static string Escape(string argument) => CliWrap.Builders.ArgumentsBuilder.Escape(argument);
    }
}