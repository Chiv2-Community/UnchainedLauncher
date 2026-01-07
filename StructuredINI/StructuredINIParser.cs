using System.Collections;
using System.Reflection;

using System.Text;

namespace StructuredINI {
    public class StructuredINIParser {
        public string Serialize<T>(T instance) {
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
                    if (list.Count == 0) {
                        // Explicitly clear to represent empty list
                        sb.AppendLine($"!{keyName}=Clear");
                    }
                    else {
                        var codec = CodecRegistry.Get(elementType) as dynamic;
                        var emittedValues = new HashSet<object>();

                        for (int i = 0; i < list.Count; i++) {
                            var item = list[i];
                            // Encode value
                            string encoded;
                            try {
                                var encodeMethod = codec.GetType().GetMethod("Encode");
                                encoded = (string)encodeMethod.Invoke(codec, new object[] { item });
                            }
                            catch (Exception ex) {
                                throw new InvalidOperationException($"Failed to encode value of type {elementType.Name}", ex);
                            }

                            if (i == 0) {
                                sb.AppendLine($"{keyName}={encoded}");
                                emittedValues.Add(item);
                            }
                            else {
                                if (emittedValues.Contains(item)) {
                                    sb.AppendLine($".{keyName}={encoded}");
                                }
                                else {
                                    sb.AppendLine($"+{keyName}={encoded}");
                                    emittedValues.Add(item);
                                }
                            }
                        }
                    }
                }
                else {
                    var codec = CodecRegistry.Get(prop.PropertyType) as dynamic;
                    string encoded;
                    try {
                        var encodeMethod = codec.GetType().GetMethod("Encode");
                        encoded = (string)encodeMethod.Invoke(codec, new object[] { val });
                    }
                    catch (Exception ex) {
                        throw new InvalidOperationException($"Failed to encode value of type {prop.PropertyType.Name}", ex);
                    }
                    sb.AppendLine($"{keyName}={encoded}");
                }
            }

            return sb.ToString();
        }

        public T Deserialize<T>(string iniContent) {
            var type = typeof(T);
            var sectionAttr = type.GetCustomAttribute<INISectionAttribute>();
            if (sectionAttr == null) {
                throw new InvalidOperationException($"Type {type.Name} does not have [INISection] attribute.");
            }

            var sectionName = sectionAttr.SectionName;
            var lines = GetLinesForSection(iniContent, sectionName);

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

            foreach (var line in lines) {
                ProcessLine(line, propMap, propertyValues);
            }

            return CreateInstance<T>(propertyValues, properties);
        }

        private List<string> GetLinesForSection(string content, string sectionName) {
            var result = new List<string>();
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool insideSection = false;

            foreach (var line in lines) {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) {
                    var currentSection = trimmed.Substring(1, trimmed.Length - 2);
                    insideSection = string.Equals(currentSection, sectionName, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (insideSection) {
                    result.Add(trimmed);
                }
            }
            return result;
        }

        private void ProcessLine(string line, Dictionary<string, PropertyInfo> propMap, Dictionary<string, object> propertyValues) {
            // Syntax: [Prefix]Key=Value
            // Prefixes: !, +, -, .
            // If no prefix, it's Set.

            // Find position of =
            int idx = line.IndexOf('=');
            if (idx == -1) return; // Invalid line

            string keyPart = line.Substring(0, idx).Trim();
            string valuePart = line.Substring(idx + 1).Trim();

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
                HandleScalarOperation(prefix, prop.Name, valuePart, prop.PropertyType, propertyValues);
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

        private void HandleScalarOperation(char prefix, string key, string valueStr, Type type, Dictionary<string, object> values) {
            // Scalars mostly support Set (=).
            // Maybe ! to clear (null)?

            if (prefix == '\0') {
                var codec = CodecRegistry.Get(type) as dynamic;
                var value = codec.Decode(valueStr);
                values[key] = value;
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
            var codec = CodecRegistry.Get(elementType) as dynamic;
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
                    list.Clear();
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