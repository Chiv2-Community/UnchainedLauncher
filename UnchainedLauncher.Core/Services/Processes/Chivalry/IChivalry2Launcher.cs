using LanguageExt;
using LanguageExt.Common;
using System.Diagnostics;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
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
        public Task<Either<LaunchFailed, Process>> Launch(LaunchOptions options);
    }

    public abstract record UnchainedLaunchFailure(string Message, int Code, Option<Error> Underlying) : Expected(Message, Code, Underlying) {
        public record InjectionFailedError() : UnchainedLaunchFailure("Failed to inject process with unchained code.", 0, None);
        public record LaunchFailedError(LaunchFailed Failure) : UnchainedLaunchFailure(Failure.Message, Failure.Code, Failure.Underlying);
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
        string ServerBrowserBackend,
        string LaunchArgs,
        bool CheckForDependencyUpdates, //TODO: remove this property
        Option<string> SavedDirSuffix,
        Option<ServerLaunchOptions> ServerLaunchOptions
    ) {
        public IEnumerable<string> ToCLIArgs() {
            var args = new List<string> {
                $"--server-browser-backend {ServerBrowserBackend}"
            };
            ServerLaunchOptions.IfSome(opts => args.AddRange(opts.ToCLIArgs()));

            var suffix = SavedDirSuffix.IfNone("Unchained");
            args.Add($"-saveddirsuffix {suffix}");

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
        IEnumerable<String> NextMapModMarkers
    ) {
        public IEnumerable<String> ToCLIArgs() {
            var args = new List<string>();
            if (Headless) {
                args.Add("-nullrhi");
                args.Add("-unattended");
                args.Add("-nosound");
            }

            Password.IfSome(password => args.Add($"ServerPassword={password.Trim()}"));
            args.Add($"--next-map-name {Map}");
            args.Add($"Port={GamePort}");
            args.Add($"GameServerPingPort={BeaconPort}");
            args.Add($"GameServerQueryPort={QueryPort}");
            args.Add($"--rcon {RconPort}");
            args.Add($"--next-map-mod-markers {string.Join(",", NextMapModMarkers)}");

            return args;
        }
    };
}