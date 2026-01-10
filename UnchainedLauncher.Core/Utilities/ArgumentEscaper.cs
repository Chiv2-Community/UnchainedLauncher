namespace UnchainedLauncher.Core.Utilities {
    public static class ArgumentEscaper {
        public static string Escape(string argument) {
            return
                argument
                    .Replace("\"", "\\\"")
                    .ReplaceLineEndings("\\r\\n");
        }
    }
}