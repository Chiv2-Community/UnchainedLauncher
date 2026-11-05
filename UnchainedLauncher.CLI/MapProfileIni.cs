using System.Security.Cryptography;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

internal static class MapProfileIni {
    internal static async Task PrepareAsync(CliLaunchOptions request, CancellationToken token) {
        if (request is not ServerOptions server || server.Settings.Rotation is not { } rotation || !rotation.HasOverrides && rotation.Actors is null) return;
        var layers = rotation.Layers.Where(layer => !layer.Ini.IsEmpty).ToArray();
        var identities = layers.Select(layer => new { layer.Group, layer.Name, layer.Ini.Identity }).ToArray();
        string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        // Immutable generations avoid carrying removed fields or slots into later launches.
        var root = Path.Combine(request.InstancePaths.Root, "map-config", Hash(JsonSerializer.SerializeToUtf8Bytes(new { rotation.Maps, rotation.Names, rotation.Profiles, rotation.Actors, identities, Baseline = server.Ini.Identity })));
        var directory = Path.Combine(root, "ini-overrides");
        foreach (var layer in layers) {
            token.ThrowIfCancellationRequested();
            layer.Ini.Apply(Path.Combine(directory, layer.Group, layer.Name), Path.Combine(request.InstancePaths.Root, "map-config", "ini-cache"));
        }
        Directory.CreateDirectory(directory);
        var entries = rotation.Profiles.Select((profile, index) => {
            var entry = new Dictionary<string, object> { ["profile"] = profile, ["name"] = rotation.Names[index], ["map"] = rotation.Maps[index] };
            if (rotation.Actors is not null) entry["mod-actors"] = rotation.Actors[index];
            return entry;
        }).ToArray();
        var yaml = new SerializerBuilder().Build().Serialize(new Dictionary<string, object> { ["match-rotation"] = entries });
        await File.WriteAllTextAsync(Path.Combine(root, "unchained.yaml"), yaml, token);
        server.MapProfilePath = request.Shared.Proton is { } proton
            ? await proton.WinePathAsync(root, windows: true, token) : root;
    }
}
