using System.ComponentModel.DataAnnotations;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

public abstract class LaunchConfiguration {
    [Required]
    public string? Chiv2Path { get; set; }

    [Required, EnumDataType(typeof(InstallationType))]
    public InstallationType? Platform { get; set; }

    public string ServerBrowserBackend { get; set; } = "https://servers.polehammer.net";
    public bool UpdatePlugin { get; set; } = true;
    public bool AllowUnstablePlugin { get; set; }
    public List<string>? GameArgs { get; set; } = [];
    public ProtonConfiguration? Proton { get; set; }

    public virtual void Validate() {
        Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true);
        if (Platform is not (InstallationType.Steam or InstallationType.EpicGamesStore))
            throw new ValidationException("Platform must be Steam or EGS (EpicGamesStore).");
    }
}

public sealed class ServerConfiguration : LaunchConfiguration {
    public Dictionary<string, MapProfile?>? MapProfiles { get; set; }
    public bool NoTui { get; set; }
    public ServerSettings? Server { get; set; }
    public Dictionary<string, ServerSettings?>? Servers { get; set; }
    // Recognized but skipped: do not deserialize or validate the inactive schema.
    [YamlConverter(typeof(IgnoredYamlNodeConverter))]
    public object? Client { get; set; }

    public override void Validate() {
        base.Validate();
        foreach (var (name, profile) in MapProfiles ?? []) {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim()) throw new ValidationException("map-profiles keys must not be blank or have surrounding whitespace.");
            MapRotation.ValidateMap(profile, $"map-profiles.{name}");
        }
        if (Server is not null) ValidateServer(Server);
        if (Servers is not null) {
            if (Server is not null) throw new ValidationException("Use either server or servers, not both.");
            if (Servers.Count == 0) throw new ValidationException("servers must contain at least one named server.");
            foreach (var settings in Servers.Values) {
                if (settings is null) throw new ValidationException("Each servers entry must contain server settings.");
                ValidateServer(settings);
            }
        }
    }

    private void ValidateServer(ServerSettings settings) {
        Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
        settings.Rotation = MapRotation.Resolve(settings.MapRotation, MapProfiles, settings.ExplicitStartingMap);
    }
}

public sealed class ClientConfiguration : LaunchConfiguration {
    [YamlConverter(typeof(IgnoredYamlNodeConverter))]
    public object? MapProfiles { get; set; }
    [Required]
    public string SavedDirSuffix { get; set; } = UnchainedLauncher.Core.Services.Mods.ProfileAssetPaths.SharedSuffix;
    public ModSetupConfiguration? ModSetup { get; set; }
    public ClientSettings? Client { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))]
    public object? Server { get; set; }
    [YamlConverter(typeof(IgnoredYamlNodeConverter))]
    public object? Servers { get; set; }
}

public sealed class ModSetupConfiguration {
    public List<string>? Registries { get; set; } = [];
    public List<string>? Mods { get; set; } = [];
    public List<string>? Dlls { get; set; } = [];
}

public sealed class ClientSettings {
    public List<string>? GameArgs { get; set; } = [];
}

public sealed class ServerSettings {
    public bool AttachStdio { get; set; }
    public bool NoSpawnConsole { get; set; }
    public List<MapRotationEntry>? MapRotation { get; set; }
    [YamlIgnore]
    internal MapRotation? Rotation { get; set; }
    [YamlIgnore]
    internal string? ExplicitStartingMap { get; private set; }
    public ModSetupConfiguration? ModSetup { get; set; }
    public Dictionary<string, Dictionary<string, Dictionary<string, object?>>>? Ini { get; set; }
    public string? SavedDirSuffix { get; set; }
    [Required]
    public string Name { get; set; } = "Unchained Server";
    public string Description { get; set; } = "";
    public string? Password { get; set; }
    [Required]
    public string StartingMap {
        get => Rotation?.Maps[Rotation.StartIndex] ?? ExplicitStartingMap ?? "FFA_Courtyard";
        set => ExplicitStartingMap = value;
    }
    public string? LocalIp { get; set; }
    [Range(1, 65535)]
    public int GamePort { get; set; } = 7777;
    [Range(1, 65535)]
    public int PingPort { get; set; } = 3075;
    [Range(1, 65535)]
    public int QueryPort { get; set; } = 7071;
    [Range(1, 65535)]
    public int RconPort { get; set; } = 9001;
    public bool Headless { get; set; } = true;
    public bool Register { get; set; }
    public bool DesyncPatch { get; set; } = true;
    public bool UseBackendBanlist { get; set; } = true;
    [EnumDataType(typeof(CensorArg))]
    public CensorArg CensorMode { get; set; } = CensorArg.None;
    public List<string>? ModActors { get; set; } = [];
    public List<string>? GameArgs { get; set; } = [];
    public DiscordSettings? Discord { get; set; }
}

public sealed class DiscordSettings {
    public string? BotToken { get; set; }
    public string? AdminChannelId { get; set; }
    public string? GeneralChannelId { get; set; }
    public string? DashboardChannelId { get; set; }
    public string? EventLogChannelId { get; set; }
    public string? AdminRoleId { get; set; }
    public bool MentionAdmins { get; set; } = true;
}
