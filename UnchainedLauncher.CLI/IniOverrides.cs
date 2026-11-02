using StructuredINI;
using StructuredINI.Codecs;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.INIModels;
using UnchainedLauncher.Core.Services.Processes.Chivalry;

namespace UnchainedLauncher.CLI;

internal sealed class IniOverrides {
    private sealed record Entry(string File, string Section, string Key, string[] Values, bool Array, bool Raw, KeyValuePair<string, string>[]? Lines = null);
    private sealed record ImportedFile(byte[] Bytes, string Entries);
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, ImportedFile> _imported = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Type[] ScalarTypes = [typeof(string), typeof(bool), typeof(int), typeof(float), typeof(double), typeof(decimal)];
    private static readonly Type[] MappedTypes = ScalarTypes.Concat(typeof(ClassLimit).Assembly.GetTypes()
        .Where(type => type.IsDefined(typeof(DeriveCodecAttribute), false))).Append(typeof(CallbackList)).ToArray();

    internal IEnumerable<string> RawArguments => _entries.Where(entry => entry.Raw)
        .Select(entry => new UEINIParameter(entry.File, entry.Section, entry.Key, entry.Values[0]).Rendered);

    internal bool IsEmpty => _entries.Count == 0 && _imported.Count == 0;
    internal string Identity => System.Text.Json.JsonSerializer.Serialize(new { Entries = _entries, Files = _imported.Select(pair => new { pair.Key, Hash = Convert.ToHexString(SHA256.HashData(pair.Value.Bytes)) }) });

    internal IniOverrides Merge(IniOverrides overrides) {
        var result = new IniOverrides();
        result._entries.AddRange(_entries);
        foreach (var entry in overrides._entries) result.AddEntry(entry);
        foreach (var pair in _imported) result._imported[pair.Key] = pair.Value;
        foreach (var pair in overrides._imported) result._imported[pair.Key] = pair.Value;
        return result;
    }

    private static IEnumerable<KeyValuePair<string, string>> Commands(Entry entry) =>
        entry.Lines ?? new StructuredINIParser().SerializeEntries(entry.Key, entry.Values, entry.Array, isConfigRoot: false);

    private void AddEntry(Entry entry) {
        if (entry.Lines is { Length: 0 }) return;
        var index = _entries.FindIndex(previous => previous.File.Equals(entry.File, StringComparison.OrdinalIgnoreCase) &&
            previous.Section.Equals(entry.Section, StringComparison.OrdinalIgnoreCase) && previous.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) {
            var previous = _entries[index];
            // Deltas extend earlier commands; replacements and explicit clears discard them.
            if (entry.Lines is not null && StructuredINIParser.IsArrayDelta(entry.Lines)) {
                if (previous.Raw) throw new ArgumentException("INI array operations cannot modify a raw command-line override.");
                entry = entry with { Lines = Commands(previous).Concat(entry.Lines).ToArray(), Array = true };
            }
            _entries.RemoveAt(index);
        }
        _entries.Add(entry);
    }

