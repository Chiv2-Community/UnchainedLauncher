namespace StructuredINI {
    internal static class TypeNameFormatter {
        private static readonly Dictionary<Type, string> CSharpAliases = new() {
            { typeof(bool), "bool" },
            { typeof(byte), "byte" },
            { typeof(sbyte), "sbyte" },
            { typeof(char), "char" },
            { typeof(decimal), "decimal" },
            { typeof(double), "double" },
            { typeof(float), "float" },
            { typeof(int), "int" },
            { typeof(uint), "uint" },
            { typeof(long), "long" },
            { typeof(ulong), "ulong" },
            { typeof(short), "short" },
            { typeof(ushort), "ushort" },
            { typeof(string), "string" },
            { typeof(object), "object" },
            { typeof(void), "void" },
        };

        public static string Format(Type type) {
            if (type == null) return "<null>";

            if (CSharpAliases.TryGetValue(type, out var alias)) {
                return alias;
            }

            if (type.IsArray) {
                var elem = type.GetElementType();
                return $"{Format(elem)}[]";
            }

            var nullableUnderlying = Nullable.GetUnderlyingType(type);
            if (nullableUnderlying != null) {
                return $"{Format(nullableUnderlying)}?";
            }

            if (type.IsGenericType) {
                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0) {
                    name = name.Substring(0, tick);
                }

                var args = type.GetGenericArguments().Select(Format);
                return $"{name}<{string.Join(", ", args)}>";
            }

            return type.Name;
        }
    }
}