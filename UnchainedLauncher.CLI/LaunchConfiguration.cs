using System.ComponentModel.DataAnnotations;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Processes.Chivalry;

namespace UnchainedLauncher.CLI;

public abstract class LaunchConfiguration {
    [Required]
    public string? Chiv2Path { get; set; }

    [Required, EnumDataType(typeof(InstallationType))]
    public InstallationType? Platform { get; set; }

    [Required]
    public string SavedDirSuffix { get; set; } = "UnchainedCLI";
    public string ServerBrowserBackend { get; set; } = "https://servers.polehammer.net";
    public bool UpdatePlugin { get; set; } = true;
    public bool AllowUnstablePlugin { get; set; }
    public List<string>? GameArgs { get; set; } = [];
    public ModSetupConfiguration? ModSetup { get; set; }

    public virtual void Validate() {
        Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true);
        if (Platform is not (InstallationType.Steam or InstallationType.EpicGamesStore))
            throw new ValidationException("Platform must be Steam or EGS (EpicGamesStore).");
    }
}

public sealed class ServerConfiguration : LaunchConfiguration {
    public bool NoTui { get; set; }
    public ServerSettings? Server { get; set; }
    // Recognized but intentionally untyped: do not validate the inactive schema.
    public object? Client { get; set; }

    public override void Validate() {
        base.Validate();
        if (Server is not null) Validator.ValidateObject(Server, new ValidationContext(Server), validateAllProperties: true);
    }
}

public sealed class ClientConfiguration : LaunchConfiguration {
    public ClientSettings? Client { get; set; }
    public object? Server { get; set; }
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
    [Required]
    public string Name { get; set; } = "Unchained Server";
    public string Description { get; set; } = "";
    public string? Password { get; set; }
    [Required]
    public string Map { get; set; } = "FFA_Courtyard";
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