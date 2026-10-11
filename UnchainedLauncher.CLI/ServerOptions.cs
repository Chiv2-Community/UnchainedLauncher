using System.Net;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.CLI;

public enum GamePlatform { Steam, EGS }

public sealed class ServerOptions {
    public string Chivalry2Path { get; private set; } = "";
    public string SavedDirSuffix { get; private set; } = "UnchainedCLI";
    public GamePlatform Platform { get; private set; }
    public string Name { get; private set; } = "Unchained Server";
    public string Description { get; private set; } = "";
    public string Map { get; private set; } = "FFA_Courtyard";
    public string? Password { get; private set; }
    public string? LocalIp { get; private set; }
    public string Backend { get; private set; } = "https://servers.polehammer.net";
    public int GamePort { get; private set; } = 7777;
    public int PingPort { get; private set; } = 3075;
    public int QueryPort { get; private set; } = 7071;
    public int RconPort { get; private set; } = 9001;
    public bool Register { get; private set; }
    public bool NoTui { get; private set; }
    public bool UpdatePlugin { get; private set; } = true;
    public bool AllowUnstablePlugin { get; private set; }
    public bool Help { get; private set; }
    public List<RegistrySpec> Registries { get; } = [];
    public List<ReleaseCoordinates> Mods { get; } = [];
    public List<ReleaseCoordinates> Dlls { get; } = [];
    public List<string> ServerMods { get; } = [];
    public List<string> GameArguments { get; } = [];

