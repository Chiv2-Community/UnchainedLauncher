using System.Security.Cryptography;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

internal static class MapProfileIni {
    internal static async Task PrepareAsync(CliLaunchOptions request, CancellationToken token) {
        if (request is not ServerOptions server || server.Settings.Rotation is not { HasOverrides: true } rotation) return;
        var layers = rotation.Overrides.Select(server.Ini.WithMapOverrides).ToArray();
        var identities = layers.Select(ini => ini.Identity).ToArray();
        string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        // Immutable generations avoid carrying removed fields or slots into later launches.
        var root = Path.Combine(request.InstancePaths.Root, "map-config", Hash(JsonSerializer.SerializeToUtf8Bytes(new { rotation.Names, rotation.Profiles, identities })));
        var directory = Path.Combine(root, "ini-overrides");
        for (var index = 0; index < layers.Length; index++) {
            token.ThrowIfCancellationRequested();
            var profile = rotation.Profiles[index];
            layers[index].Apply(Path.Combine(directory, "map-profiles", profile));
        }
        Directory.CreateDirectory(directory);
        var entries = rotation.Profiles.Select((profile, index) => new { profile, name = rotation.Names[index] }).ToArray();
        var yaml = new SerializerBuilder().Build().Serialize(new Dictionary<string, object> { ["map-rotation"] = entries });
        await File.WriteAllTextAsync(Path.Combine(directory, "unchained.yaml"), yaml, token);
        server.MapProfilePath = request.Shared.Proton is { } proton
            ? await proton.WinePathAsync(root, windows: true, token) : root;
    }
}
