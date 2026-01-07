using LanguageExt;
using LanguageExt.Common;
using System.Diagnostics;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry;

using static LanguageExt.Prelude;
public interface IChivalry2Launcher {
    /// <summary>
    /// Launches a vanilla game. Implementations may do extra work to enable client side pak file loading
    /// </summary>
    /// <param name="options"></param>
    /// <returns>
    /// Left if the game failed to launch.
    /// Right if the game was launched successfully.
    /// </returns>
    Task<Either<LaunchFailed, Process>> Launch(LaunchOptions options);
}

public abstract record UnchainedLaunchFailure(string Message, int Code, Option<Error> Underlying) : Expected(Message, Code, Underlying) {
    public record InjectionFailedError() : UnchainedLaunchFailure("Failed to inject process with unchained code.", 0, None);
    public record LaunchFailedError(LaunchFailed Failure) : UnchainedLaunchFailure(Failure.Message, 0, Failure.Underlying);
    public record DependencyDownloadFailedError(IEnumerable<string> DependencyNames)
        : UnchainedLaunchFailure($"Failed to download dependencies '{string.Join("', '", DependencyNames)}'", 0, None);
    public record LaunchCancelledError() : UnchainedLaunchFailure($"Launch Cancelled", 0, None);
    public record MissingArgumentsError() : UnchainedLaunchFailure("Necessary modded launch arguments not provided.", 0, None);


    public static UnchainedLaunchFailure InjectionFailed() =>
        new InjectionFailedError();
    public static UnchainedLaunchFailure LaunchFailed(LaunchFailed failure) => new LaunchFailedError(failure);
    public static UnchainedLaunchFailure DependencyDownloadFailed(IEnumerable<string> dependencyNames) =>
        new DependencyDownloadFailedError(dependencyNames);

    public static UnchainedLaunchFailure LaunchCancelled() => new LaunchCancelledError();
    public static UnchainedLaunchFailure MissingArguments() => new MissingArgumentsError();

    public LaunchFailed AsLaunchFailed(string args) => new LaunchFailed("unknown", args, this);
}

public record LaunchOptions(
    IEnumerable<ReleaseCoordinates> EnabledReleases,
    Option<string> ServerBrowserBackend,
    string LaunchArgs,
    bool CheckForDependencyUpdates, //TODO: remove this property
    Option<string> SavedDirSuffix,
    Option<ServerLaunchOptions> ServerLaunchOptions
) {
    public IReadOnlyList<CLIArg> ToCLIArgs() {
        var args = new List<CLIArg> {
            new RawArgs(LaunchArgs),
            new UEParameter("-savedirsuffix", SavedDirSuffix.IfNone("Unchained")),
            new Flag("-unchained")
        };

        ServerLaunchOptions.IfSome(opts => args.AddRange(opts.ToCLIArgs()));
        ServerBrowserBackend.IfSome(backend => args.Add(new Parameter("--server-browser-backend", backend)));

        return args;
    }
};

public record ServerLaunchOptions(
    bool Headless,
    string Name,
    string Description,
    Option<string> Password,
    string Map,
    int GamePort,
    int BeaconPort,
    int QueryPort,
    int RconPort,
    Option<string> LocalIp,
    IEnumerable<String> NextMapModActors
) {
    public IReadOnlyList<CLIArg> ToCLIArgs() {
        var args = new List<CLIArg>() {
            new UEINIParameter("Game", "/Script/TBL.TBLGameMode", "ServerName", Name),
            new UEINIParameter("Game", "/Script/TBL.TBLTitleScreen", "bSavedHasAgreedToTOS", "True"),
            new Parameter("--next-map-name", Map),
            new UEParameter("Port", GamePort.ToString()),
            new UEParameter("GameServerPingPort", BeaconPort.ToString()),
            new UEParameter("GameServerQueryPort", QueryPort.ToString()),
            new Parameter("-rcon", RconPort.ToString())
        };

        if (Headless) {
            args.Add(new Flag("-nullrhi"));
            args.Add(new Flag("-unattended"));
            args.Add(new Flag("-nosound"));
        }

        Password.IfSome(password => args.Add(new UEParameter("ServerPassword", password.Trim())));

        if (NextMapModActors.Any())
            args.Add(new Parameter("--next-map-mod-actors", string.Join(",", NextMapModActors)));

        LocalIp.IfSome(ip => args.Add(new Parameter("--local-ip", ip)));

        return args;
    }
};


public abstract record CLIArg(string Rendered);

public record RawArgs(string Args) : CLIArg(Args);
public record Flag(string FlagName) : CLIArg(FlagName);
public record Parameter(string ParamName, string Value) : CLIArg($"{ParamName} \"{Value}\"");
public record UEParameter(string ParamName, string Value) : CLIArg($"{ParamName}={Value}");
public record UEINIParameter(string Type, string Section, string Key, string Value) : CLIArg($"-ini:{Type}:[{Section}]:{Key}=\"{Value}\"");