    public static ServerOptions Parse(string[] args) {
        var options = new ServerOptions();
        if (args.Length == 0 || args[0] is "--help" or "-h") {
            options.Help = true;
            return options;
        }
        if (args[0] != "server") throw new ArgumentException("Expected the 'server' subcommand.");
        var seen = new HashSet<string>();
        var platformProvided = false;
        for (var i = 1; i < args.Length; i++) {
            if (args[i] == "--") {
                options.GameArguments.AddRange(args.Skip(i + 1));
                break;
            }
            var pair = args[i].Split('=', 2);
            var key = pair[0];
            if (key is "--help" or "-h") { options.Help = true; return options; }
            if (key is not ("--add-registry" or "--mod" or "--dll" or "--server-mod") && !seen.Add(key))
                throw new ArgumentException($"Duplicate option: {key}");
            string Value() {
                var value = pair.Length == 2 ? pair[1]
                    : i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i]
                    : throw new ArgumentException($"Missing value for {key}.");
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Empty value for {key}.");
                return value;
            }
            void Flag() {
                if (pair.Length != 1) throw new ArgumentException($"{key} does not take a value.");
            }
            switch (key) {
                case "--chivalry2-path": options.Chivalry2Path = Path.GetFullPath(Value()); break;
                case "--saved-dir-suffix": options.SavedDirSuffix = Value(); break;
                case "--platform":
                    var platform = Value();
                    options.Platform = platform.ToLowerInvariant() switch {
                        "steam" => GamePlatform.Steam,
                        "egs" => GamePlatform.EGS,
                        _ => throw new ArgumentException("--platform must be Steam or EGS.")
                    };
                    platformProvided = true;
                    break;
                case "--add-registry": options.Registries.Add(RegistrySpec.Parse(Value())); break;
                case "--mod": options.Mods.Add(ParseCoordinates(Value())); break;
                case "--dll": options.Dlls.Add(ParseCoordinates(Value())); break;
                case "--server-mod": options.ServerMods.Add(Value()); break;
                case "--name": options.Name = Value(); break;
                case "--description": options.Description = Value(); break;
                case "--map": options.Map = Value(); break;
                case "--password": options.Password = Value(); break;
                case "--local-ip":
                    options.LocalIp = Value();
                    if (!IPAddress.TryParse(options.LocalIp, out _)) throw new ArgumentException("--local-ip must be an IP address.");
                    break;
                case "--server-browser-backend":
                    options.Backend = Value();
                    if (!Uri.TryCreate(options.Backend, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                        throw new ArgumentException("--server-browser-backend must be an HTTP(S) URL.");
                    break;
                case "--game-port": options.GamePort = ParsePort(Value(), key); break;
                case "--ping-port": options.PingPort = ParsePort(Value(), key); break;
                case "--query-port": options.QueryPort = ParsePort(Value(), key); break;
                case "--rcon-port": options.RconPort = ParsePort(Value(), key); break;
                case "--register": Flag(); options.Register = true; break;
                case "--no-tui": Flag(); options.NoTui = true; break;
                case "--no-plugin-update": Flag(); options.UpdatePlugin = false; break;
                case "--allow-unstable-plugin": Flag(); options.AllowUnstablePlugin = true; break;
                default: throw new ArgumentException($"Unknown option: {key}. Pass game arguments after --.");
            }
        }
        if (options.Chivalry2Path.Length == 0) throw new ArgumentException("--chivalry2-path is required.");
        if (!platformProvided) throw new ArgumentException("--platform is required (Steam or EGS).");
        if (!Regex.IsMatch(options.SavedDirSuffix, @"\A[A-Za-z0-9_-]+\z"))
            throw new ArgumentException("--saved-dir-suffix may contain only letters, digits, underscores, and hyphens.");
        if (new[] { options.GamePort, options.PingPort, options.QueryPort, options.RconPort }.Distinct().Count() != 4)
            throw new ArgumentException("Game, ping, query, and RCON ports must be distinct.");
        if (options.Mods.GroupBy(m => (m.Org, m.ModuleName)).Any(g => g.Select(m => m.Version).Distinct().Count() > 1))
            throw new ArgumentException("Only one version of each mod may be selected.");
        return options;
    }

    private static int ParsePort(string value, string option) =>
        int.TryParse(value, out var port) && port is >= 1 and <= 65535 ? port
            : throw new ArgumentException($"{option} must be a port between 1 and 65535.");

    public static ReleaseCoordinates ParseCoordinates(string value) {
        var match = Regex.Match(value, @"\A([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+):([^\s:/\\]+)\z");
        if (!match.Success) throw new ArgumentException($"Invalid coordinate '{value}'; expected organization/id:version (exact release tag).");
        return new ReleaseCoordinates(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
    }

    public ServerLaunchOptions ToServerLaunchOptions() => new(
        Headless: true, Name, Description, RegisterWithBackend: Register,
        Password: Optional(Password), Map, GamePort, BeaconPort: PingPort, QueryPort, RconPort,
        FFATimeLimit: null, FFAScoreLimit: null, TDMTimeLimit: null, TDMTicketCount: null,
        PlayerBotCount: null, WarmupTime: null, LocalIp: Optional(LocalIp),
        ServerMods: ServerMods.Distinct(), DiscordIntegration: None,
        DesyncPatch: false, UseBackendBanlist: false, CensorMode: CensorArg.None);

    public LaunchOptions ToLaunchOptions() => new(
        EnabledReleases: Mods.Distinct(), ServerBrowserBackend: Some(Backend),
        LaunchArgs: string.Join(" ", new[] { "TBL", "-server" }.Concat(GameArguments.Select(ArgumentEscaper.Escape))),
        CheckForDependencyUpdates: UpdatePlugin, AllowUnstablePluginReleases: AllowUnstablePlugin,
        SavedDirSuffix: Some(SavedDirSuffix), ServerLaunchOptions: Some(ToServerLaunchOptions()));

    public const string HelpText = """
        UnchainedLauncher.CLI server --chivalry2-path <directory> --platform Steam|EGS [options] [-- game-args]

        Server-only launcher; Windows is required for launching and DLL injection.
          --saved-dir-suffix <suffix>        Saved profile suffix (default: UnchainedCLI)
          --add-registry github:owner/repo   Add a GitHub registry (repeatable)
          --add-registry local:<directory>  Add a local registry (repeatable)
          --add-registry config:<json-file>  Add a Core registry configuration, including aggregates
          --mod <organization/id:version>   Install a mod at its exact release tag (repeatable)
          --dll <organization/id:version>   RESERVED: warns; still injects ALL Plugins DLLs
          --server-mod <blueprint-class>    Activate a server mod actor (repeatable)
          --name <name>                     Server name (default: Unchained Server)
          --description <text>              Description / MOTD
          --map <map>                       Initial map (default: FFA_Courtyard)
          --password <password>             Server password
          --game-port <port>                Default: 7777
          --ping-port <port>                Default: 3075
          --query-port <port>               A2S query port (default: 7071)
          --rcon-port <port>                Default: 9001
          --local-ip <address>              Local server address
          --register                        List server in the server browser (default: off)
          --server-browser-backend <url>    Default: https://servers.polehammer.net
          --no-plugin-update                Use the existing UnchainedPlugin.dll
          --allow-unstable-plugin            Allow prerelease plugin updates
          --no-tui                          Print periodic status instead of the live dashboard
          --help                            Show help

        The official GitHub registry is always included; additional registries are additive.
        Q or Ctrl+C stops the server. Redirected output automatically disables the TUI.
        EGS authentication arguments must be supplied after --; this does not log in to Epic.
        DLL package selection is tracked in https://github.com/Chiv2-Community/UnchainedLauncher/issues/149
        """;
}
