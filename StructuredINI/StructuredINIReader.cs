using System.Reflection;

namespace StructuredINI {
    public class StructuredINIReader {
        private readonly StructuredINIParser _parser = new();

        private readonly Dictionary<string, string> _sectionBuffers = new(StringComparer.OrdinalIgnoreCase);

        public static T LoadOrDefault<T>(string path, T fallback) {
            try {
                var reader = new StructuredINIReader();

                if (!reader.Load(path)) {
                    return fallback;
                }

                var readValue = reader.Read<T>();
                return readValue == null ? fallback : readValue;
            }
            catch {
                return fallback;
            }
        }
        
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
                var type = typeof(T);
                if (IsINIFile(type)) {
                    if (!TryReadFile(type, out var fileObj)) {
                        return false;
                    }

                    value = (T)fileObj;
                    return true;
                }

                var sectionName = GetSectionName(type);
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

        public T? Read<T>() {
            var type = typeof(T);
            if (IsINIFile(type)) {
                if (!TryReadFile(type, out var fileObj)) {
                    throw new KeyNotFoundException($"INI file model {type.Name} could not be read from the loaded file.");
                }

                return (T)fileObj;
            }

            var sectionName = GetSectionName(type);
            if (sectionName == null) {
                throw new InvalidOperationException($"Type {type.Name} does not have [INISection] attribute.");
            }

            if (!_sectionBuffers.TryGetValue(sectionName, out var sectionContent)) {
                throw new KeyNotFoundException($"INI section [{sectionName}] was not found in the loaded file.");
            }

            return _parser.Deserialize<T>(sectionContent);
        }

        private bool TryReadFile(Type fileType, out object value) {
            value = null!;

            var sectionProperties = GetINISectionProperties(fileType);
            if (sectionProperties.Count == 0) {
                return false;
            }

            var duplicateSectionNames = sectionProperties
                .GroupBy(p => GetSectionName(p.PropertyType), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateSectionNames.Count > 0) {
                return false;
            }

            // Prefer the constructor with the most parameters so records work naturally.
            var ctor = fileType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();

            if (ctor != null) {
                var args = new List<object?>();
                foreach (var param in ctor.GetParameters()) {
                    var prop = sectionProperties.FirstOrDefault(p => string.Equals(p.Name, param.Name, StringComparison.OrdinalIgnoreCase));
                    object? argValue = null;

                    if (prop != null && TryReadSection(prop.PropertyType, out var sectionValue)) {
                        argValue = sectionValue;
                    }
                    else if (param.HasDefaultValue) {
                        argValue = param.DefaultValue;
                    }
                    else if (IsNullableType(param.ParameterType)) {
                        argValue = null;
                    }
                    else if (param.ParameterType.IsValueType) {
                        argValue = Activator.CreateInstance(param.ParameterType);
                    }

                    args.Add(argValue);
                }

                value = ctor.Invoke(args.ToArray());
            }
            else {
                value = Activator.CreateInstance(fileType)!;
            }

            // Populate any writable section properties not covered by ctor.
            foreach (var prop in sectionProperties) {
                if (!prop.CanWrite) continue;
                if (TryReadSection(prop.PropertyType, out var sectionValue)) {
                    prop.SetValue(value, sectionValue);
                }
            }

            return true;
        }

        private bool TryReadSection(Type sectionType, out object sectionValue) {
            sectionValue = null!;
            var sectionName = GetSectionName(sectionType);
            if (sectionName == null) return false;

            if (!_sectionBuffers.TryGetValue(sectionName, out var sectionContent)) {
                return false;
            }

            var method = typeof(StructuredINIParser)
                .GetMethod(nameof(StructuredINIParser.Deserialize))!
                .MakeGenericMethod(sectionType);

            sectionValue = method.Invoke(_parser, new object[] { sectionContent })!;
            return true;
        }

        private static bool IsINIFile(Type type) =>
            type.GetCustomAttribute<INIFileAttribute>() != null;

        private static List<PropertyInfo> GetINISectionProperties(Type fileType) {
            return fileType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => GetSectionName(p.PropertyType) != null)
                .OrderBy(p => p.MetadataToken)
                .ToList();
        }

        private static bool IsNullableType(Type type) {
            if (!type.IsValueType) return true;
            return Nullable.GetUnderlyingType(type) != null;
        }

        private static string? GetSectionName(Type type) {
            var sectionAttr = type.GetCustomAttribute<INISectionAttribute>();
            return sectionAttr?.SectionName;
        }
    }
}