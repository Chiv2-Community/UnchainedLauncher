using System.Text;

namespace UnchainedLauncher.CLI;

// Steam's text KeyValues files: libraryfolders, config and compatibilitytool manifests.
internal sealed class SteamKeyValues {
    internal string? Value { get; private init; }
    internal Dictionary<string, SteamKeyValues> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal SteamKeyValues? At(params string[] keys) {
        SteamKeyValues? node = this;
        foreach (var key in keys) node = node?.Children.GetValueOrDefault(key);
        return node;
    }

    internal static SteamKeyValues Read(string path) => Parse(File.ReadAllText(path));

    internal static SteamKeyValues Parse(string text) {
        var position = 0;
        string? Token() {
            while (position < text.Length) {
                if (char.IsWhiteSpace(text[position])) { position++; continue; }
                if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/') {
                    while (position < text.Length && text[position] != '\n') position++;
                    continue;
                }
                break;
            }
            if (position == text.Length) return null;
            var first = text[position++];
            if (first is '{' or '}') return first.ToString();
            var value = new StringBuilder();
            if (first == '"') {
                while (position < text.Length) {
                    var c = text[position++];
                    if (c == '"') return value.ToString();
                    if (c == '\\' && position < text.Length && text[position] is '\\' or '"') c = text[position++];
                    value.Append(c);
                }
                throw new InvalidDataException("Unterminated Steam KeyValues string.");
            }
            value.Append(first);
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}'))
                value.Append(text[position++]);
            return value.ToString();
        }

        SteamKeyValues Object(bool nested) {
            var result = new SteamKeyValues();
            while (Token() is { } key) {
                if (key == "}" && nested) return result;
                if (key is "{" or "}") throw new InvalidDataException("Invalid Steam KeyValues object.");
                var value = Token() ?? throw new InvalidDataException("Missing Steam KeyValues value.");
                result.Children[key] = value == "{" ? Object(true) : value != "}"
                    ? new SteamKeyValues { Value = value } : throw new InvalidDataException("Missing Steam KeyValues value.");
            }
            if (nested) throw new InvalidDataException("Unterminated Steam KeyValues object.");
            return result;
        }
        return Object(false);
    }
}