using System.Collections;
using System.Reflection;

using System.Text;

namespace StructuredINI {
    public class StructuredINIParser {
        public sealed record Entry(string Section, string Key, string Value) {
            public string Name => Key.TrimStart('!', '+', '-', '.');
        }

        // Read unknown sections and fields without requiring a CLR model.
        public IReadOnlyList<Entry> ReadEntries(string content, bool strict = false) {
            var entries = new List<Entry>();
            var section = "";
            foreach (var original in content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)) {
                var line = original.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']')) {
                    section = line[1..^1];
                    continue;
                }
                var equals = line.IndexOf('=');
                if (section.Length == 0 || equals <= 0 || line.Contains('\0')) {
                    if (strict) throw new FormatException("Invalid INI structure.");
                    continue;
                }
                var entry = new Entry(section, line[..equals].Trim(), line[(equals + 1)..]);
                if (entry.Name.Length == 0) {
                    if (strict) throw new FormatException("Invalid INI key.");
                    continue;
                }
                entries.Add(entry);
            }
            return entries;
        }

        // Values are already encoded by StructuredINI codecs. Use the same collection
        // representation for model-based serialization and dynamically selected fields.
        public static bool IsArrayDelta(IEnumerable<KeyValuePair<string, string>> entries) {
            var commands = entries.ToArray();
            return commands.Length > 0 && commands.All(entry => entry.Key.Length > 0 && entry.Key[0] is '+' or '-' or '.');
        }

        public IEnumerable<KeyValuePair<string, string>> SerializeEntries(string key, IEnumerable<string> values, bool collection, bool isConfigRoot, char operation = '\0') {
            var encoded = values.ToArray();
            if (isConfigRoot && operation != '\0')
                throw new ArgumentException("Root array operations must be resolved before serializing entries.");
            if (operation != '\0') {
                if (!collection || operation is not ('+' or '-' or '.' or '!'))
                    throw new ArgumentException("INI array operations require a collection and one of +, -, ., or !.");
                if (operation == '!') {
                    if (encoded.Length != 0) throw new ArgumentException("The ! array operation requires an empty list.");
                    yield return new("!" + key, "ClearArray");
                }
                else foreach (var value in encoded) yield return new(operation + key, value);
                yield break;
            }
            if (!collection || isConfigRoot) {
                foreach (var value in encoded) yield return new(key, value);
                yield break;
            }
            // A supplied collection replaces inherited values, including when empty.
            yield return new("!" + key, "ClearArray");
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in encoded)
                yield return new((emitted.Add(value) ? "+" : ".") + key, value);
        }

        public string Serialize<T>(T instance, bool isConfigRoot) {
            var type = typeof(T);
            var sectionAttr = type.GetCustomAttribute<INISectionAttribute>();
            if (sectionAttr == null) {
                throw new InvalidOperationException($"Type {type.Name} does not have [INISection] attribute.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[{sectionAttr.SectionName}]");

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties) {
                var val = prop.GetValue(instance);
                if (val == null) continue;

                var keyAttr = prop.GetCustomAttribute<INIKeyAttribute>();
                var keyName = keyAttr?.KeyName ?? prop.Name;

                if (IsCollection(prop.PropertyType, out var elementType)) {
                    var list = (IList)val;
                    foreach (var entry in SerializeEntries(keyName, list.Cast<object>().Select(item => EncodeValue(elementType, item)), collection: true, isConfigRoot: isConfigRoot))
                        sb.AppendLine($"{entry.Key}={entry.Value}");
                }
                else {
                    foreach (var entry in SerializeEntries(keyName, new[] { EncodeValue(prop.PropertyType, val) }, collection: false, isConfigRoot: isConfigRoot))
                        sb.AppendLine($"{entry.Key}={entry.Value}");
                }
            }

            return sb.ToString();
        }

        private static string EncodeValue(Type type, object value) {
            var codec = CodecRegistry.Get(type);
            try {
                return (string)codec.GetType().GetMethod("Encode")!.Invoke(codec, new[] { value })!;
            }
            catch (Exception ex) {
                throw new InvalidOperationException($"Failed to encode value of type {type.Name}", ex);
            }
        }

        public T Deserialize<T>(string iniContent) {
            var type = typeof(T);
            var sectionAttr = type.GetCustomAttribute<INISectionAttribute>();
            if (sectionAttr == null) {
                throw new InvalidOperationException($"Type {type.Name} does not have [INISection] attribute.");
            }

            var sectionName = sectionAttr.SectionName;

            // Prepare storage for property values
            // For arrays/lists, we store the List<ElementType> object.
            // For scalars, we store the value directly.
            var propertyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propMap = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in properties) {
                var keyAttr = prop.GetCustomAttribute<INIKeyAttribute>();
                var keyName = keyAttr?.KeyName ?? prop.Name;
                propMap[keyName] = prop;
            }

            // Initialize collections
            foreach (var prop in properties) {
                if (IsCollection(prop.PropertyType, out var elementType)) {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    propertyValues[prop.Name] = Activator.CreateInstance(listType);
                }
            }

            foreach (var entry in ReadEntries(iniContent).Where(entry => entry.Section.Equals(sectionName, StringComparison.OrdinalIgnoreCase))) {
                ProcessLine(entry, propMap, propertyValues);
            }

            return CreateInstance<T>(propertyValues, properties);
        }

        private void ProcessLine(Entry entry, Dictionary<string, PropertyInfo> propMap, Dictionary<string, object> propertyValues) {
            // Syntax: [Prefix]Key=Value
            // Prefixes: !, +, -, .
            // If no prefix, it's Set.

            string keyPart = entry.Key;
            string valuePart = entry.Value.Trim();

            char prefix = '\0';
            string key = keyPart;

            if (keyPart.Length > 0 && IsPrefix(keyPart[0])) {
                prefix = keyPart[0];
                key = keyPart.Substring(1);
            }

            if (!propMap.TryGetValue(key, out var prop)) {
                return; // Property not found, ignore
            }

            if (IsCollection(prop.PropertyType, out var elementType)) {
                HandleCollectionOperation(prefix, key, valuePart, elementType, propertyValues[prop.Name]);
            }
            else {
                HandleScalarOperation(prefix, key, prop, valuePart, propertyValues);
            }
        }

        private bool IsPrefix(char c) {
            return c == '!' || c == '+' || c == '-' || c == '.';
        }

        private bool IsCollection(Type type, out Type elementType) {
            if (type.IsArray) {
                elementType = type.GetElementType();
                return true;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
            elementType = null;
            return false;
        }

        private void HandleScalarOperation(char prefix, string iniKey, PropertyInfo prop, string valueStr, Dictionary<string, object> values) {
            // Scalars mostly support Set (=).
            // Maybe ! to clear (null)?

            if (prefix == '\0') {
                try {
                    var codec = CodecRegistry.Get(prop.PropertyType) as dynamic;
                    var value = codec.Decode(valueStr);
                    values[prop.Name] = value;
                }
                catch (KeyNotFoundException ex) {
                    throw new InvalidOperationException(
                        $"Missing codec while parsing INI key '{iniKey}' (property '{prop.Name}', type {TypeNameFormatter.Format(prop.PropertyType)}).",
                        ex);
                }
            }
            // Ignore other prefixes for scalars for now or implement if needed
        }

        private void HandleCollectionOperation(char prefix, string key, string valueStr, Type elementType, object listObj) {
            var list = (IList)listObj;

            // valueStr might be "ClearArray" for !
            // But ! usually just clears.

            if (prefix == '!') {
                list.Clear();
                return;
            }

            // Decode value
            dynamic codec;
            try {
                codec = CodecRegistry.Get(elementType) as dynamic;
            }
            catch (KeyNotFoundException ex) {
                throw new InvalidOperationException(
                    $"Missing codec while parsing INI key '{key}' (collection element type {TypeNameFormatter.Format(elementType)}).",
                    ex);
            }
            // The codec interface is generic, so we use dynamic dispatch or reflection invoke.
            // But CodecRegistry.Get(Type) returns object.
            // We can cast to IINICodec<T> but we don't know T at compile time easily here without MakeGenericType.
            // Using dynamic is easiest here.

            object val = null;
            try {
                // We must invoke Decode dynamically
                var decodeMethod = codec.GetType().GetMethod("Decode");
                val = decodeMethod.Invoke(codec, new object[] { valueStr });
            }
            catch (Exception ex) {
                // handle error
                throw new InvalidOperationException($"Failed to decode value '{valueStr}' for type {elementType.Name}", ex);
            }

            switch (prefix) {
                case '\0': // Set: Clear then Append
                    list.Add(val);
                    break;
                case '+': // AddUnique
                    if (!list.Contains(val)) {
                        list.Add(val);
                    }
                    break;
                case '.': // Add (Allow Duplicate)
                    list.Add(val);
                    break;
                case '-': // Remove
                    list.Remove(val);
                    break;
            }
        }

        private T CreateInstance<T>(Dictionary<string, object> propertyValues, PropertyInfo[] properties) {
            // Try to find a constructor that matches properties
            var ctor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();

            if (ctor != null) {
                var args = new List<object>();
                foreach (var param in ctor.GetParameters()) {
                    // Find matching property value
                    // Try case-insensitive match
                    var key = propertyValues.Keys.FirstOrDefault(k => string.Equals(k, param.Name, StringComparison.OrdinalIgnoreCase));

                    object argValue = null;
                    if (key != null) {
                        argValue = propertyValues[key];
                    }

                    // If it's an array, we might have a List in propertyValues, need to convert
                    if (param.ParameterType.IsArray && argValue is IList list) {
                        var arr = Array.CreateInstance(param.ParameterType.GetElementType(), list.Count);
                        list.CopyTo(arr, 0);
                        argValue = arr;
                    }
                    else if (argValue == null && param.HasDefaultValue) {
                        argValue = param.DefaultValue;
                    }
                    else if (argValue == null && param.ParameterType.IsValueType) {
                        argValue = Activator.CreateInstance(param.ParameterType);
                    }

                    args.Add(argValue);
                }
                return (T)ctor.Invoke(args.ToArray());
            }

            // If no constructor, try default constructor and property setters
            var instance = Activator.CreateInstance<T>();
            foreach (var prop in properties) {
                if (prop.CanWrite && propertyValues.TryGetValue(prop.Name, out var val)) {
                    if (prop.PropertyType.IsArray && val is IList list) {
                        var arr = Array.CreateInstance(prop.PropertyType.GetElementType(), list.Count);
                        list.CopyTo(arr, 0);
                        prop.SetValue(instance, arr);
                    }
                    else {
                        prop.SetValue(instance, val);
                    }
                }
            }
            return instance;
        }
    }
}
