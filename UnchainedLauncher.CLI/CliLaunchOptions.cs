using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.CLI;

/// <summary>Settings used by both client and server launches.</summary>
public sealed class SharedLaunchOptions {
    public string Chivalry2Path { get; }
    public string SavedDirSuffix { get; }
    public InstallationType Platform { get; }
    public string Backend { get; }
    public bool UpdatePlugin { get; }
    public bool AllowUnstablePlugin { get; }
    public bool UnsafeLoadAllDlls { get; private set; }
    internal void EnableUnsafeDllLoading() => UnsafeLoadAllDlls = true;
    public RegistryOptions RegistryOptions { get; }
    public IReadOnlyList<RegistrySpec> Registries => RegistryOptions.Registries;
    public IReadOnlyList<ReleaseCoordinates> Mods { get; }
    public IReadOnlyList<ReleaseCoordinates> Dlls { get; }
    public IReadOnlyList<string> GameArguments { get; }
    internal ProfileAssetPaths Assets { get; set; }
    internal ProtonConfiguration ProtonConfiguration { get; }
    internal ProtonRuntime? Proton { get; set; }

    internal SharedLaunchOptions(LaunchConfiguration configuration, string directory, string? savedDirSuffix = null, ServerSettings? server = null) {
        Chivalry2Path = Path.GetFullPath(configuration.Chiv2Path!, directory);
        SavedDirSuffix = savedDirSuffix ?? server?.SavedDirSuffix ?? (configuration as ClientConfiguration)?.SavedDirSuffix ?? "UnchainedCLI";
        Assets = new ProfileAssetPaths(SavedDirSuffix);
        var proton = configuration.Proton ?? new ProtonConfiguration();
        string? Absolute(string? value) => value == null ? null : Path.GetFullPath(value, directory);
        ProtonConfiguration = new ProtonConfiguration {
            Path = Absolute(proton.Path), CompatDataPath = Absolute(proton.CompatDataPath),
            SteamPath = Absolute(proton.SteamPath), HelperPath = Absolute(proton.HelperPath)
        };
        Platform = configuration.Platform!.Value;
        Backend = configuration.ServerBrowserBackend;
        UpdatePlugin = configuration.UpdatePlugin;
        AllowUnstablePlugin = configuration.AllowUnstablePlugin;
        GameArguments = Entries(configuration.GameArgs, allowEmpty: true).ToArray();
        var setup = server?.ModSetup ?? (configuration as ClientConfiguration)?.ModSetup ?? new ModSetupConfiguration();
        RegistryOptions = new RegistryOptions(setup.Registries, directory);
        Mods = Entries(setup.Mods).Select(ParseCoordinates).ToArray();
        Dlls = Entries(setup.Dlls).Select(ParseCoordinates).ToArray();
        if (!Uri.TryCreate(Backend, UriKind.Absolute, out var backend) || backend.Scheme is not ("http" or "https"))
            throw new ArgumentException("server-browser-backend must be an HTTP(S) URL.");
        if (Mods.GroupBy(m => (m.Org, m.ModuleName)).Any(g => g.Select(m => m.Version).Distinct().Count() > 1))
            throw new ArgumentException("Only one version of each mod may be selected.");
    }

