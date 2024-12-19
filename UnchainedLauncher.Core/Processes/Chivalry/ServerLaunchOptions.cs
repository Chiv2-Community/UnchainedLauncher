using LanguageExt;

namespace UnchainedLauncher.Core.Processes.Chivalry {
    public record ServerLaunchOptions(
        bool Headless,
        string Name,
        string Description,
        Option<string> Password,
        string Map,
        int GamePort,
        int BeaconPort,
        int QueryPort,
        int RconPort
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

            return args;
        }
    };
}