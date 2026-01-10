namespace UnchainedLauncher.Core.Utilities {
    public static class ArgumentEscaper {
        public static string Escape(string argument) {
            var escaped = argument
                .Replace("\"", "\\\"")
                .ReplaceLineEndings("\\r\\n");

            // Quote if contains spaces or is empty
            if (string.IsNullOrEmpty(argument) || argument.Contains(' ')) {
                return $"\"{escaped}\"";
            }

            return escaped;
        }
    }
}