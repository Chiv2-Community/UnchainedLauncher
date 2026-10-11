using UnchainedLauncher.Core.JsonModels;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

internal sealed class InstallationTypeYamlConverter : IYamlTypeConverter {
    public bool Accepts(Type type) => type == typeof(InstallationType) || type == typeof(InstallationType?);

    internal static InstallationType Parse(string value) => value.ToLowerInvariant() switch {
        "steam" => InstallationType.Steam,
        "egs" or "epicgamesstore" => InstallationType.EpicGamesStore,
        _ => throw new ArgumentException("Platform must be Steam or EGS (EpicGamesStore).")
    };

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
        var scalar = parser.Consume<Scalar>();
        try { return Parse(scalar.Value); }
        catch (ArgumentException) { throw new YamlException(scalar.Start, scalar.End, "Invalid platform."); }
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        emitter.Emit(new Scalar(value?.ToString() ?? "null"));
}
