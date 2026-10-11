using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

internal sealed class IgnoredYamlNodeConverter : IYamlTypeConverter {
    // Opt in only via YamlConverter on the opposite command's property, never by type.
    public bool Accepts(Type type) => false;

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
        // Consume one syntactically valid YAML node without materializing or validating its contents.
        parser.SkipThisAndNestedEvents();
        return null;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("Ignored YAML nodes are only used when loading configuration.");
}