using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

public sealed class IniSource {
    internal Dictionary<string, Dictionary<string, Dictionary<string, object?>>> Inline { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal string? DirectoryPath { get; init; }
    internal IniOverrides? Parsed { get; set; }
    public Dictionary<string, Dictionary<string, object?>> this[string category] => Inline[category];

    public static implicit operator IniSource(Dictionary<string, Dictionary<string, Dictionary<string, object?>>> inline) {
        var result = new IniSource();
        foreach (var pair in inline) result.Inline.Add(pair.Key, pair.Value);
        return result;
    }
}

internal sealed class IniSourceYamlConverter : IYamlTypeConverter {
    public bool Accepts(Type type) => type == typeof(IniSource);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
        string SourceDirectory() => Path.GetDirectoryName((parser as YamlIncludeParser)?.SourcePath
            ?? throw new ArgumentException("INI references require a configuration source file."))!;
        string ReadPath() {
            var directory = SourceDirectory();
            var scalar = parser.Consume<Scalar>();
            if (string.IsNullOrWhiteSpace(scalar.Value) || scalar.Style == ScalarStyle.Plain && scalar.Value is "null" or "~")
                throw new YamlException(scalar.Start, scalar.End, "INI paths must be nonblank strings.");
            return Path.GetFullPath(scalar.Value, directory);
        }
        if (parser.Accept<Scalar>(out _)) return new IniSource { DirectoryPath = ReadPath() };
        parser.Consume<MappingStart>();
        var result = new IniSource();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!parser.Accept<MappingEnd>(out _)) {
            var category = parser.Consume<Scalar>().Value;
            if (!names.Add(category)) throw new YamlException("Duplicate INI category.");
            if (parser.Accept<Scalar>(out _)) result.Files.Add(category, ReadPath());
            else result.Inline.Add(category, (Dictionary<string, Dictionary<string, object?>>)rootDeserializer(typeof(Dictionary<string, Dictionary<string, object?>>))!);
        }
        parser.Consume<MappingEnd>();
        return result;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("INI source YAML is read-only.");
}
