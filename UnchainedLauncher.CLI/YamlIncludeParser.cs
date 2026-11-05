using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnchainedLauncher.CLI;

// Expand events rather than reserializing nodes, preserving scalar types and source marks.
internal sealed class YamlIncludeParser : IParser {
    private enum Resource { None, Path, Registry }
    private const int MaximumDepth = 32;
    private const int MaximumEvents = 100_000;
    private const long MaximumBytes = 16 * 1024 * 1024;
    private sealed record SourceEvent(ParsingEvent Event, string Path);
    private readonly List<SourceEvent> _events = [];
    private readonly HashSet<string> _active = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private long _bytes;
    private int _index = -1;
    private int _document;
    private string _anchorPrefix = "";

    internal string SourcePath { get; private set; }
    public ParsingEvent? Current => _index >= 0 && _index < _events.Count ? _events[_index].Event : null;

    internal YamlIncludeParser(string path, Type type) {
        SourcePath = Path.GetFullPath(path);
        _events.Add(new SourceEvent(new StreamStart(), SourcePath));
        _events.Add(new SourceEvent(new DocumentStart(), SourcePath));
        Read(SourcePath, 0, type);
        _events.Add(new SourceEvent(new DocumentEnd(true), SourcePath));
        _events.Add(new SourceEvent(new StreamEnd(), SourcePath));
    }

    public bool MoveNext() {
        if (++_index >= _events.Count) return false;
        SourcePath = _events[_index].Path;
        return true;
    }