    internal void ValidateMapOverrides(IniOverrides overrides) {
        foreach (var entry in overrides._entries) {
            if (!Contains(entry.File, entry.Section, entry.Key))
                throw new ArgumentException($"Map-profile INI field {entry.File}.{entry.Section}.{entry.Key} requires a server.ini default (or global-settings.ini default) so other maps can restore it.");
            var baseline = _entries.First(item => item.File.Equals(entry.File, StringComparison.OrdinalIgnoreCase) &&
                item.Section.Equals(entry.Section, StringComparison.OrdinalIgnoreCase) && item.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
            if (ReferenceEquals(baseline, entry) || Commands(baseline).SequenceEqual(Commands(entry))) continue;
            var array = entry.Array || entry.Lines?.Any(line => "+.-!".Contains(line.Key[0])) == true;
            if (array && baseline.Lines is not null && !baseline.Lines.Any(line => line.Key.StartsWith('!')))
                throw new ArgumentException($"Imported INI array {entry.File}.{entry.Section}.{entry.Key} requires an explicit !{entry.Key}=ClearArray baseline for map restoration.");
        }
    }

    internal bool Contains(string file, string section, string key) => _entries.Any(entry =>
        string.Equals(entry.File, file, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.Section, section, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    internal static IniOverrides Parse(object? input) {
        if (input is IniSource source) {
            if (source.Parsed is not null) return source.Parsed;
            var imported = new IniOverrides();
            var sources = source.Files.ToList();
            if (source.DirectoryPath is { } directory) {
                if (!Directory.Exists(directory)) throw new ArgumentException($"INI directory does not exist: {directory}");
                sources.AddRange(Directory.EnumerateFiles(directory).Where(path => Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.Ordinal).Select(path => KeyValuePair.Create(Path.GetFileNameWithoutExtension(path), path)));
                if (sources.Count == 0) throw new ArgumentException($"INI directory contains no INI files: {directory}");
            }
            foreach (var pair in sources) imported.Import(pair.Key, pair.Value);
            source.Parsed = imported.Merge(Parse(source.Inline));
            return source.Parsed;
        }
        var files = input as Dictionary<string, Dictionary<string, Dictionary<string, object?>>>;
        if (input is not null && files is null) throw new ArgumentException("INI must be a directory or category mapping.");
        var result = new IniOverrides();
        if (files is null) return result;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, sections) in files) {
            ValidateCategory(file);
            if (sections is null) throw new ArgumentException($"ini.{file} must contain a section mapping.");
            foreach (var (section, fields) in sections) {
                ValidateName(section, "section");
                if (fields is null) throw new ArgumentException($"ini.{file}.{section} must contain a field mapping.");
                foreach (var (key, node) in fields) {
                    if (key.TrimStart('!', '+', '-', '.').Equals("MapList", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("MapList cannot be set in ini; use server.map-rotation (or each servers entry's map-rotation) instead.");
                    var operation = key.Length > 0 && "!+.-".Contains(key[0]) ? key[0] : '\0';
                    var name = operation == '\0' ? key : key[1..];
                    ValidateName(name, "key");
                    var path = $"ini.{file}.{section}.{key}";
                    if (!paths.Add($"{file}\0{section}\0{key}")) throw new ArgumentException($"Duplicate INI field: {path} (names are case-insensitive).");
                    try { result.AddEntry(ParseEntry(file, section, name, node, operation)); }
                    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or TargetInvocationException) {
                        // Do not echo values: INI overrides may contain credentials.
                        throw new ArgumentException($"Invalid {path}: {ex.Message}");
                    }
                }
            }
        }
        return result;
    }

    private static Entry ParseEntry(string file, string section, string key, object? node, char operation) {
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
                    if (operation != '\0') throw new ArgumentException("INI array operations cannot use raw command-line overrides.");
                    if (value is not string raw || raw.Contains('\0')) throw new ArgumentException("raw requires a string value without NUL characters.");
                    return new Entry(file, section, key, [raw], false, true);
                }
                type = ResolveType(name);
                if (knownType is not null && type != knownType)
                    throw new ArgumentException($"This known field requires type {knownType.Name}, or raw.");
            }
        }
        type ??= InferType(value);
        if (type.IsArray) {
            if (value is not IList list) throw new ArgumentException("Array fields require a list of values.");
            var values = list.Cast<object?>().Select(item => Encode(type.GetElementType()!, item)).ToArray();
            var lines = operation == '\0' ? null : new StructuredINIParser().SerializeEntries(key, values, collection: true, isConfigRoot: false, operation: operation).ToArray();
            return new Entry(file, section, key, values, true, false, lines);
        }
        if (operation != '\0') throw new ArgumentException("INI array operations require an array type and a list of values.");
        return new Entry(file, section, key, [Encode(type, value)], false, false);
    }

    private static Type InferType(object? value) {
        if (value is IList list) {
            var types = list.Cast<object?>().Select(InferType).Distinct().ToArray();
            if (types.Any(type => type.IsArray)) throw new ArgumentException("Nested INI arrays are not supported; supply an explicit record-array type for structured values.");
            Type element;
            if (types.Length == 0) element = typeof(string); // Clearing an array needs no element codec.
            else if (types.Length == 1) element = types[0];
            else if (types.All(type => type == typeof(int) || type == typeof(decimal) || type == typeof(double)))
                element = types.Contains(typeof(double)) ? typeof(double) : typeof(decimal);
            else throw new ArgumentException("Cannot infer a common INI array type; quote string values or supply an explicit array type.");
            return element.MakeArrayType();
        }
        return value switch {
            null => throw new ArgumentException("INI values cannot be null."),
            string => typeof(string),
            bool => typeof(bool),
            byte or sbyte or short or ushort or int => typeof(int),
            uint or long or ulong or decimal => typeof(decimal),
            float number when !float.IsFinite(number) => throw new ArgumentException("Numbers must be finite."),
            double number when !double.IsFinite(number) => throw new ArgumentException("Numbers must be finite."),
            float or double => typeof(double),
            _ => throw new ArgumentException("Cannot infer an INI type from a mapping or structured value; supply an explicit type.")
        };
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

    internal void Apply(string directory, string? cacheDirectory = null, Func<string, string, bool>? createLink = null) {
        var files = new Dictionary<string, StructuredINIWriter>(StringComparer.OrdinalIgnoreCase);
        BufferInto(files);
        Publish(directory, files, cacheDirectory, createLink, isConfigRoot: false);
    }

    internal void ApplyProfile(string directory, StructuredINIWriter gameDefaults, MapRotation? rotation, string cacheDirectory) {
        var files = new Dictionary<string, StructuredINIWriter>(StringComparer.OrdinalIgnoreCase) { ["Game"] = gameDefaults };
        BufferInto(files);
        rotation?.BufferInto(gameDefaults);
        Publish(directory, files, cacheDirectory, null, isConfigRoot: true, gamePatched: true);
    }

    private void Publish(string directory, Dictionary<string, StructuredINIWriter> files, string? cacheDirectory,
        Func<string, string, bool>? createLink, bool isConfigRoot, bool gamePatched = false) {
        Directory.CreateDirectory(directory);
        foreach (var (category, snapshot) in _imported) {
            if (gamePatched && category.Equals("Game", StringComparison.OrdinalIgnoreCase) || EntriesIdentity(category) != snapshot.Entries) continue;
            var imported = snapshot;
            if (isConfigRoot) {
                using var reader = new StreamReader(new MemoryStream(snapshot.Bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                imported = snapshot with { Bytes = Encoding.UTF8.GetBytes(StructuredINIWriter.SerializeDocument(reader.ReadToEnd(), isConfigRoot: true)) };
            }
            files.Remove(category);
            var destination = Path.Combine(directory, category + ".ini");
            if (File.Exists(destination)) {
                if (File.ReadAllBytes(destination).SequenceEqual(imported.Bytes)) continue;
            }
            // Link an immutable cached snapshot, never a user-owned file that can change later.
            cacheDirectory ??= Path.Combine(directory, ".ini-cache");
            Directory.CreateDirectory(cacheDirectory);
            var cached = Path.Combine(cacheDirectory, Convert.ToHexStringLower(SHA256.HashData(imported.Bytes)));
            var staged = Path.Combine(cacheDirectory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                File.WriteAllBytes(staged, imported.Bytes);
                try { File.Move(staged, cached); }
                catch (IOException) when (File.Exists(cached)) {
                    if (!File.ReadAllBytes(cached).SequenceEqual(imported.Bytes)) throw new IOException("INI cache content mismatch.");
                }
            } finally {
                if (File.Exists(staged)) File.Delete(staged);
            }
            var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                if (!(createLink ?? AssetCacheFiles.TryCreateHardLink)(temporary, cached)) File.WriteAllBytes(temporary, imported.Bytes);
                File.Move(temporary, destination, overwrite: true);
            } finally {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        // Atomic replacement keeps any existing hard link from being edited in place.
        IniPatches.Write(directory, files, isConfigRoot);
    }

    internal void BufferInto(Dictionary<string, StructuredINIWriter> files) {
        foreach (var file in _entries.Where(entry => !entry.Raw).GroupBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)) {
            if (!files.TryGetValue(file.Key, out var writer)) files[file.Key] = writer = new StructuredINIWriter();
            foreach (var section in file.GroupBy(entry => entry.Section, StringComparer.OrdinalIgnoreCase)) {
                var lines = section.SelectMany(Commands);
                writer.MergeSection(section.Key, lines);
            }
        }
    }

    private string EntriesIdentity(string category) => System.Text.Json.JsonSerializer.Serialize(_entries.Where(entry => entry.File.Equals(category, StringComparison.OrdinalIgnoreCase)));

    private void Import(string category, string path) {
        ValidateCategory(category);
        if (_imported.ContainsKey(category))
            throw new ArgumentException("INI categories must be unique bare names such as Engine or Game.");
        byte[] bytes;
        string text;
        try {
            bytes = File.ReadAllBytes(path);
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            throw new ArgumentException($"Cannot read INI file: {path}");
        }
        IReadOnlyList<StructuredINIParser.Entry> lines;
        try { lines = new StructuredINIParser().ReadEntries(text, strict: true); }
        catch (FormatException) { throw new ArgumentException($"Invalid INI structure in {path}."); }
        if (lines.Any(line => line.Name.Equals("MapList", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("MapList cannot be set in ini; use map-rotation.");
        foreach (var group in lines.GroupBy(line => line.Section + "\0" + line.Name, StringComparer.OrdinalIgnoreCase)) {
            var first = group.First();
            _entries.Add(new Entry(category, first.Section, first.Name, [], false, false,
                group.Select(line => KeyValuePair.Create(line.Key, line.Value)).ToArray()));
        }
        _imported.Add(category, new ImportedFile(bytes, EntriesIdentity(category)));
    }

    private static void ValidateCategory(string category) {
        if (!Regex.IsMatch(category, @"\A[A-Za-z][A-Za-z0-9_]*\z") ||
            Regex.IsMatch(category, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("INI file names must be bare categories such as Game, without .ini, a path, or reserved device names.");
    }
}
