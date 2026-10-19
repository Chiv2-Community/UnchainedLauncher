using StructuredINI;
using StructuredINI.Codecs;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.INIModels;
using UnchainedLauncher.Core.Services.Processes.Chivalry;

namespace UnchainedLauncher.CLI;

internal sealed class IniOverrides {
    private sealed record Entry(string File, string Section, string Key, string[] Values, bool Array, bool Raw);
    private readonly List<Entry> _entries = [];
    private static readonly Type[] ScalarTypes = [typeof(string), typeof(bool), typeof(int), typeof(float), typeof(double), typeof(decimal)];
    private static readonly Type[] MappedTypes = ScalarTypes.Concat(typeof(ClassLimit).Assembly.GetTypes()
        .Where(type => type.IsDefined(typeof(DeriveCodecAttribute), false))).Append(typeof(CallbackList)).ToArray();

    internal IEnumerable<string> RawArguments => _entries.Where(entry => entry.Raw)
        .Select(entry => new UEINIParameter(entry.File, entry.Section, entry.Key, entry.Values[0]).Rendered);

    internal bool Contains(string file, string section, string key) => _entries.Any(entry =>
        string.Equals(entry.File, file, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.Section, section, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    internal static IniOverrides Parse(Dictionary<string, Dictionary<string, Dictionary<string, object?>>>? files) {
        var result = new IniOverrides();
        if (files is null) return result;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, sections) in files) {
            if (!Regex.IsMatch(file, @"\A[A-Za-z][A-Za-z0-9_]*\z"))
                throw new ArgumentException("INI file names must be bare categories such as Game, without .ini or a path.");
            if (sections is null) throw new ArgumentException($"ini.{file} must contain a section mapping.");
            foreach (var (section, fields) in sections) {
                ValidateName(section, "section");
                if (fields is null) throw new ArgumentException($"ini.{file}.{section} must contain a field mapping.");
                foreach (var (key, node) in fields) {
                    if (key.TrimStart('!', '+', '-', '.').Equals("MapList", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("MapList cannot be set in ini; use server.map-rotation (or each servers entry's map-rotation) instead.");
                    ValidateName(key, "key");
                    var path = $"ini.{file}.{section}.{key}";
                    if (!paths.Add($"{file}\0{section}\0{key}")) throw new ArgumentException($"Duplicate INI field: {path} (names are case-insensitive).");
                    try { result._entries.Add(ParseEntry(file, section, key, node)); }
                    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or TargetInvocationException) {
                        // Do not echo values: INI overrides may contain credentials.
                        throw new ArgumentException($"Invalid {path}: {ex.Message}");
                    }
                }
            }
        }
        return result;
    }

    private static Entry ParseEntry(string file, string section, string key, object? node) {
        var knownType = FindKnownType(file, section, key);
        var type = knownType;
        var value = node;
        if (node is IDictionary map && (map.Contains("type") || map.Contains("value"))) {
            if (map.Keys.Cast<object>().Any(name => name is not string text || text is not ("type" or "value")) || !map.Contains("value"))
                throw new ArgumentException("Use only type and value in a typed override, and supply value.");
            value = map["value"];
            if (map.Contains("type")) {
                if (map["type"] is not string name) throw new ArgumentException("type must be a supported type name.");
                if (name.Equals("raw", StringComparison.OrdinalIgnoreCase)) {
                    if (value is not string raw || raw.Contains('\0')) throw new ArgumentException("raw requires a string value without NUL characters.");
                    return new Entry(file, section, key, [raw], false, true);
                }
                type = ResolveType(name);
                if (knownType is not null && type != knownType)
                    throw new ArgumentException($"This known field requires type {knownType.Name}, or raw.");
            }
        }
        if (type is null) throw new ArgumentException("Unknown field; supply { type: String, value: ... } (or type: raw for an exact INI string).");
        if (type.IsArray) {
            if (value is not IList list) throw new ArgumentException("Array fields require a list of values.");
            return new Entry(file, section, key, list.Cast<object?>().Select(item => Encode(type.GetElementType()!, item)).ToArray(), true, false);
        }
        return new Entry(file, section, key, [Encode(type, value)], false, false);
    }

    private static Type? FindKnownType(string file, string section, string key) {
        var fileProperty = typeof(Chivalry2INI).GetProperties().FirstOrDefault(property =>
            property.Name.Equals(file, StringComparison.OrdinalIgnoreCase));
        var sectionType = fileProperty?.PropertyType.GetProperties().Select(property => property.PropertyType).FirstOrDefault(type =>
            string.Equals(type.GetCustomAttribute<INISectionAttribute>()?.SectionName, section, StringComparison.OrdinalIgnoreCase));
        return sectionType?.GetProperties().FirstOrDefault(property =>
            (property.GetCustomAttribute<INIKeyAttribute>()?.KeyName ?? property.Name).Equals(key, StringComparison.OrdinalIgnoreCase))?.PropertyType;
    }

    private static Type ResolveType(string name) {
        if (name.EndsWith("[]", StringComparison.Ordinal)) {
            var element = ResolveType(name[..^2]);
            if (element.IsArray) throw new ArgumentException("Nested INI arrays are not supported.");
            return element.MakeArrayType();
        }
        if (name.Equals("Bool", StringComparison.OrdinalIgnoreCase)) return typeof(bool);
        if (name.Equals("Int", StringComparison.OrdinalIgnoreCase)) return typeof(int);
        if (name.Equals("Float", StringComparison.OrdinalIgnoreCase)) return typeof(float);
        return MappedTypes.FirstOrDefault(type => type.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unsupported type. Use String, Boolean, Int32, Single, Double, Decimal, CharacterClass, ClassLimit, AutoBalance, CallbackList, an array type such as String[], or raw.");
    }

    private static string Encode(Type type, object? node) {
        var value = ConvertValue(type, node);
        var codec = type == typeof(CallbackList) ? new CallbackListCodec() : CodecRegistry.Get(type);
        var encoded = (string)codec.GetType().GetMethod("Encode")!.Invoke(codec, [value])!;
        if (encoded.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Typed INI values cannot contain line breaks or NUL characters.");
        return encoded;
    }

    private static object ConvertValue(Type type, object? node) {
        if (node is null) throw new ArgumentException("INI values cannot be null.");
        if (type == typeof(CallbackList)) {
            if (node is not IList callbacks) throw new ArgumentException("CallbackList requires a list of strings.");
            return new CallbackList(callbacks.Cast<object?>().Select(item => (string)ConvertValue(typeof(string), item)).ToArray());
        }
        if (ScalarTypes.Contains(type) || type.IsEnum) {
            if (node is IDictionary or IList) throw new ArgumentException($"Expected a scalar {type.Name} value.");
            var text = Convert.ToString(node, CultureInfo.InvariantCulture)!;
            if (type.IsEnum) {
                if (!Enum.GetNames(type).Any(name => name.Equals(text, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Expected a named {type.Name} value.");
                return Enum.Parse(type, text, true);
            }
            try {
                var codec = CodecRegistry.Get(type);
                var value = codec.GetType().GetMethod("Decode")!.Invoke(codec, [text])!;
                if (value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f))
                    throw new ArgumentException("Numbers must be finite.");
                return value;
            }
            catch (TargetInvocationException) { throw new ArgumentException($"Expected a valid {type.Name} value."); }
        }
        var constructor = type.GetConstructors().OrderByDescending(ctor => ctor.GetParameters().Length).First();
        var parameters = constructor.GetParameters();
        object?[] args;
        if (node is IDictionary fields) {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry field in fields) {
                if (field.Key is not string name || !parameters.Any(parameter => parameter.Name!.Equals(name, StringComparison.OrdinalIgnoreCase)) || !values.TryAdd(name, field.Value))
                    throw new ArgumentException($"Invalid or duplicate member of {type.Name}; expected {string.Join(", ", parameters.Select(parameter => parameter.Name))}.");
            }
            args = parameters.Select(parameter => values.TryGetValue(parameter.Name!, out var item)
                ? ConvertValue(parameter.ParameterType, item)
                : throw new ArgumentException($"Missing {type.Name}.{parameter.Name}.")).ToArray();
        }
        else if (node is IList list && list.Count == parameters.Length) {
            args = parameters.Select((parameter, index) => ConvertValue(parameter.ParameterType, list[index])).ToArray();
        }
        else throw new ArgumentException($"{type.Name} requires a member mapping or a list of {parameters.Length} parameters.");
        return constructor.Invoke(args);
    }

    private static void ValidateName(string name, string kind) {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.IndexOfAny(['[', ']', ':', '=', ',', '\r', '\n', '\0', '"']) >= 0 ||
            kind == "key" && "!+.-;#".Contains(name[0]))
            throw new ArgumentException($"Invalid INI {kind} name.");
    }

    internal void Apply(string directory) {
        foreach (var file in _entries.Where(entry => !entry.Raw).GroupBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)) {
            var writer = new StructuredINIWriter();
            foreach (var section in file.GroupBy(entry => entry.Section, StringComparer.OrdinalIgnoreCase)) {
                var lines = section.SelectMany(entry => entry.Array
                    ? new[] { KeyValuePair.Create("!" + entry.Key, "ClearArray") }
                        .Concat(entry.Values.Select(value => KeyValuePair.Create("." + entry.Key, value)))
                    : [KeyValuePair.Create(entry.Key, entry.Values[0])]);
                writer.BufferSection(section.Key, lines);
            }
            if (!writer.WriteOut(Path.Combine(directory, file.Key + ".ini")))
                throw new IOException("Could not apply server INI overrides; refusing to launch.");
        }
    }
}