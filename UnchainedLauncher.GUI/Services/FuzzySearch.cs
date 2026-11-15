namespace UnchainedLauncher.GUI.Services {
    public static class FuzzySearch {
        // Match ordered characters, ignoring case and separators (e.g. "td ward" matches "TDM_Wardenglade").
        public static bool Matches(string text, string query) {
            var position = 0;
            foreach (var character in query) {
                if (!char.IsLetterOrDigit(character)) continue;
                while (position < text.Length && char.ToUpperInvariant(text[position]) != char.ToUpperInvariant(character)) {
                    position++;
                }
                if (position == text.Length) return false;
                position++;
            }
            return true;
        }
    }
}