    private void Read(string path, int depth, Type? type, Resource resource = Resource.None) {
        if (depth > MaximumDepth) throw new ArgumentException($"YAML !load nesting exceeds {MaximumDepth}: {path}");
        if (!_active.Add(path)) throw new ArgumentException($"YAML !load cycle: {path}");
        var previousPrefix = _anchorPrefix;
        _anchorPrefix = $"document{++_document}_";
        try {
            _bytes += new FileInfo(path).Length;
            if (_bytes > MaximumBytes) throw new ArgumentException("YAML !load expansion exceeds 16 MiB.");
            using var reader = File.OpenText(path);
            var parser = new Parser(reader);
            parser.Consume<StreamStart>();
            parser.Consume<DocumentStart>();
            ExpandNode(parser, path, depth, type, ignored: false, nesting: 0, resource);
            parser.Consume<DocumentEnd>();
            if (!parser.Accept<StreamEnd>(out _)) throw new ArgumentException($"YAML file must contain exactly one document: {path}");
        }
        catch (YamlException ex) {
            throw new ArgumentException($"Invalid YAML configuration or schema at {path}, line {ex.Start.Line}, column {ex.Start.Column}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            throw new ArgumentException($"Cannot read YAML configuration: {path}");
        }
        finally {
            _active.Remove(path);
            _anchorPrefix = previousPrefix;
        }
    }

    private void Add(IParser parser, string path, Resource resource = Resource.None) {
        if (_events.Count >= MaximumEvents) throw new ArgumentException("YAML !load expansion exceeds 100000 events.");
        // Anchors belong to one source document, including repeated loads of the same file.
        AnchorName Anchor(AnchorName anchor) => anchor.IsEmpty ? anchor : new AnchorName(_anchorPrefix + anchor.Value);
        var item = parser.Current!;
        if (item is Scalar value && resource != Resource.None && !string.IsNullOrWhiteSpace(value.Value) &&
            !(value.Style == ScalarStyle.Plain && (value.Value == "~" || value.Value.Equals("null", StringComparison.OrdinalIgnoreCase)))) {
            var text = value.Value;
            if (resource == Resource.Path) text = Path.GetFullPath(text, Path.GetDirectoryName(path)!);
            else if (text.StartsWith("local:", StringComparison.Ordinal) || text.StartsWith("config:", StringComparison.Ordinal)) {
                var colon = text.IndexOf(':');
                text = text[..(colon + 1)] + Path.GetFullPath(text[(colon + 1)..], Path.GetDirectoryName(path)!);
            }
            item = new Scalar(value.Anchor, value.Tag, text, value.Style, value.IsPlainImplicit, value.IsQuotedImplicit, value.Start, value.End, value.IsKey);
        }
        var scoped = item switch {
            Scalar scalar when !scalar.Anchor.IsEmpty => new Scalar(Anchor(scalar.Anchor), scalar.Tag, scalar.Value,
                scalar.Style, scalar.IsPlainImplicit, scalar.IsQuotedImplicit, scalar.Start, scalar.End, scalar.IsKey),
            MappingStart mapping when !mapping.Anchor.IsEmpty => new MappingStart(Anchor(mapping.Anchor), mapping.Tag,
                mapping.IsImplicit, mapping.Style, mapping.Start, mapping.End),
            SequenceStart sequence when !sequence.Anchor.IsEmpty => new SequenceStart(Anchor(sequence.Anchor), sequence.Tag,
                sequence.IsImplicit, sequence.Style, sequence.Start, sequence.End),
            AnchorAlias alias => (ParsingEvent)new AnchorAlias(Anchor(alias.Value), alias.Start, alias.End),
            _ => item
        };
        _events.Add(new SourceEvent(scoped, path));
        parser.MoveNext();
    }

    private void ExpandNode(IParser parser, string path, int depth, Type? type, bool ignored, int nesting, Resource resource = Resource.None) {
        if (nesting > 128) throw new ArgumentException($"YAML nesting exceeds 128: {path}");
        var item = parser.Current!;
        if (!ignored && item is NodeEvent node && !node.Tag.IsEmpty && node.Tag.Value != "!" &&
            !node.Tag.Value.StartsWith("tag:yaml.org,2002:", StringComparison.Ordinal)) {
            if (node.Tag.Value != "!load" || item is not Scalar scalar || string.IsNullOrWhiteSpace(scalar.Value))
                throw new ArgumentException($"Unsupported YAML tag or invalid !load at {path}, line {item.Start.Line}, column {item.Start.Column}.");
            if (!node.Anchor.IsEmpty)
                throw new ArgumentException($"YAML !load cannot declare an anchor at {path}, line {item.Start.Line}.");
            if (Uri.TryCreate(scalar.Value, UriKind.Absolute, out var uri) && !uri.IsFile)
                throw new ArgumentException($"YAML !load requires a local file at {path}, line {item.Start.Line}.");
            Read(Path.GetFullPath(scalar.Value, Path.GetDirectoryName(path)!), depth + 1, type, resource);
            parser.MoveNext();
            return;
        }
        if (item is MappingStart) {
            Add(parser, path);
            while (!parser.Accept<MappingEnd>(out _)) {
                var key = (parser.Current as Scalar)?.Value;
                var property = type?.GetProperties().FirstOrDefault(property => HyphenatedNamingConvention.Instance.Apply(property.Name) == key);
                // Inactive schemas must remain unread, even when their values contain !load.
                var skip = ignored || property?.GetCustomAttributesData().Any(attribute => attribute.AttributeType == typeof(YamlConverterAttribute) &&
                    attribute.ConstructorArguments[0].Value is Type converter && converter == typeof(IgnoredYamlNodeConverter)) == true;
                var valueType = property?.PropertyType ?? (type?.IsGenericType == true && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                    ? type.GetGenericArguments()[1] : null);
                var valueResource = property?.Name == "Registries" ? Resource.Registry
                    : property?.Name == "Chiv2Path" || property?.DeclaringType == typeof(ProtonConfiguration) ? Resource.Path : Resource.None;
                ExpandNode(parser, path, depth, null, ignored, nesting + 1);
                ExpandNode(parser, path, depth, valueType, skip, nesting + 1, valueResource);
            }
            Add(parser, path);
        } else if (item is SequenceStart) {
            var elementType = type?.IsGenericType == true ? type.GetGenericArguments().FirstOrDefault() : null;
            Add(parser, path);
            while (!parser.Accept<SequenceEnd>(out _)) ExpandNode(parser, path, depth, elementType, ignored, nesting + 1, resource);
            Add(parser, path);
        } else {
            Add(parser, path, ignored ? Resource.None : resource);
        }
    }
}
