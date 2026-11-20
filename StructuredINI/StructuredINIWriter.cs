using System.Reflection;
using System.Text;

namespace StructuredINI {
    public class StructuredINIWriter {
        private readonly StructuredINIParser _parser = new();

        private readonly List<string> _sectionOrder = new();
        private readonly Dictionary<string, List<string>> _bufferedSections = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ParsedIni> _documentTrivia = new();

        // Import only document trivia; effective keys still come from the buffered patch.
        public void BufferDocumentTrivia(string content) => _documentTrivia.Add(ParseIni(SplitLines(content).ToArray()));

        // Buffer only caller-selected keys; WriteOut preserves all other keys.
        public void BufferSection(string sectionName, IEnumerable<KeyValuePair<string, string>> entries) {
            if (!_bufferedSections.ContainsKey(sectionName)) _sectionOrder.Add(sectionName);
            _bufferedSections[sectionName] = entries.SelectMany(entry => SplitLines($"{entry.Key}={entry.Value}")).ToList();
        }

        // Merge caller-selected keys into a pending section; later values replace only those keys.
        public void MergeSection(string sectionName, IEnumerable<KeyValuePair<string, string>> entries) {
            var lines = entries.Select(entry => $"{entry.Key}={entry.Value}").ToList();
            MergeSectionLines(sectionName, lines);
        }

        private void MergeSectionLines(string sectionName, List<string> lines) {
            if (!_bufferedSections.TryGetValue(sectionName, out var buffered)) {
                _sectionOrder.Add(sectionName);
                _bufferedSections[sectionName] = lines.SelectMany(SplitLines).ToList();
                return;
            }

            PatchSection(buffered, lines);
            _bufferedSections[sectionName] = buffered.SelectMany(SplitLines).ToList();
        }

        public static bool Save<T>(string path, T t, bool isConfigRoot) {
            var writer = new StructuredINIWriter();
            return writer.BufferWrite(t) && writer.WriteOut(path, isConfigRoot);
        }

        public bool BufferWrite<T>(T iniSection) => BufferWrite(iniSection, merge: false);

        public bool BufferWrite<T>(T iniSection, bool merge) {
            if (iniSection == null) return false;

            var type = iniSection.GetType();
            if (type.GetCustomAttribute<INIFileAttribute>() != null) {
                return BufferWriteFileObject(iniSection, merge);
            }

            var ini = _parser.Serialize(iniSection, isConfigRoot: false);

            // Extract the section name from the first line: [SectionName]
            // Serialize always writes the header first.
            var headerEnd = ini.IndexOf(']');
            if (!ini.StartsWith("[") || headerEnd <= 1) {
                return false;
            }

            var sectionName = ini.Substring(1, headerEnd - 1);
            var lines = SplitLines(ini).Skip(1).ToList();

            if (merge) {
                while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                MergeSectionLines(sectionName, lines);
                return true;
            }

            if (!_bufferedSections.ContainsKey(sectionName)) {
                _sectionOrder.Add(sectionName);
            }

            _bufferedSections[sectionName] = lines;
            return true;
        }

        private bool BufferWriteFileObject(object iniFile, bool merge) {
            var fileType = iniFile.GetType();
            var props = fileType
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.PropertyType.GetCustomAttribute<INISectionAttribute>() != null)
                .OrderBy(p => p.MetadataToken)
                .ToList();

            if (props.Count == 0) return false;

            var any = false;
            foreach (var prop in props) {
                var sectionObj = prop.GetValue(iniFile);
                if (sectionObj == null) continue;
                any |= BufferWrite((dynamic)sectionObj, merge);
            }

            return any;
        }

        public bool WriteOut(string path, bool isConfigRoot) {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (_sectionOrder.Count == 0 && _documentTrivia.Count == 0) return false;

            var parsed = File.Exists(path) ? ParseIni(File.ReadAllLines(path)) : new ParsedIni();
            foreach (var trivia in _documentTrivia) {
                MergeTrivia(parsed.PreambleLines, trivia.PreambleLines);
                foreach (var sectionName in trivia.Order) {
                    if (!parsed.Sections.TryGetValue(sectionName, out var section)) {
                        parsed.Order.Add(sectionName);
                        parsed.Sections[sectionName] = section = new IniSection(sectionName, new List<string>());
                    }
                    MergeTrivia(section.BodyLines, trivia.Sections[sectionName].BodyLines);
                }
            }
            foreach (var sectionName in _sectionOrder) {
                var bodyLines = _bufferedSections[sectionName];
                if (parsed.Sections.TryGetValue(sectionName, out var existingSection)) {
                    PatchSection(existingSection.BodyLines, bodyLines);
                }
                else {
                    parsed.Order.Add(sectionName);
                    parsed.Sections[sectionName] = new IniSection(sectionName, bodyLines);
                }
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }

            var sb = new StringBuilder();
            WriteIni(sb, parsed, isConfigRoot);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }

        public static string SerializeDocument(string content, bool isConfigRoot) {
            if (!isConfigRoot) return content;
            var resolved = ParseIni(SplitLines(content).ToArray());
            var root = new StringBuilder();
            WriteIni(root, resolved, isConfigRoot: true);
            return root.ToString();
        }