    internal static IEnumerable<string> Entries(IEnumerable<string>? values, bool allowEmpty = false) =>
        (values ?? []).Select(value => value is null || !allowEmpty && string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Configuration lists must contain strings, not null or blank entries.") : value);

    public static ReleaseCoordinates ParseCoordinates(string value) {
        var match = Regex.Match(value, @"\A([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+):([^\s:/\\]+)\z");
        if (!match.Success) throw new ArgumentException($"Invalid coordinate '{value}'; expected organization/id:version (exact release tag).");
        return new ReleaseCoordinates(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
    }
}

public abstract class CliLaunchOptions : CliCommandOptions {
    private readonly SharedLaunchOptions? _shared;
    public SharedLaunchOptions Shared => _shared ?? throw new InvalidOperationException("Help is not a launch request.");
    public IReadOnlyList<string> GameArguments { get; }
    public ServerInstancePaths InstancePaths => new(Shared.Assets);
    internal bool AttachStdio => this is ServerOptions server && (server.Settings.AttachStdio ||
        GameArguments.Any(arg => arg is "--attach-stdio" or "-attach-stdio"));

    private protected CliLaunchOptions(SharedLaunchOptions shared, IEnumerable<string> gameArguments) {
        _shared = shared;
        GameArguments = shared.GameArguments.Concat(gameArguments).ToArray();
    }

    private protected CliLaunchOptions(string helpText) {
        Help = true;
        HelpText = helpText;
        GameArguments = [];
    }

    public static CliLaunchOptions Parse(string[] args) => CommandLineOptions.Parse(args) as CliLaunchOptions
        ?? throw new ArgumentException("Expected a client or server launch command.");

    internal static CliLaunchOptions FromConfiguration(LaunchConfiguration configuration, string directory) {
        return configuration switch {
            ServerConfiguration server => new ServerOptions(
                new SharedLaunchOptions(configuration, directory, server: server.Server),
                server.Server ?? new ServerSettings(), server.NoTui),
            ClientConfiguration client => new ClientOptions(new SharedLaunchOptions(configuration, directory), SharedLaunchOptions.Entries(client.Client?.GameArgs, allowEmpty: true)),
            _ => throw new ArgumentException("Expected client or server configuration.")
        };
    }

    public abstract LaunchOptions ToLaunchOptions();

    private protected LaunchOptions BuildLaunchOptions(IEnumerable<string> prefix, LanguageExt.Option<ServerLaunchOptions> server) {
        return new(
        EnabledReleases: Shared.Mods.Distinct(), ServerBrowserBackend: Some(Shared.Backend),
        LaunchArgs: string.Join(" ", prefix.Concat(GameArguments.Select(ArgumentEscaper.Escape))),
        CheckForDependencyUpdates: Shared.UpdatePlugin, AllowUnstablePluginReleases: Shared.AllowUnstablePlugin,
        SavedDirSuffix: Some(Shared.SavedDirSuffix), ServerLaunchOptions: server);
    }

    internal static CliLaunchOptions ForHelp(string text) => new HelpOptions(text);

    private sealed class HelpOptions : CliLaunchOptions {
        // Help is a parser result, never a launch; shared settings are not constructed.
        public HelpOptions(string text) : base(text) { }
        public override LaunchOptions ToLaunchOptions() => throw new InvalidOperationException("Help is not a launch request.");
    }
}

public sealed class ClientOptions : CliLaunchOptions {
    internal ClientOptions(SharedLaunchOptions shared, IEnumerable<string> arguments) : base(shared, arguments) { }
    public override LaunchOptions ToLaunchOptions() => BuildLaunchOptions(["TBL"], None);
}

public sealed class ServerOptions : CliLaunchOptions {
    internal IniOverrides Ini { get; }
    public ServerSettings Settings { get; }
    public bool NoTui { get; }
    // Chivalry removes hyphens when choosing its Saved directory (the plugin log path does not).
    internal string GameSavedDirSuffix => Shared.Assets.Suffix;

    internal ServerOptions(SharedLaunchOptions shared, ServerSettings settings, bool noTui)
        : base(shared, SharedLaunchOptions.Entries(settings.GameArgs, allowEmpty: true)) {
        Settings = settings;
        Ini = IniOverrides.Parse(settings.Ini);
        if (settings.Rotation is not null && Ini.Contains("Game", "/Script/TBL.TBLGameMode", "MapListIndex"))
            throw new ArgumentException("MapListIndex is managed by map-rotation; use starting-map to choose the first map.");
        NoTui = noTui;
        if (!settings.Headless && settings.DesyncPatch)
            throw new ArgumentException("Desync patch requires a headless server. To enable rendering, pass --no-desync-patch or set server.desync-patch: false in YAML.");
        if (new[] { settings.GamePort, settings.PingPort, settings.QueryPort, settings.RconPort }.Distinct().Count() != 4)
            throw new ArgumentException("Game, ping, query, and RCON ports must be distinct.");
        settings.LocalIp = string.IsNullOrWhiteSpace(settings.LocalIp) ? LocalAddressDetector.Detect().ToString() : settings.LocalIp.Trim();
        if (!IPAddress.TryParse(settings.LocalIp, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("local-ip must be an IPv4 address.");
        settings.ModActors = SharedLaunchOptions.Entries(settings.ModActors).Distinct().ToList();
        ToDiscordOptions();
    }

    public LanguageExt.Option<DiscordIntegrationLaunchOptions> ToDiscordOptions() {
        if (Settings.Discord is not { } discord) return None;
        var channels = new[] { discord.AdminChannelId, discord.GeneralChannelId, discord.DashboardChannelId, discord.EventLogChannelId };
        if (string.IsNullOrWhiteSpace(discord.BotToken) && channels.All(string.IsNullOrWhiteSpace) && string.IsNullOrWhiteSpace(discord.AdminRoleId)) return None;
        if (string.IsNullOrWhiteSpace(discord.BotToken) || channels.All(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Discord integration requires a bot token and at least one channel ID.");
        LanguageExt.Option<string> Text(string? value) => string.IsNullOrWhiteSpace(value) ? None : Some(value.Trim());
        return Some(new DiscordIntegrationLaunchOptions(discord.BotToken.Trim(),
            Text(discord.AdminChannelId), Text(discord.GeneralChannelId), Text(discord.DashboardChannelId),
            Text(discord.EventLogChannelId), Text(discord.AdminRoleId), discord.MentionAdmins));
    }

    public ServerLaunchOptions ToServerLaunchOptions() => new(
        Settings.Headless, Settings.Name, Settings.Description, RegisterWithBackend: Settings.Register,
        Password: string.IsNullOrWhiteSpace(Settings.Password) ? None : Some(Settings.Password.Trim()),
        Settings.StartingMap, Settings.GamePort, BeaconPort: Settings.PingPort, Settings.QueryPort, Settings.RconPort,
        FFATimeLimit: null, FFAScoreLimit: null, TDMTimeLimit: null, TDMTicketCount: null,
        PlayerBotCount: null, WarmupTime: null, LocalIp: Optional(Settings.LocalIp),
        ServerMods: Settings.ModActors ?? [], DiscordIntegration: ToDiscordOptions(),
        Settings.DesyncPatch, Settings.UseBackendBanlist, Settings.CensorMode) {
        SuppressServerNameOverride = Ini.Contains("Game", "/Script/TBL.TBLGameMode", "ServerName")
    };

    public override LaunchOptions ToLaunchOptions() {
        var prefix = new List<string> { "TBL", "-server" };
        if (Settings.AttachStdio && !GameArguments.Any(arg => arg is "--attach-stdio" or "-attach-stdio")) prefix.Add("--attach-stdio");
        if (Settings.NoSpawnConsole && !GameArguments.Any(arg => arg is "--no-spawn-console" or "-no-spawn-console")) prefix.Add("--no-spawn-console");
        if (!Ini.Contains("Game", "/Script/TBL.TBLTitleScreen", "bSavedHasAgreedToTOS"))
            prefix.Add("-ini:Game:[/Script/TBL.TBLTitleScreen]:bSavedHasAgreedToTOS=True");
        if (!Ini.Contains("Game", "/Script/TBL.TBLGameInstance", "FirstLoadCompleted"))
            prefix.Add("-ini:Game:[/Script/TBL.TBLGameInstance]:FirstLoadCompleted=True");
        prefix.AddRange(Ini.RawArguments);
        return BuildLaunchOptions(prefix, Some(ToServerLaunchOptions()));
    }
}
