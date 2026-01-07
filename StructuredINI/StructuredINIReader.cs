using System.Reflection;

namespace StructuredINI {
    public class StructuredINIReader {
        private readonly StructuredINIParser _parser = new();

        private readonly Dictionary<string, string> _sectionBuffers = new(StringComparer.OrdinalIgnoreCase);

        public bool Load(string path) {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try {
                if (!File.Exists(path)) return false;

                var lines = File.ReadAllLines(path);
                _sectionBuffers.Clear();

                string? currentSectionName = null;
                var currentLines = new List<string>();

                void FlushCurrent() {
                    if (string.IsNullOrWhiteSpace(currentSectionName)) {
                        currentLines.Clear();
                        return;
                    }

                    // Use last occurrence if the same section appears multiple times.
                    _sectionBuffers[currentSectionName] = string.Join(Environment.NewLine, currentLines) + Environment.NewLine;
                    currentLines.Clear();
                }

                foreach (var rawLine in lines) {
                    var line = rawLine;
                    var trimmed = line.Trim();

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.Length > 2) {
                        FlushCurrent();

                        currentSectionName = trimmed.Substring(1, trimmed.Length - 2);
                        currentLines.Add($"[{currentSectionName}]");
                        continue;
                    }

                    if (currentSectionName != null) {
                        currentLines.Add(line);
                    }
                }

                FlushCurrent();
                return _sectionBuffers.Count > 0;
            }
            catch {
                return false;
            }
        }

        public bool TryRead<T>(out T value) {
            value = default!;

            try {
                var sectionName = GetSectionName(typeof(T));
                if (sectionName == null) return false;

                if (!_sectionBuffers.TryGetValue(sectionName, out var sectionContent)) {
                    return false;
                }

                value = _parser.Deserialize<T>(sectionContent);
                return true;
            }
            catch {
                return false;
            }
        }

        public T Read<T>() {
            var sectionName = GetSectionName(typeof(T));
            if (sectionName == null) {
                throw new InvalidOperationException($"Type {typeof(T).Name} does not have [INISection] attribute.");
            }

            if (!_sectionBuffers.TryGetValue(sectionName, out var sectionContent)) {
                throw new KeyNotFoundException($"INI section [{sectionName}] was not found in the loaded file.");
            }

            return _parser.Deserialize<T>(sectionContent);
        }

        private static string? GetSectionName(Type type) {
            var sectionAttr = type.GetCustomAttribute<INISectionAttribute>();
            return sectionAttr?.SectionName;
        }
    }
}