        // Resolve commands before writing standalone configs; never just strip prefixes.
        private static List<string> ResolveRootArrays(List<string> lines) {
            var operated = lines.Where(line => {
                var trimmed = line.Trim();
                return trimmed.Length > 0 && "!+.-".Contains(trimmed[0]) && TryExtractKey(line) != null;
            }).Select(line => TryExtractKey(line)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var values = operated.ToDictionary(key => key, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines) {
                var key = TryExtractKey(line);
                if (key == null || !values.TryGetValue(key, out var items)) continue;
                var trimmed = line.Trim();
                var value = trimmed[(trimmed.IndexOf('=') + 1)..].Trim();
                switch (trimmed[0]) {
                    case '!': items.Clear(); break;
                    case '-': items.Remove(value); break;
                    case '+': if (!items.Contains(value, StringComparer.Ordinal)) items.Add(value); break;
                    default: items.Add(value); break;
                }
            }
            var result = new List<string>();
            foreach (var line in lines) {
                var key = TryExtractKey(line);
                if (key == null || !operated.Contains(key)) { result.Add(line); continue; }
                if (values.Remove(key, out var items))
                    result.AddRange(items.Select(value => $"{key}={value}"));
            }
            return result;
        }

        private static List<string> SplitLines(string text) {
            if (text is null) return new List<string>();
            return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
        }

        private static string? TryExtractKey(string line) {
            if (line == null) return null;
            var trimmed = line.Trim();
            if (trimmed.Length == 0) return null;
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) return null;
            if (trimmed.StartsWith(";") || trimmed.StartsWith("#")) return null;

            // Support Unreal-style array operations with prefixes: ! + . -
            var prefix = trimmed[0];
            if (prefix == '!' || prefix == '+' || prefix == '.' || prefix == '-') {
                trimmed = trimmed.Substring(1);
            }

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) return null;
            return trimmed.Substring(0, eq).Trim();
        }

        private static void PatchSection(List<string> existing, List<string> newBodyLines) {
            var keysToReplace = newBodyLines.Select(TryExtractKey).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (keysToReplace.Count > 0) {
                existing.RemoveAll(l => {
                    var key = TryExtractKey(l);
                    return key != null && keysToReplace.Contains(key);
                });
            }

            // Insert before trailing blank lines to keep section spacing.
            var insertAt = existing.Count;
            while (insertAt > 0 && string.IsNullOrWhiteSpace(existing[insertAt - 1])) {
                insertAt--;
            }

            existing.InsertRange(insertAt, newBodyLines);
        }

        private static void MergeTrivia(List<string> existing, List<string> imported) {
            var missing = imported.Where(line => TryExtractKey(line) == null && !existing.Contains(line, StringComparer.Ordinal)).ToArray();
            existing.AddRange(missing);
        }

        private static IEnumerable<string> NormalizeBlankLines(List<string> lines) {
            var written = false;
            var pendingBlank = false;
            foreach (var line in lines) {
                if (string.IsNullOrWhiteSpace(line)) {
                    pendingBlank = written;
                    continue;
                }
                if (pendingBlank) yield return "";
                yield return line;
                written = true;
                pendingBlank = false;
            }
        }

        private static void WriteIni(StringBuilder sb, ParsedIni ini, bool isConfigRoot) {
            // Preserve leading (non-section) lines.
            foreach (var line in NormalizeBlankLines(ini.PreambleLines)) {
                sb.AppendLine(line);
            }

            foreach (var sectionName in ini.Order) {
                var section = ini.Sections[sectionName];

                sb.AppendLine($"[{section.Name}]");
                var bodyLines = isConfigRoot ? ResolveRootArrays(section.BodyLines) : section.BodyLines;
                foreach (var line in NormalizeBlankLines(bodyLines)) {
                    sb.AppendLine(line);
                }
                sb.AppendLine();
            }
        }

        private static ParsedIni ParseIni(string[] lines) {
            var parsed = new ParsedIni();
            IniSection? current = null;

            foreach (var rawLine in lines) {
                var line = rawLine ?? string.Empty;
                var trimmed = line.Trim();

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.Length >= 3) {
                    var sectionName = trimmed.Substring(1, trimmed.Length - 2);
                    // Repeated sections append to the first occurrence.
                    if (!parsed.Sections.TryGetValue(sectionName, out current)) {
                        current = new IniSection(sectionName, new List<string>());
                        parsed.Sections[sectionName] = current;
                        parsed.Order.Add(sectionName);
                    }
                    continue;
                }

                if (current == null) {
                    parsed.PreambleLines.Add(line);
                }
                else {
                    current.BodyLines.Add(line);
                }
            }

            return parsed;
        }

        private sealed class ParsedIni {
            public List<string> PreambleLines { get; } = new();
            public List<string> Order { get; } = new();
            public Dictionary<string, IniSection> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class IniSection {
            public string Name { get; }
            public List<string> BodyLines { get; }

            public IniSection(string name, List<string> bodyLines) {
                Name = name;
                BodyLines = bodyLines;
            }
        }
    }
}
