using LanguageExt;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public record ModdedLaunchOptions(
        string ServerBrowserBackend,
        Option<string> SavedDirSuffix,
        Option<ServerLaunchOptions> ServerLaunchOptions
    ) {
        public IEnumerable<string> ToCLIArgs() {
            var args = new List<string> {
                $"--server-browser-backend {ServerBrowserBackend}"
            };
            ServerLaunchOptions.IfSome(opts => args.AddRange(opts.ToCLIArgs()));

            var suffix = SavedDirSuffix.IfNone("Unchained");
            args.Add($"--saved-dir-suffix {suffix}");

            return args;
        }
    };
}