using System.Reflection;
using System.Text;

namespace StructuredINI {
    public class StructuredINIWriter {
        private readonly StructuredINIParser _parser = new();

        private readonly List<string> _sectionOrder = new();
        private readonly Dictionary<string, string> _bufferedSections = new(StringComparer.OrdinalIgnoreCase);

        public static bool Save<T>(string path, T t) {
            var writer = new StructuredINIWriter();
            return writer.BufferWrite(t) && writer.WriteOut(path);
        }

        public bool BufferWrite<T>(T iniSection) {
            if (iniSection == null) return false;

            var type = iniSection.GetType();
            if (type.GetCustomAttribute<INIFileAttribute>() != null) {
                return BufferWriteFileObject(iniSection);
            }

            var ini = _parser.Serialize(iniSection);

            // Extract the section name from the first line: [SectionName]
            // Serialize always writes the header first.
            var headerEnd = ini.IndexOf(']');
            if (!ini.StartsWith("[") || headerEnd <= 1) {
                return false;
            }

            var sectionName = ini.Substring(1, headerEnd - 1);

            if (!_bufferedSections.ContainsKey(sectionName)) {
                _sectionOrder.Add(sectionName);
            }

            _bufferedSections[sectionName] = ini;
            return true;
        }

        private bool BufferWriteFileObject(object iniFile) {
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
                any |= BufferWrite((dynamic)sectionObj);
            }

            return any;
        }

        public bool WriteOut(string path) {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (_sectionOrder.Count == 0) return false;

            var sectionsToWrite = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var keysToReplaceBySection = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var sectionName in _sectionOrder) {
                if (!_bufferedSections.TryGetValue(sectionName, out var sectionText)) {
                    continue;
                }

                var lines = SplitLines(sectionText);
                if (lines.Count == 0) {
                    continue;
                }

                // Strip header line, keep only body lines.
                var bodyLines = lines.Skip(1).ToList();
                sectionsToWrite[sectionName] = bodyLines;
                keysToReplaceBySection[sectionName] = ExtractKeysFromLines(bodyLines);
            }

            var sb = new StringBuilder();
            if (File.Exists(path)) {
                var existingLines = File.ReadAllLines(path);
                var parsed = ParseIni(existingLines);

                // Patch existing sections in-place.
                foreach (var (sectionName, newBodyLines) in sectionsToWrite) {
                    var keysToReplace = keysToReplaceBySection.TryGetValue(sectionName, out var keys)
                        ? keys
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (parsed.Sections.TryGetValue(sectionName, out var existingSection)) {
                        PatchSection(existingSection, keysToReplace, newBodyLines);
                    }
                    else {
                        // Append new section at the end.
                        parsed.Order.Add(sectionName);
                        parsed.Sections[sectionName] = new IniSection(sectionName, newBodyLines.ToList());
                    }
                }

                WriteIni(sb, parsed);
            }
            else {
                // No existing file: write only the buffered sections in insertion order.
                for (int i = 0; i < _sectionOrder.Count; i++) {
                    var sectionName = _sectionOrder[i];
                    if (!sectionsToWrite.TryGetValue(sectionName, out var bodyLines)) {
                        continue;
                    }

                    if (i > 0) {
                        sb.AppendLine();
                    }

                    sb.AppendLine($"[{sectionName}]");
                    foreach (var line in bodyLines) {
                        sb.AppendLine(line);
                    }
                }
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }

        private static List<string> SplitLines(string text) {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        }

        private static HashSet<string> ExtractKeysFromLines(IEnumerable<string> lines) {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines) {
                var key = TryExtractKey(line);
                if (key != null) {
                    keys.Add(key);
                }
            }

            return keys;
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

        private static void PatchSection(IniSection existing, HashSet<string> keysToReplace, List<string> newBodyLines) {
            if (keysToReplace.Count > 0) {
                existing.BodyLines.RemoveAll(l => {
                    var key = TryExtractKey(l);
                    return key != null && keysToReplace.Contains(key);
                });
            }

            // Insert before trailing blank lines to keep section spacing.
            var insertAt = existing.BodyLines.Count;
            while (insertAt > 0 && string.IsNullOrWhiteSpace(existing.BodyLines[insertAt - 1])) {
                insertAt--;
            }

            existing.BodyLines.InsertRange(insertAt, newBodyLines);
        }

        private static void WriteIni(StringBuilder sb, ParsedIni ini) {
            // Preserve leading (non-section) lines.
            foreach (var line in ini.PreambleLines) {
                sb.AppendLine(line);
            }

            var firstSectionWritten = ini.PreambleLines.Count == 0;
            foreach (var sectionName in ini.Order) {
                if (!ini.Sections.TryGetValue(sectionName, out var section)) {
                    continue;
                }

                if (!firstSectionWritten) {
                    firstSectionWritten = true;
                }
                else {
                    // Separate sections with a blank line.
                    if (sb.Length > 0 && !sb.ToString().EndsWith(Environment.NewLine + Environment.NewLine)) {
                        sb.AppendLine();
                    }
                }

                sb.AppendLine($"[{section.Name}]");
                foreach (var line in section.BodyLines) {
                    sb.AppendLine(line);
                }
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
                    if (!parsed.Sections.TryGetValue(sectionName, out current)) {
                        current = new IniSection(sectionName, new List<string>());
                        parsed.Sections[sectionName] = current;
                        parsed.Order.Add(sectionName);
                    }
                    else {
                        // Duplicate sections: last one wins by continuing to append to the existing section.
                        // This matches the reader semantics.
                        // (We keep the first occurrence in order.)
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