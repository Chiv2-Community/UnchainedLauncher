using System.Diagnostics;
using log4net;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Extensions;
using LanguageExt;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.Core {
    public class Chivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2Launcher));

        public static readonly string GameBinPath = FilePaths.BinDir + "\\Chivalry2-Win64-Shipping.exe";
        public static readonly string OriginalLauncherPath = "Chivalry2Launcher-ORIGINAL.exe";

        private static readonly LanguageExt.HashSet<int> GracefulExitCodes = new LanguageExt.HashSet<int> { 0, -1073741510 };

        /// <summary>
        /// The original launcher is used to launch the game with no mods.
        /// </summary>
        private static ProcessLauncher VanillaLauncher { get; } = new ProcessLauncher(OriginalLauncherPath, Directory.GetCurrentDirectory(), Prelude.Eff<IEnumerable<string>>(() => new List<string>()));

        /// <summary>
        /// The modded launcher is used to launch the game with mods. The DLLs here are the relative paths to the DLLs that are to be injected.
        /// </summary>
        private static ProcessLauncher ModdedLauncher { get; } = new ProcessLauncher(GameBinPath, FilePaths.BinDir, Prelude.Eff(() => Directory.EnumerateFiles(FilePaths.PluginDir, "*.dll")));

        public Chivalry2Launcher() {
        }

        public Either<ProcessLaunchFailure, Process> LaunchVanilla(IEnumerable<string> args) {
            logger.Info("Attempting to launch vanilla game.");
            logger.LogListInfo("Launch args: ", args);
            var launchResult = VanillaLauncher.Launch(string.Join(" ", args));

            launchResult.Match(
                Left: error => error.Match(
                    LaunchFailedError: e => logger.Error($"Failed to launch Chivalry 2. {e.ExecutablePath} {e.Args}", e.Underlying),
                    InjectionFailedError: e => logger.Error($"This should be impossible. Report a bug please", e.Underlying)
                ),
                Right: _ => logger.Info("Successfully launched Chivalry 2.")
            );

            return launchResult;
        }

        /// <summary>
        /// Launches the game with the provided launch options.
        /// </summary>
        /// <param name="installationType"></param>
        /// <param name="launchOptions"></param>
        /// <param name="serverLaunchOptions"></param>
        /// <param name="extraArgs"></param>
        /// <returns>
        /// None if the installation type is not set.
        /// Some if the game was launched successfully.
        ///     * Left if the game failed to launch.
        ///     * Right if the game was launched successfully.
        /// </returns>
        public Option<Either<ProcessLaunchFailure, Process>> LaunchModded(InstallationType installationType, ModdedLaunchOptions launchOptions, Option<ServerLaunchOptions> serverLaunchOptions, IEnumerable<string> extraArgs) {
            if (installationType == InstallationType.NotSet) return Prelude.None;

            logger.Info("Attempting to launch modded game.");

            var moddedLaunchArgs = extraArgs.ToList();
            var tblLoc = moddedLaunchArgs.IndexOf("TBL") + 1;

            var serverLaunchOpts = serverLaunchOptions.ToList().Bind(opts => opts.ToCLIArgs());
            var launchOpts = launchOptions.ToCLIArgs();

            moddedLaunchArgs.InsertRange(tblLoc, serverLaunchOpts);
            moddedLaunchArgs.InsertRange(tblLoc, launchOpts);

            logger.LogListInfo($"Launch args:", moddedLaunchArgs);

            var launchResult = ModdedLauncher.Launch(string.Join(" ", moddedLaunchArgs));


            launchResult.Match(
                Left: error => error.Match(
                    LaunchFailedError: e => logger.Error($"Failed to launch Chivalry 2 Unchained. {e.ExecutablePath} {e.Args}", e.Underlying),
                    InjectionFailedError: e => logger.Error($"Failed to inject DLLs into Chivalry 2 Unchained. {e.DllPaths}", e.Underlying)
                ),
                Right: _ => logger.Info("Successfully launched Chivalry 2 Unchained")
            );

            return Prelude.Some(launchResult);
        }
    }

    public record ServerLaunchOptions(
        bool Headless,
        Option<string> Password,
        Option<string> Map,
        Option<int> GamePort,
        Option<int> BeaconPort,
        Option<int> QueryPort,
        Option<int> RconPort
    ) {
        public IEnumerable<String> ToCLIArgs() {
            var args = new List<string>();
            if (Headless) {
                args.Add("-nullrhi");
                args.Add("-unattended");
                args.Add("-nosound");
            }

            Password.IfSome(password => args.Add($"ServerPassword={password.Trim()}"));
            Map.IfSome(map => args.Add($"--next-map-name {map}"));
            GamePort.IfSome(port => args.Add($"Port={port}"));
            BeaconPort.IfSome(port => args.Add($"GameServerPingPort={port}"));
            QueryPort.IfSome(port => args.Add($"GameServerQueryPort={port}"));
            RconPort.IfSome(port => args.Add($"--rcon {port}"));

            return args;
        }
    };




    public record ModdedLaunchOptions(
        string ServerBrowserBackend,
        Option<IEnumerable<Release>> EnabledMods,
        Option<string> SavedDirSuffix
    ) {
        public IEnumerable<string> ToCLIArgs() {
            var args = new List<string>();
            args.Add($"--server-browser-backend {ServerBrowserBackend}");
            EnabledMods.IfSome(mods => args.AddRange(mods.Select(mod => $"--mod {mod.Manifest.RepoUrl}")));
            SavedDirSuffix.IfSome(suffix => args.Add($"--saved-dir-suffix {suffix}"));
            return args;
        }
    };
}
